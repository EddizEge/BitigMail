using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BitigMail.Engine.Archive;

public sealed class ArchiveSearchIndex : IDisposable
{
    private readonly string _dbPath;
    private readonly string _indexDir;
    private readonly object _writeLock = new();
    private SqliteConnection? _connection;
    private bool _disposed;

    public ArchiveSearchIndex(string runtimeDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDir);
        _indexDir = Path.Combine(Path.GetFullPath(runtimeDir), "archives");
        Directory.CreateDirectory(_indexDir);
        _dbPath = Path.Combine(_indexDir, "archive-search.db");

        InitializeDatabase();
    }

    public string DatabasePath => _dbPath;

    private void InitializeDatabase()
    {
        lock (_writeLock)
        {
            try
            {
                OpenAndSetupDatabase();
            }
            catch (SqliteException)
            {
                _connection?.Dispose();
                _connection = null;
                SqliteConnection.ClearAllPools();

                if (File.Exists(_dbPath))
                {
                    string corruptBackup = _dbPath + $".corrupted-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                    try { File.Move(_dbPath, corruptBackup, overwrite: true); } catch { }
                    try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch { }
                    try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch { }
                }

                OpenAndSetupDatabase();
            }
        }
    }

    private void OpenAndSetupDatabase()
    {
        _connection?.Dispose();
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS archive_catalog (
                archive_id TEXT PRIMARY KEY,
                company_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                archive_name TEXT NOT NULL,
                company_name TEXT,
                project_name TEXT,
                source_kind TEXT NOT NULL,
                dialect TEXT NOT NULL,
                source_fingerprint TEXT NOT NULL,
                total_items INTEGER NOT NULL,
                total_size_bytes INTEGER NOT NULL,
                truncated_items_count INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                indexed_at_utc TEXT,
                index_generation INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_archive_catalog_scope ON archive_catalog(company_id, project_id);

            CREATE TABLE IF NOT EXISTS message_metadata (
                rowid INTEGER PRIMARY KEY AUTOINCREMENT,
                archive_id TEXT NOT NULL,
                company_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                item_ordinal INTEGER NOT NULL,
                item_id TEXT NOT NULL,
                relative_eml_path TEXT NOT NULL,
                original_folder TEXT NOT NULL,
                sender_display TEXT NOT NULL,
                sender_address TEXT NOT NULL,
                recipients_display TEXT NOT NULL,
                subject_raw TEXT NOT NULL,
                original_mime_date_utc TEXT,
                source_internal_date_utc TEXT,
                date_sort_key INTEGER,
                has_attachments INTEGER NOT NULL,
                attachment_names_raw TEXT NOT NULL,
                byte_length INTEGER NOT NULL,
                body_snippet TEXT NOT NULL,
                is_body_truncated INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (archive_id) REFERENCES archive_catalog(archive_id)
            );
            CREATE INDEX IF NOT EXISTS idx_msg_meta_scope ON message_metadata(company_id, project_id, archive_id);
            CREATE INDEX IF NOT EXISTS idx_msg_meta_item_id ON message_metadata(item_id);
            CREATE INDEX IF NOT EXISTS idx_msg_meta_date ON message_metadata(date_sort_key);
            CREATE INDEX IF NOT EXISTS idx_msg_meta_archive_item ON message_metadata(archive_id, item_id);

            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                subject_search,
                sender_search,
                recipient_search,
                body_search,
                attachment_search,
                tokenize='unicode61 remove_diacritics 0'
            );
        ";
        cmd.ExecuteNonQuery();

        try
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE message_metadata ADD COLUMN source_internal_date_utc TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch { }
    }

    public async Task IndexArchiveAsync(
        ArchiveManifest manifest,
        ArchiveStorageManager storage,
        Action<int, int>? progressCallback,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(storage);

        lock (_writeLock)
        {
            EnsureOpenConnection();
            using var transaction = _connection!.BeginTransaction();

            try
            {
                // 1. Delete existing rows for this archive if any (for reindex idempotency)
                using (var deleteFts = _connection.CreateCommand())
                {
                    deleteFts.Transaction = transaction;
                    deleteFts.CommandText = @"
                        DELETE FROM messages_fts 
                        WHERE rowid IN (SELECT rowid FROM message_metadata WHERE archive_id = $archiveId);
                    ";
                    deleteFts.Parameters.AddWithValue("$archiveId", manifest.ArchiveId);
                    deleteFts.ExecuteNonQuery();
                }

                using (var deleteMeta = _connection.CreateCommand())
                {
                    deleteMeta.Transaction = transaction;
                    deleteMeta.CommandText = "DELETE FROM message_metadata WHERE archive_id = $archiveId;";
                    deleteMeta.Parameters.AddWithValue("$archiveId", manifest.ArchiveId);
                    deleteMeta.ExecuteNonQuery();
                }

                // 2. Insert catalog record with status = 'indexing'
                using (var upsertCatalog = _connection.CreateCommand())
                {
                    upsertCatalog.Transaction = transaction;
                    upsertCatalog.CommandText = @"
                        INSERT INTO archive_catalog (
                            archive_id, company_id, project_id, archive_name, company_name, project_name,
                            source_kind, dialect, source_fingerprint, total_items, total_size_bytes,
                            truncated_items_count, status, created_at_utc, indexed_at_utc, index_generation
                        ) VALUES (
                            $archiveId, $companyId, $projectId, $archiveName, $companyName, $projectName,
                            $sourceKind, $dialect, $sourceFingerprint, $totalItems, $totalSizeBytes,
                            $truncatedCount, 'indexing', $createdAtUtc, NULL, 1
                        )
                        ON CONFLICT(archive_id) DO UPDATE SET
                            company_id = excluded.company_id,
                            project_id = excluded.project_id,
                            archive_name = excluded.archive_name,
                            company_name = excluded.company_name,
                            project_name = excluded.project_name,
                            source_kind = excluded.source_kind,
                            dialect = excluded.dialect,
                            source_fingerprint = excluded.source_fingerprint,
                            total_items = excluded.total_items,
                            total_size_bytes = excluded.total_size_bytes,
                            truncated_items_count = excluded.truncated_items_count,
                            status = 'indexing',
                            index_generation = archive_catalog.index_generation + 1;
                    ";
                    upsertCatalog.Parameters.AddWithValue("$archiveId", manifest.ArchiveId);
                    upsertCatalog.Parameters.AddWithValue("$companyId", manifest.CompanyId);
                    upsertCatalog.Parameters.AddWithValue("$projectId", manifest.ProjectId);
                    upsertCatalog.Parameters.AddWithValue("$archiveName", manifest.ArchiveName);
                    upsertCatalog.Parameters.AddWithValue("$companyName", (object?)manifest.CompanyName ?? DBNull.Value);
                    upsertCatalog.Parameters.AddWithValue("$projectName", (object?)manifest.ProjectName ?? DBNull.Value);
                    upsertCatalog.Parameters.AddWithValue("$sourceKind", manifest.SourceKind);
                    upsertCatalog.Parameters.AddWithValue("$dialect", manifest.Dialect);
                    upsertCatalog.Parameters.AddWithValue("$sourceFingerprint", manifest.SourceFingerprint);
                    upsertCatalog.Parameters.AddWithValue("$totalItems", manifest.TotalItems);
                    upsertCatalog.Parameters.AddWithValue("$totalSizeBytes", manifest.TotalSizeBytes);
                    upsertCatalog.Parameters.AddWithValue("$truncatedCount", manifest.TruncatedItemsCount);
                    upsertCatalog.Parameters.AddWithValue("$createdAtUtc", manifest.CreatedAtUtc.ToString("o"));
                    upsertCatalog.ExecuteNonQuery();
                }

                // 3. Index items
                int indexedCount = 0;
                int truncatedCount = 0;

                using var insertMetaCmd = _connection.CreateCommand();
                insertMetaCmd.Transaction = transaction;
                insertMetaCmd.CommandText = @"
                    INSERT INTO message_metadata (
                        archive_id, company_id, project_id, item_ordinal, item_id,
                        relative_eml_path, original_folder, sender_display, sender_address,
                        recipients_display, subject_raw, original_mime_date_utc, source_internal_date_utc, date_sort_key,
                        has_attachments, attachment_names_raw, byte_length, body_snippet, is_body_truncated
                    ) VALUES (
                        $archiveId, $companyId, $projectId, $itemOrdinal, $itemId,
                        $relativeEmlPath, $originalFolder, $senderDisplay, $senderAddress,
                        $recipientsDisplay, $subjectRaw, $originalMimeDateUtc, $sourceInternalDateUtc, $dateSortKey,
                        $hasAttachments, $attachmentNamesRaw, $byteLength, $bodySnippet, $isBodyTruncated
                    );
                    SELECT last_insert_rowid();
                ";

                using var insertFtsCmd = _connection.CreateCommand();
                insertFtsCmd.Transaction = transaction;
                insertFtsCmd.CommandText = @"
                    INSERT INTO messages_fts (
                        rowid, subject_search, sender_search, recipient_search, body_search, attachment_search
                    ) VALUES (
                        $rowid, $subjectSearch, $senderSearch, $recipientSearch, $bodySearch, $attachmentSearch
                    );
                ";

                foreach (var item in manifest.Items)
                {
                    ct.ThrowIfCancellationRequested();

                    using var emlStream = storage.OpenRawEmlStream(manifest.ArchiveId, item.RelativeEmlPath);
                    var parsed = ArchiveMimeParser.Parse(emlStream, item.ByteLength);

                    if (parsed.IsBodyTruncated)
                    {
                        truncatedCount++;
                    }

                    long? dateSortKey = (parsed.DateUtc ?? item.SourceInternalDateUtc)?.ToUnixTimeSeconds();
                    string attachmentNamesJoined = string.Join(" ", parsed.Attachments.Select(a => a.FileName));
                    string bodySnippet = parsed.BodyText.Length > 200 ? parsed.BodyText[..200] : parsed.BodyText;

                    insertMetaCmd.Parameters.Clear();
                    insertMetaCmd.Parameters.AddWithValue("$archiveId", manifest.ArchiveId);
                    insertMetaCmd.Parameters.AddWithValue("$companyId", manifest.CompanyId);
                    insertMetaCmd.Parameters.AddWithValue("$projectId", manifest.ProjectId);
                    insertMetaCmd.Parameters.AddWithValue("$itemOrdinal", item.Ordinal);
                    insertMetaCmd.Parameters.AddWithValue("$itemId", item.ItemId);
                    insertMetaCmd.Parameters.AddWithValue("$relativeEmlPath", item.RelativeEmlPath);
                    insertMetaCmd.Parameters.AddWithValue("$originalFolder", item.OriginalFolder);
                    insertMetaCmd.Parameters.AddWithValue("$senderDisplay", parsed.SenderDisplay);
                    insertMetaCmd.Parameters.AddWithValue("$senderAddress", parsed.SenderAddress);
                    insertMetaCmd.Parameters.AddWithValue("$recipientsDisplay", parsed.RecipientsDisplay);
                    insertMetaCmd.Parameters.AddWithValue("$subjectRaw", parsed.Subject);
                    insertMetaCmd.Parameters.AddWithValue("$originalMimeDateUtc", (object?)parsed.DateUtc?.ToString("o") ?? DBNull.Value);
                    insertMetaCmd.Parameters.AddWithValue("$sourceInternalDateUtc", (object?)item.SourceInternalDateUtc?.ToString("o") ?? DBNull.Value);
                    insertMetaCmd.Parameters.AddWithValue("$dateSortKey", (object?)dateSortKey ?? DBNull.Value);
                    insertMetaCmd.Parameters.AddWithValue("$hasAttachments", parsed.HasAttachments ? 1 : 0);
                    insertMetaCmd.Parameters.AddWithValue("$attachmentNamesRaw", attachmentNamesJoined);
                    insertMetaCmd.Parameters.AddWithValue("$byteLength", item.ByteLength);
                    insertMetaCmd.Parameters.AddWithValue("$bodySnippet", bodySnippet);
                    insertMetaCmd.Parameters.AddWithValue("$isBodyTruncated", parsed.IsBodyTruncated ? 1 : 0);

                    long rowId = (long)insertMetaCmd.ExecuteScalar()!;

                    insertFtsCmd.Parameters.Clear();
                    insertFtsCmd.Parameters.AddWithValue("$rowid", rowId);
                    insertFtsCmd.Parameters.AddWithValue("$subjectSearch", ArchiveSearchPolicy.NormalizeSearchText(parsed.Subject));
                    insertFtsCmd.Parameters.AddWithValue("$senderSearch", ArchiveSearchPolicy.NormalizeSearchText(parsed.SenderDisplay + " " + parsed.SenderAddress));
                    insertFtsCmd.Parameters.AddWithValue("$recipientSearch", ArchiveSearchPolicy.NormalizeSearchText(parsed.RecipientsDisplay));
                    insertFtsCmd.Parameters.AddWithValue("$bodySearch", ArchiveSearchPolicy.NormalizeSearchText(parsed.BodyText));
                    insertFtsCmd.Parameters.AddWithValue("$attachmentSearch", ArchiveSearchPolicy.NormalizeSearchText(attachmentNamesJoined));
                    insertFtsCmd.ExecuteNonQuery();

                    indexedCount++;
                    progressCallback?.Invoke(indexedCount, manifest.Items.Count);
                }

                // 4. Update catalog record to status = 'ready'
                using (var updateReady = _connection.CreateCommand())
                {
                    updateReady.Transaction = transaction;
                    updateReady.CommandText = @"
                        UPDATE archive_catalog SET
                            status = 'ready',
                            truncated_items_count = $truncatedCount,
                            indexed_at_utc = $now
                        WHERE archive_id = $archiveId;
                    ";
                    updateReady.Parameters.AddWithValue("$archiveId", manifest.ArchiveId);
                    updateReady.Parameters.AddWithValue("$truncatedCount", truncatedCount);
                    updateReady.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
                    updateReady.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }

                // Mark as index_failed outside transaction
                try
                {
                    using var failCmd = _connection.CreateCommand();
                    failCmd.CommandText = "UPDATE archive_catalog SET status = 'index_failed' WHERE archive_id = $archiveId;";
                    failCmd.Parameters.AddWithValue("$archiveId", manifest.ArchiveId);
                    failCmd.ExecuteNonQuery();
                }
                catch { }

                throw;
            }
        }

        await Task.CompletedTask;
    }

    public ArchiveSearchResponse Search(
        ArchiveSearchRequest request,
        IReadOnlyDictionary<string, ArchiveManifest> registeredManifests)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Validate Selections
        var selections = request.SelectedScopes.Select(s => s.ToSelection()).ToList();
        var validatedSelections = ArchiveSearchPolicy.ValidateSelections(selections);

        // Empty selection strictly returns empty result (Decision 1 & 6)
        if (validatedSelections.Count == 0)
        {
            return new ArchiveSearchResponse
            {
                TotalCount = 0,
                Page = request.Page,
                PageSize = request.PageSize,
                Items = new(),
                IndexHealthy = true
            };
        }

        // 2. Validate ownership of each selected scope against registered manifests
        foreach (var sel in validatedSelections)
        {
            if (!registeredManifests.TryGetValue(sel.ArchiveId, out var manifest))
            {
                throw new ArchiveSearchPolicyException($"Arşiv kayıtlı katalogda bulunamadı: {sel.ArchiveId}");
            }
            ArchiveSearchPolicy.EnsureOwnedScope(sel, new ArchiveScopeSelection(manifest.CompanyId, manifest.ProjectId, manifest.ArchiveId));
        }

        // 3. Compile FTS query and validate date bounds
        var compiledFtsQuery = ArchiveSearchPolicy.CompileLiteralQuery(request.Query, request.ResolveField());
        var dateBounds = ArchiveSearchPolicy.ParseDates(request.StartDate, request.EndDate);
        long offset = ArchiveSearchPolicy.ValidatePageAndGetOffset(request.Page, request.PageSize);

        lock (_writeLock)
        {
            EnsureOpenConnection();

            // Build parameterized SQL
            var whereClauses = new List<string>();
            var parameters = new List<SqliteParameter>();

            // Scope filter: archive_id IN (...)
            var archiveParamNames = new List<string>();
            for (int i = 0; i < validatedSelections.Count; i++)
            {
                string paramName = $"$arc{i}";
                archiveParamNames.Add(paramName);
                parameters.Add(new SqliteParameter(paramName, validatedSelections[i].ArchiveId));
            }
            whereClauses.Add($"m.archive_id IN ({string.Join(", ", archiveParamNames)})");

            // Folder filter
            if (!string.IsNullOrWhiteSpace(request.Folder) && !string.Equals(request.Folder, "all", StringComparison.OrdinalIgnoreCase))
            {
                whereClauses.Add("m.original_folder = $folder");
                parameters.Add(new SqliteParameter("$folder", request.Folder.Trim()));
            }

            // Has attachments filter
            if (request.HasAttachment.HasValue)
            {
                whereClauses.Add("m.has_attachments = $hasAttachments");
                parameters.Add(new SqliteParameter("$hasAttachments", request.HasAttachment.Value ? 1 : 0));
            }

            // Date bounds filter (UTC+03)
            if (dateBounds.StartUtcInclusive.HasValue)
            {
                whereClauses.Add("m.original_mime_date_utc IS NOT NULL AND m.date_sort_key >= $startEpoch");
                parameters.Add(new SqliteParameter("$startEpoch", dateBounds.StartUtcInclusive.Value.ToUnixTimeSeconds()));
            }
            if (dateBounds.EndUtcExclusive.HasValue)
            {
                whereClauses.Add("m.original_mime_date_utc IS NOT NULL AND m.date_sort_key < $endEpoch");
                parameters.Add(new SqliteParameter("$endEpoch", dateBounds.EndUtcExclusive.Value.ToUnixTimeSeconds()));
            }

            // FTS Match clause
            string fromClause;
            if (!string.IsNullOrEmpty(compiledFtsQuery))
            {
                fromClause = "FROM message_metadata m JOIN messages_fts f ON m.rowid = f.rowid";
                whereClauses.Add("messages_fts MATCH $ftsQuery");
                parameters.Add(new SqliteParameter("$ftsQuery", compiledFtsQuery));
            }
            else
            {
                fromClause = "FROM message_metadata m";
            }

            string whereSql = string.Join(" AND ", whereClauses);

            // 1. Total Count Query
            int totalCount = 0;
            using (var countCmd = _connection!.CreateCommand())
            {
                countCmd.CommandText = $"SELECT COUNT(*) {fromClause} WHERE {whereSql};";
                foreach (var p in parameters) countCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                totalCount = Convert.ToInt32(countCmd.ExecuteScalar()!);
            }

            // 2. Paginated Results Query
            var items = new List<ArchiveSearchResultItem>();
            using (var queryCmd = _connection!.CreateCommand())
            {
                queryCmd.CommandText = $@"
                    SELECT 
                        m.item_id, m.archive_id, m.company_id, m.project_id,
                        m.original_folder, m.sender_display, m.sender_address,
                        m.recipients_display, m.subject_raw, m.body_snippet,
                        m.original_mime_date_utc, m.has_attachments,
                        m.attachment_names_raw, m.byte_length, m.is_body_truncated,
                        m.source_internal_date_utc
                    {fromClause}
                    WHERE {whereSql}
                    ORDER BY m.date_sort_key DESC, m.rowid DESC
                    LIMIT $pageSize OFFSET $offset;
                ";
                foreach (var p in parameters) queryCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                queryCmd.Parameters.AddWithValue("$pageSize", request.PageSize);
                queryCmd.Parameters.AddWithValue("$offset", offset);

                using var reader = queryCmd.ExecuteReader();
                while (reader.Read())
                {
                    string itemId = reader.GetString(0);
                    string archiveId = reader.GetString(1);
                    string archiveName = registeredManifests.TryGetValue(archiveId, out var m) ? m.ArchiveName : archiveId;

                    DateTimeOffset? dateUtc = reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture);
                    DateTimeOffset? internalDateUtc = reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture);
                    string formattedDate = dateUtc?.ToString("dd.MM.yyyy HH:mm") ?? "-";

                    string attRaw = reader.GetString(12);
                    var attNames = string.IsNullOrWhiteSpace(attRaw)
                        ? new List<string>()
                        : attRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

                    items.Add(new ArchiveSearchResultItem
                    {
                        MessageId = $"{archiveId}:{itemId}",
                        ArchiveId = archiveId,
                        CompanyId = reader.GetString(2),
                        ProjectId = reader.GetString(3),
                        ArchiveName = archiveName,
                        OriginalFolder = reader.GetString(4),
                        Sender = reader.GetString(5),
                        SenderEmail = reader.GetString(6),
                        Recipients = reader.GetString(7),
                        Subject = reader.GetString(8),
                        Snippet = reader.GetString(9),
                        OriginalMimeDateUtc = dateUtc,
                        SourceInternalDateUtc = internalDateUtc,
                        FormattedDate = formattedDate,
                        HasAttachments = reader.GetInt32(11) == 1,
                        AttachmentNames = attNames,
                        SizeBytes = reader.GetInt64(13),
                        IsBodyTruncated = reader.GetInt32(14) == 1
                    });
                }
            }

            // Check if any selected archives have truncated items
            bool hasTruncatedInScope = registeredManifests
                .Where(kv => validatedSelections.Any(s => s.ArchiveId == kv.Key))
                .Any(kv => kv.Value.TruncatedItemsCount > 0);

            string? warning = hasTruncatedInScope
                ? "Seçili arşivlerde 512 KiB sınırını aşan gövdeler kısmi indekslenmiştir."
                : null;

            return new ArchiveSearchResponse
            {
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize,
                Items = items,
                IndexHealthy = true,
                Warning = warning
            };
        }
    }

    /// <summary>
    /// Verifies that a message exists in the search scope and matches all active search filters (Decision 5).
    /// Binds exact archive owner, physical item, and full current search/filter request (no first-match fallback).
    /// </summary>
    public (string ArchiveId, string RelativeEmlPath, string ItemId)? VerifyMessageInSearchScope(
        string messageId,
        ArchiveSearchRequest request,
        IReadOnlyDictionary<string, ArchiveManifest> registeredManifests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentNullException.ThrowIfNull(request);

        int colonIndex = messageId.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= messageId.Length - 1)
        {
            return null;
        }

        string targetArchiveId = messageId[..colonIndex];
        string targetItemId = messageId[(colonIndex + 1)..];

        var selections = request.SelectedScopes.Select(s => s.ToSelection()).ToList();
        var validatedSelections = ArchiveSearchPolicy.ValidateSelections(selections);
        if (validatedSelections.Count == 0) return null;

        var targetSelection = validatedSelections.FirstOrDefault(s => string.Equals(s.ArchiveId, targetArchiveId, StringComparison.Ordinal));
        if (targetSelection == null)
        {
            return null;
        }

        foreach (var sel in validatedSelections)
        {
            if (!registeredManifests.TryGetValue(sel.ArchiveId, out var m))
                return null;
            ArchiveSearchPolicy.EnsureOwnedScope(sel, new ArchiveScopeSelection(m.CompanyId, m.ProjectId, m.ArchiveId));
        }

        if (!registeredManifests.TryGetValue(targetArchiveId, out var targetManifest))
            return null;

        ArchiveSearchPolicy.EnsureOwnedScope(targetSelection, new ArchiveScopeSelection(targetManifest.CompanyId, targetManifest.ProjectId, targetManifest.ArchiveId));

        var compiledFtsQuery = ArchiveSearchPolicy.CompileLiteralQuery(request.Query, request.ResolveField());
        var dateBounds = ArchiveSearchPolicy.ParseDates(request.StartDate, request.EndDate);

        lock (_writeLock)
        {
            EnsureOpenConnection();

            var whereClauses = new List<string>();
            var parameters = new List<SqliteParameter>();

            whereClauses.Add("m.archive_id = $targetArchiveId");
            parameters.Add(new SqliteParameter("$targetArchiveId", targetArchiveId));

            whereClauses.Add("m.item_id = $targetItemId");
            parameters.Add(new SqliteParameter("$targetItemId", targetItemId));

            whereClauses.Add("m.company_id = $companyId");
            parameters.Add(new SqliteParameter("$companyId", targetManifest.CompanyId));

            whereClauses.Add("m.project_id = $projectId");
            parameters.Add(new SqliteParameter("$projectId", targetManifest.ProjectId));

            if (!string.IsNullOrWhiteSpace(request.Folder) && !string.Equals(request.Folder, "all", StringComparison.OrdinalIgnoreCase))
            {
                whereClauses.Add("m.original_folder = $folder");
                parameters.Add(new SqliteParameter("$folder", request.Folder.Trim()));
            }

            if (request.HasAttachment.HasValue)
            {
                whereClauses.Add("m.has_attachments = $hasAttachments");
                parameters.Add(new SqliteParameter("$hasAttachments", request.HasAttachment.Value ? 1 : 0));
            }

            if (dateBounds.StartUtcInclusive.HasValue)
            {
                whereClauses.Add("m.original_mime_date_utc IS NOT NULL AND m.date_sort_key >= $startEpoch");
                parameters.Add(new SqliteParameter("$startEpoch", dateBounds.StartUtcInclusive.Value.ToUnixTimeSeconds()));
            }
            if (dateBounds.EndUtcExclusive.HasValue)
            {
                whereClauses.Add("m.original_mime_date_utc IS NOT NULL AND m.date_sort_key < $endEpoch");
                parameters.Add(new SqliteParameter("$endEpoch", dateBounds.EndUtcExclusive.Value.ToUnixTimeSeconds()));
            }

            string fromClause;
            if (!string.IsNullOrEmpty(compiledFtsQuery))
            {
                fromClause = "FROM message_metadata m JOIN messages_fts f ON m.rowid = f.rowid";
                whereClauses.Add("messages_fts MATCH $ftsQuery");
                parameters.Add(new SqliteParameter("$ftsQuery", compiledFtsQuery));
            }
            else
            {
                fromClause = "FROM message_metadata m";
            }

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = $"SELECT m.archive_id, m.relative_eml_path, m.item_id {fromClause} WHERE {string.Join(" AND ", whereClauses)} LIMIT 1;";
            foreach (var p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }
            return null;
        }
    }

    public IReadOnlyList<ArchiveCatalogItemDto> GetCatalogItems()
    {
        lock (_writeLock)
        {
            EnsureOpenConnection();
            var list = new List<ArchiveCatalogItemDto>();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    archive_id, archive_name, company_id, project_id,
                    company_name, project_name, source_kind, dialect,
                    source_fingerprint, total_items, total_size_bytes,
                    truncated_items_count, status, created_at_utc, indexed_at_utc, index_generation
                FROM archive_catalog
                ORDER BY created_at_utc DESC;
            ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTimeOffset? indexedAt = reader.IsDBNull(14)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture);

                list.Add(new ArchiveCatalogItemDto
                {
                    ArchiveId = reader.GetString(0),
                    ArchiveName = reader.GetString(1),
                    CompanyId = reader.GetString(2),
                    ProjectId = reader.GetString(3),
                    CompanyName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ProjectName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SourceKind = reader.GetString(6),
                    Dialect = reader.GetString(7),
                    SourceFingerprint = reader.GetString(8),
                    TotalItems = reader.GetInt32(9),
                    TotalSizeBytes = reader.GetInt64(10),
                    TruncatedItemsCount = reader.GetInt32(11),
                    Status = reader.GetString(12),
                    CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture),
                    IndexedAtUtc = indexedAt,
                    IndexGeneration = reader.GetInt32(15)
                });
            }
            return list;
        }
    }

    public async Task RebuildDatabaseAsync(ArchiveStorageManager storage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var manifests = storage.GetAllArchiveManifests();

        string tempDbPath = Path.Combine(_indexDir, $"catalog-fts-{Guid.NewGuid():N}.tmp.db");

        lock (_writeLock)
        {
            // Build temporary DB
            using (var tempConn = new SqliteConnection($"Data Source={tempDbPath}"))
            {
                tempConn.Open();
                using var cmd = tempConn.CreateCommand();
                cmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;

                    CREATE TABLE schema_version (version INTEGER PRIMARY KEY, applied_at_utc TEXT NOT NULL);
                    CREATE TABLE archive_catalog (
                        archive_id TEXT PRIMARY KEY,
                        company_id TEXT NOT NULL,
                        project_id TEXT NOT NULL,
                        archive_name TEXT NOT NULL,
                        company_name TEXT,
                        project_name TEXT,
                        source_kind TEXT NOT NULL,
                        dialect TEXT NOT NULL,
                        source_fingerprint TEXT NOT NULL,
                        total_items INTEGER NOT NULL,
                        total_size_bytes INTEGER NOT NULL,
                        truncated_items_count INTEGER NOT NULL DEFAULT 0,
                        status TEXT NOT NULL,
                        created_at_utc TEXT NOT NULL,
                        indexed_at_utc TEXT,
                        index_generation INTEGER NOT NULL DEFAULT 1
                    );
                    CREATE TABLE message_metadata (
                        rowid INTEGER PRIMARY KEY AUTOINCREMENT,
                        archive_id TEXT NOT NULL,
                        company_id TEXT NOT NULL,
                        project_id TEXT NOT NULL,
                        item_ordinal INTEGER NOT NULL,
                        item_id TEXT NOT NULL,
                        relative_eml_path TEXT NOT NULL,
                        original_folder TEXT NOT NULL,
                        sender_display TEXT NOT NULL,
                        sender_address TEXT NOT NULL,
                        recipients_display TEXT NOT NULL,
                        subject_raw TEXT NOT NULL,
                        original_mime_date_utc TEXT,
                        source_internal_date_utc TEXT,
                        date_sort_key INTEGER,
                        has_attachments INTEGER NOT NULL,
                        attachment_names_raw TEXT NOT NULL,
                        byte_length INTEGER NOT NULL,
                        body_snippet TEXT NOT NULL,
                        is_body_truncated INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE VIRTUAL TABLE messages_fts USING fts5(
                        subject_search, sender_search, recipient_search, body_search, attachment_search,
                        tokenize='unicode61 remove_diacritics 0'
                    );
                ";
                cmd.ExecuteNonQuery();

                // Populate temp DB
                foreach (var manifest in manifests)
                {
                    ct.ThrowIfCancellationRequested();
                    // Insert into tempConn inside transaction
                    using var tx = tempConn.BeginTransaction();

                    using (var catCmd = tempConn.CreateCommand())
                    {
                        catCmd.Transaction = tx;
                        catCmd.CommandText = @"
                            INSERT INTO archive_catalog VALUES (
                                $aid, $cid, $pid, $aname, $cname, $pname,
                                $skind, $dia, $fp, $ti, $ts,
                                $tc, 'ready', $cat, $iat, 1
                            );
                        ";
                        catCmd.Parameters.AddWithValue("$aid", manifest.ArchiveId);
                        catCmd.Parameters.AddWithValue("$cid", manifest.CompanyId);
                        catCmd.Parameters.AddWithValue("$pid", manifest.ProjectId);
                        catCmd.Parameters.AddWithValue("$aname", manifest.ArchiveName);
                        catCmd.Parameters.AddWithValue("$cname", (object?)manifest.CompanyName ?? DBNull.Value);
                        catCmd.Parameters.AddWithValue("$pname", (object?)manifest.ProjectName ?? DBNull.Value);
                        catCmd.Parameters.AddWithValue("$skind", manifest.SourceKind);
                        catCmd.Parameters.AddWithValue("$dia", manifest.Dialect);
                        catCmd.Parameters.AddWithValue("$fp", manifest.SourceFingerprint);
                        catCmd.Parameters.AddWithValue("$ti", manifest.TotalItems);
                        catCmd.Parameters.AddWithValue("$ts", manifest.TotalSizeBytes);
                        catCmd.Parameters.AddWithValue("$tc", manifest.TruncatedItemsCount);
                        catCmd.Parameters.AddWithValue("$cat", manifest.CreatedAtUtc.ToString("o"));
                        catCmd.Parameters.AddWithValue("$iat", DateTimeOffset.UtcNow.ToString("o"));
                        catCmd.ExecuteNonQuery();
                    }

                    int truncCount = 0;
                    using var metaCmd = tempConn.CreateCommand();
                    metaCmd.Transaction = tx;
                    metaCmd.CommandText = @"
                        INSERT INTO message_metadata (
                            archive_id, company_id, project_id, item_ordinal, item_id,
                            relative_eml_path, original_folder, sender_display, sender_address,
                            recipients_display, subject_raw, original_mime_date_utc, source_internal_date_utc, date_sort_key,
                            has_attachments, attachment_names_raw, byte_length, body_snippet, is_body_truncated
                        ) VALUES (
                            $archiveId, $companyId, $projectId, $itemOrdinal, $itemId,
                            $relativeEmlPath, $originalFolder, $senderDisplay, $senderAddress,
                            $recipientsDisplay, $subjectRaw, $originalMimeDateUtc, $sourceInternalDateUtc, $dateSortKey,
                            $hasAttachments, $attachmentNamesRaw, $byteLength, $bodySnippet, $isBodyTruncated
                        );
                        SELECT last_insert_rowid();
                    ";

                    using var ftsCmd = tempConn.CreateCommand();
                    ftsCmd.Transaction = tx;
                    ftsCmd.CommandText = @"
                        INSERT INTO messages_fts (
                            rowid, subject_search, sender_search, recipient_search, body_search, attachment_search
                        ) VALUES (
                            $rowid, $subjectSearch, $senderSearch, $recipientSearch, $bodySearch, $attachmentSearch
                        );
                    ";

                    foreach (var item in manifest.Items)
                    {
                        using var emlStream = storage.OpenRawEmlStream(manifest.ArchiveId, item.RelativeEmlPath);
                        var parsed = ArchiveMimeParser.Parse(emlStream, item.ByteLength);
                        if (parsed.IsBodyTruncated) truncCount++;

                        long? dateSortKey = (parsed.DateUtc ?? item.SourceInternalDateUtc)?.ToUnixTimeSeconds();
                        string attachmentNamesJoined = string.Join(" ", parsed.Attachments.Select(a => a.FileName));
                        string bodySnippet = parsed.BodyText.Length > 200 ? parsed.BodyText[..200] : parsed.BodyText;

                        metaCmd.Parameters.Clear();
                        metaCmd.Parameters.AddWithValue("$archiveId", manifest.ArchiveId);
                        metaCmd.Parameters.AddWithValue("$companyId", manifest.CompanyId);
                        metaCmd.Parameters.AddWithValue("$projectId", manifest.ProjectId);
                        metaCmd.Parameters.AddWithValue("$itemOrdinal", item.Ordinal);
                        metaCmd.Parameters.AddWithValue("$itemId", item.ItemId);
                        metaCmd.Parameters.AddWithValue("$relativeEmlPath", item.RelativeEmlPath);
                        metaCmd.Parameters.AddWithValue("$originalFolder", item.OriginalFolder);
                        metaCmd.Parameters.AddWithValue("$senderDisplay", parsed.SenderDisplay);
                        metaCmd.Parameters.AddWithValue("$senderAddress", parsed.SenderAddress);
                        metaCmd.Parameters.AddWithValue("$recipientsDisplay", parsed.RecipientsDisplay);
                        metaCmd.Parameters.AddWithValue("$subjectRaw", parsed.Subject);
                        metaCmd.Parameters.AddWithValue("$originalMimeDateUtc", (object?)parsed.DateUtc?.ToString("o") ?? DBNull.Value);
                        metaCmd.Parameters.AddWithValue("$sourceInternalDateUtc", (object?)item.SourceInternalDateUtc?.ToString("o") ?? DBNull.Value);
                        metaCmd.Parameters.AddWithValue("$dateSortKey", (object?)dateSortKey ?? DBNull.Value);
                        metaCmd.Parameters.AddWithValue("$hasAttachments", parsed.HasAttachments ? 1 : 0);
                        metaCmd.Parameters.AddWithValue("$attachmentNamesRaw", attachmentNamesJoined);
                        metaCmd.Parameters.AddWithValue("$byteLength", item.ByteLength);
                        metaCmd.Parameters.AddWithValue("$bodySnippet", bodySnippet);
                        metaCmd.Parameters.AddWithValue("$isBodyTruncated", parsed.IsBodyTruncated ? 1 : 0);

                        long rowId = (long)metaCmd.ExecuteScalar()!;

                        ftsCmd.Parameters.Clear();
                        ftsCmd.Parameters.AddWithValue("$rowid", rowId);
                        ftsCmd.Parameters.AddWithValue("$subjectSearch", ArchiveSearchPolicy.NormalizeSearchText(parsed.Subject));
                        ftsCmd.Parameters.AddWithValue("$senderSearch", ArchiveSearchPolicy.NormalizeSearchText(parsed.SenderDisplay + " " + parsed.SenderAddress));
                        ftsCmd.Parameters.AddWithValue("$recipientSearch", ArchiveSearchPolicy.NormalizeSearchText(parsed.RecipientsDisplay));
                        ftsCmd.Parameters.AddWithValue("$bodySearch", ArchiveSearchPolicy.NormalizeSearchText(parsed.BodyText));
                        ftsCmd.Parameters.AddWithValue("$attachmentSearch", ArchiveSearchPolicy.NormalizeSearchText(attachmentNamesJoined));
                        ftsCmd.ExecuteNonQuery();
                    }

                    using (var upCmd = tempConn.CreateCommand())
                    {
                        upCmd.Transaction = tx;
                        upCmd.CommandText = "UPDATE archive_catalog SET truncated_items_count = $tc WHERE archive_id = $aid;";
                        upCmd.Parameters.AddWithValue("$tc", truncCount);
                        upCmd.Parameters.AddWithValue("$aid", manifest.ArchiveId);
                        upCmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
            }

            // Close active connection and clear pools
            _connection?.Dispose();
            _connection = null;
            SqliteConnection.ClearAllPools();

            // Backup old DB if exists
            if (File.Exists(_dbPath))
            {
                string corruptBackup = _dbPath + $".corrupted-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                try { File.Move(_dbPath, corruptBackup, overwrite: true); } catch { }
                try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch { }
                try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch { }
            }

            // Move temp DB to active DB
            File.Move(tempDbPath, _dbPath, overwrite: true);

            // Reopen active connection
            InitializeDatabase();
        }

        await Task.CompletedTask;
    }

    private void EnsureOpenConnection()
    {
        if (_connection == null || _connection.State != ConnectionState.Open)
        {
            InitializeDatabase();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _connection?.Dispose();
            _connection = null;
        }
    }
}
