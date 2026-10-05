using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BitigMail.Engine.Bridge;

namespace BitigMail.LocalHost.Bridge.Transfer;

/// <summary>
/// Durable snapshots for file/account bridge transfers.
/// A damaged record is never equivalent to an absent record; fail-closed integrity.
/// </summary>
public sealed class BridgeTransferJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _importPlansDir;
    private readonly string _importJournalsDir;
    private readonly string _exportPlansDir;
    private readonly string _exportJournalsDir;
    private readonly object _lock = new();
    private readonly HashSet<string> _observedJournals = new(StringComparer.Ordinal);

    internal Action<string>? OnBeforeAtomicWrite { get; set; }
    internal Action<int>? OnAtomicPublicationRetry { get; set; }
    internal Action<BridgeImportJournalEntry>? OnBeforeWriteImportAppendIntent { get; set; }
    internal Action<BridgeImportJournalEntry>? OnBeforeWriteImportVerified { get; set; }
    internal Action<BridgeExportJournalEntry>? OnBeforeWriteExportVerified { get; set; }

    public BridgeTransferJournal(string runtimeDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDir);
        string baseDir = Path.Combine(Path.GetFullPath(runtimeDir), "bridge-transfers");
        _importPlansDir = Path.Combine(baseDir, "import-plans");
        _importJournalsDir = Path.Combine(baseDir, "import-journals");
        _exportPlansDir = Path.Combine(baseDir, "export-plans");
        _exportJournalsDir = Path.Combine(baseDir, "export-journals");

        Directory.CreateDirectory(_importPlansDir);
        Directory.CreateDirectory(_importJournalsDir);
        Directory.CreateDirectory(_exportPlansDir);
        Directory.CreateDirectory(_exportJournalsDir);
    }

    public string ImportPlansDirectory => _importPlansDir;
    public string ImportJournalsDirectory => _importJournalsDir;
    public string ExportPlansDirectory => _exportPlansDir;
    public string ExportJournalsDirectory => _exportJournalsDir;

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128 ||
            id.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
            throw new ArgumentException("Geçersiz aktarım kaydı kimliği.");
    }

    private static InvalidOperationException DamagedRecord() =>
        new("Aktarım kaydı okunamadı veya bütünlüğü bozuldu. Veri bütünlüğünü korumak için işlem durduruldu.");

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? throw DamagedRecord();
        }
        catch (FileNotFoundException) { return null; }
        catch (JsonException) { throw DamagedRecord(); }
        catch (IOException) { throw DamagedRecord(); }
        catch (UnauthorizedAccessException) { throw DamagedRecord(); }
    }

    // --- Import Plan and Journal Operations ---

    private static void ValidateImportPlan(BridgeImportPlan plan, string id)
    {
        _ = BitigMail.Engine.Models.FrozenMailFilter.Validate(plan.AdvancedFilterCanonicalJson, plan.AdvancedFilterFingerprint, plan.AdvancedFilterUnknownCount, plan.DateFilterBlocked);
        if (plan.PlanId != id || plan.PreviewId != id || string.IsNullOrWhiteSpace(plan.CompanyId) ||
            string.IsNullOrWhiteSpace(plan.ProjectId) || string.IsNullOrWhiteSpace(plan.SourceHandle) ||
            string.IsNullOrWhiteSpace(plan.TargetAccountId) || plan.Items == null ||
            plan.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.ItemId) || string.IsNullOrWhiteSpace(i.SourceRelativePath) ||
                string.IsNullOrWhiteSpace(i.TargetFolder) || string.IsNullOrWhiteSpace(i.SourceSha256) || string.IsNullOrWhiteSpace(i.CanonicalSha256)) ||
            plan.Items.Select(i => i.ItemId).Distinct(StringComparer.Ordinal).Count() != plan.Items.Count)
            throw DamagedRecord();
    }

    public void SaveImportPlan(BridgeImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateId(plan.PlanId);
        ValidateImportPlan(plan, plan.PlanId);
        lock (_lock)
        {
            AtomicWrite(Path.Combine(_importPlansDir, plan.PlanId + ".json"), JsonSerializer.SerializeToUtf8Bytes(plan, JsonOptions), false);
        }
    }

    public BridgeImportPlan? GetImportPlan(string planId)
    {
        ValidateId(planId);
        lock (_lock)
        {
            var plan = Read<BridgeImportPlan>(Path.Combine(_importPlansDir, planId + ".json"));
            if (plan != null) ValidateImportPlan(plan, planId);
            return plan;
        }
    }

    public BridgeImportJournalState InitializeImportJournal(string jobId, BridgeImportPlan plan)
    {
        ValidateId(jobId);
        ArgumentNullException.ThrowIfNull(plan);
        lock (_lock)
        {
            if (_observedJournals.Contains(jobId)) throw DamagedRecord();
            var frozen = GetImportPlan(plan.PlanId) ?? throw DamagedRecord();
            var state = new BridgeImportJournalState { JobId = jobId, PlanId = frozen.PlanId };
            foreach (var item in frozen.Items)
            {
                state.Entries.Add(item.ItemId, new BridgeImportJournalEntry
                {
                    ItemId = item.ItemId,
                    SourceRelativePath = item.SourceRelativePath,
                    PhysicalOrdinal = item.PhysicalOrdinal,
                    TargetFolder = item.TargetFolder,
                    ExpectedSha256 = item.CanonicalSha256
                });
            }
            AtomicWrite(Path.Combine(_importJournalsDir, jobId + ".json"), JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), false);
            _observedJournals.Add(jobId);
            return state;
        }
    }

    public BridgeImportJournalState? GetImportJournal(string jobId)
    {
        ValidateId(jobId);
        lock (_lock)
        {
            var state = Read<BridgeImportJournalState>(Path.Combine(_importJournalsDir, jobId + ".json"));
            if (state == null)
            {
                if (_observedJournals.Contains(jobId)) throw DamagedRecord();
                return null;
            }
            _observedJournals.Add(jobId);
            if (state.JobId != jobId || string.IsNullOrEmpty(state.PlanId) || state.Entries == null) throw DamagedRecord();
            var plan = GetImportPlan(state.PlanId) ?? throw DamagedRecord();
            if (state.Entries.Count != plan.Items.Count) throw DamagedRecord();
            foreach (var item in plan.Items)
            {
                if (!state.Entries.TryGetValue(item.ItemId, out var entry) || entry == null || entry.ItemId != item.ItemId ||
                    entry.SourceRelativePath != item.SourceRelativePath || entry.PhysicalOrdinal != item.PhysicalOrdinal ||
                    entry.TargetFolder != item.TargetFolder || entry.ExpectedSha256 != item.CanonicalSha256 || !Enum.IsDefined(entry.Status))
                    throw DamagedRecord();
            }
            return state;
        }
    }

    public void UpdateImportEntry(string jobId, BridgeImportJournalEntry entry)
    {
        ValidateId(jobId);
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            var state = GetImportJournal(jobId) ?? throw DamagedRecord();
            if (!state.Entries.TryGetValue(entry.ItemId, out var old) ||
                old.SourceRelativePath != entry.SourceRelativePath || old.PhysicalOrdinal != entry.PhysicalOrdinal ||
                old.TargetFolder != entry.TargetFolder || old.ExpectedSha256 != entry.ExpectedSha256) throw DamagedRecord();
            if (entry.Status == BridgeItemStatus.AppendIntent) OnBeforeWriteImportAppendIntent?.Invoke(entry);
            if (entry.Status == BridgeItemStatus.Verified) OnBeforeWriteImportVerified?.Invoke(entry);
            state.Entries[entry.ItemId] = entry;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AtomicWrite(Path.Combine(_importJournalsDir, jobId + ".json"), JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), true);
        }
    }

    // --- Export Plan and Journal Operations ---

    private static void ValidateExportPlan(BridgeExportPlan plan, string id)
    {
        _ = BitigMail.Engine.Models.FrozenMailFilter.Validate(plan.AdvancedFilterCanonicalJson, plan.AdvancedFilterFingerprint, plan.AdvancedFilterUnknownCount);
        if (plan.PlanId != id || plan.PreviewId != id || string.IsNullOrWhiteSpace(plan.CompanyId) ||
            string.IsNullOrWhiteSpace(plan.ProjectId) || string.IsNullOrWhiteSpace(plan.SourceAccountId) ||
            string.IsNullOrWhiteSpace(plan.TargetDirHandle) || plan.Items == null ||
            plan.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.ItemId) || string.IsNullOrWhiteSpace(i.SourceFolder) ||
                string.IsNullOrWhiteSpace(i.FolderKey) || string.IsNullOrWhiteSpace(i.SourceSha256) || string.IsNullOrWhiteSpace(i.RelativeOutputPath)) ||
            plan.Items.Select(i => i.ItemId).Distinct(StringComparer.Ordinal).Count() != plan.Items.Count)
            throw DamagedRecord();
    }

    public void SaveExportPlan(BridgeExportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateId(plan.PlanId);
        ValidateExportPlan(plan, plan.PlanId);
        lock (_lock)
        {
            AtomicWrite(Path.Combine(_exportPlansDir, plan.PlanId + ".json"), JsonSerializer.SerializeToUtf8Bytes(plan, JsonOptions), false);
        }
    }

    public BridgeExportPlan? GetExportPlan(string planId)
    {
        ValidateId(planId);
        lock (_lock)
        {
            var plan = Read<BridgeExportPlan>(Path.Combine(_exportPlansDir, planId + ".json"));
            if (plan != null) ValidateExportPlan(plan, planId);
            return plan;
        }
    }

    public BridgeExportJournalState InitializeExportJournal(string jobId, BridgeExportPlan plan, string jobOutputDir)
    {
        ValidateId(jobId);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobOutputDir);
        lock (_lock)
        {
            if (_observedJournals.Contains(jobId)) throw DamagedRecord();
            var frozen = GetExportPlan(plan.PlanId) ?? throw DamagedRecord();
            var state = new BridgeExportJournalState
            {
                JobId = jobId,
                PlanId = frozen.PlanId,
                JobOutputDir = jobOutputDir
            };
            foreach (var item in frozen.Items)
            {
                state.Entries.Add(item.ItemId, new BridgeExportJournalEntry
                {
                    ItemId = item.ItemId,
                    SourceFolder = item.SourceFolder,
                    SourceUid = item.SourceUid,
                    SourceUidValidity = item.SourceUidValidity,
                    FolderKey = item.FolderKey,
                    RelativeOutputPath = item.RelativeOutputPath,
                    ExpectedSha256 = item.SourceSha256
                });
            }
            AtomicWrite(Path.Combine(_exportJournalsDir, jobId + ".json"), JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), false);
            _observedJournals.Add(jobId);
            return state;
        }
    }

    public BridgeExportJournalState? GetExportJournal(string jobId)
    {
        ValidateId(jobId);
        lock (_lock)
        {
            var state = Read<BridgeExportJournalState>(Path.Combine(_exportJournalsDir, jobId + ".json"));
            if (state == null)
            {
                if (_observedJournals.Contains(jobId)) throw DamagedRecord();
                return null;
            }
            _observedJournals.Add(jobId);
            if (state.JobId != jobId || string.IsNullOrEmpty(state.PlanId) || string.IsNullOrEmpty(state.JobOutputDir) || state.Entries == null)
                throw DamagedRecord();
            var plan = GetExportPlan(state.PlanId) ?? throw DamagedRecord();
            if (state.Entries.Count != plan.Items.Count) throw DamagedRecord();
            foreach (var item in plan.Items)
            {
                if (!state.Entries.TryGetValue(item.ItemId, out var entry) || entry == null || entry.ItemId != item.ItemId ||
                    entry.SourceFolder != item.SourceFolder || entry.SourceUid != item.SourceUid || entry.SourceUidValidity != item.SourceUidValidity ||
                    entry.FolderKey != item.FolderKey || entry.RelativeOutputPath != item.RelativeOutputPath ||
                    entry.ExpectedSha256 != item.SourceSha256 || !Enum.IsDefined(entry.Status))
                    throw DamagedRecord();
            }
            return state;
        }
    }

    public void UpdateExportEntry(string jobId, BridgeExportJournalEntry entry)
    {
        ValidateId(jobId);
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            var state = GetExportJournal(jobId) ?? throw DamagedRecord();
            if (!state.Entries.TryGetValue(entry.ItemId, out var old) ||
                old.SourceFolder != entry.SourceFolder || old.SourceUid != entry.SourceUid || old.SourceUidValidity != entry.SourceUidValidity ||
                old.FolderKey != entry.FolderKey || old.RelativeOutputPath != entry.RelativeOutputPath ||
                old.ExpectedSha256 != entry.ExpectedSha256) throw DamagedRecord();
            if (entry.Status == BridgeItemStatus.Verified) OnBeforeWriteExportVerified?.Invoke(entry);
            state.Entries[entry.ItemId] = entry;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AtomicWrite(Path.Combine(_exportJournalsDir, jobId + ".json"), JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), true);
        }
    }

    private void AtomicWrite(string targetPath, byte[] data, bool overwrite)
    {
        OnBeforeAtomicWrite?.Invoke(targetPath);
        string tmpPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(true);
            }
            BitigMail.LocalHost.Jobs.JobSnapshotPersistence.Publish(tmpPath, targetPath, overwrite, OnAtomicPublicationRetry);
        }
        finally
        {
            try { File.Delete(tmpPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
