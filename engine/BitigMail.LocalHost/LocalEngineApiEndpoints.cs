using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.OAuth;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.OAuth;
using BitigMail.LocalHost.Security;
using BitigMail.LocalHost.Recovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost;

public static class LocalEngineApiEndpoints
{
    private static bool CanReadJob(HttpContext http,IdentityCatalog catalog,LocalJobRecord job)
    {
        if(http.Items[typeof(AuthenticatedSessionPrincipal)] is not AuthenticatedSessionPrincipal actor)return true;
        return http.RequestServices.GetService(typeof(JobManager)) is JobManager jobs
            ? jobs.CanAccessJob(actor,job)
            : (string.IsNullOrWhiteSpace(job.ActorUserId)||job.ActorUserId==actor.UserId||catalog.IsAdmin(actor))&&catalog.CanAccess(actor,job.ClientContext.CompanyId,job.ClientContext.ProjectId);
    }
    public static void MapLocalEngineEndpoints(this WebApplication app)
    {
        // 1. Session handshake
        app.MapPost("/api/session", (SessionManager sessionManager) =>
        {
            string token = sessionManager.CreateSession();
            return Results.Ok(new { token, version = "0.1.0" });
        });

        // 2. Session status
        app.MapGet("/api/session/status", (SessionManager sessionManager) =>
        {
            return Results.Ok(new { status = "ready", hasActiveSession = sessionManager.HasActiveSession });
        });

        // 3. Source OST Native Picker
        app.MapPost("/api/picker/source", async (IFilePickerService pickerService) =>
        {
            var result = await pickerService.PickSourceOstAsync();
            return Results.Ok(result);
        });

        // 4. Target PST Native Picker
        app.MapPost("/api/picker/target", async (IFilePickerService pickerService, TargetPickerRequest? req) =>
        {
            var result = await pickerService.PickTargetPstAsync(req?.SourceHandle);
            return Results.Ok(result);
        });

        // 5. Source OST Analysis (Server binds analysis to source handle)
        app.MapPost("/api/source/analyze", async (FileHandleRegistry handleRegistry, OstAnalyzer analyzer, AnalyzeRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req?.SourceHandle))
            {
                return Results.BadRequest(new { error = "sourceHandle parametresi zorunludur." });
            }

            string? sourcePath = handleRegistry.GetSourcePath(req.SourceHandle);
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                return Results.BadRequest(new { error = "Geçersiz veya süresi dolmuş kaynak tanıtıcısı. Lütfen dosyayı tekrar seçin." });
            }

            try
            {
                var result = await analyzer.AnalyzeAsync(sourcePath);
                handleRegistry.AttachAnalysis(req.SourceHandle, result);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 5b. Source Selection Preview (Authoritative immutable server-bound preview)
        app.MapPost("/api/source/selection/preview", HandleSelectionPreview);

        // 6. Start Conversion Job (Server relies solely on server-bound analysis and selection)
        app.MapPost("/api/jobs/start", (FileHandleRegistry handleRegistry, JobManager jobManager, StartJobRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req?.SourceHandle) || string.IsNullOrWhiteSpace(req?.TargetHandle))
            {
                return Results.BadRequest(new { error = "sourceHandle ve targetHandle parametreleri zorunludur." });
            }

            if (req.SourceHandle.StartsWith("msrc_"))
            {
                return HandleMimeStart(handleRegistry, jobManager, req);
            }

            var sourceEntry = handleRegistry.GetSourceEntry(req.SourceHandle);
            string? targetPath = handleRegistry.GetTargetPath(req.TargetHandle);

            if (sourceEntry == null || string.IsNullOrEmpty(sourceEntry.FullPath) || !File.Exists(sourceEntry.FullPath))
            {
                return Results.BadRequest(new { error = "Kaynak dosya bulunamadı veya tanıtıcı geçersiz." });
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                return Results.BadRequest(new { error = "Hedef dosya tanıtıcısı geçersiz." });
            }

            // Must have completed server-side analysis bound to this handle
            if (sourceEntry.AnalysisResult == null || string.IsNullOrEmpty(sourceEntry.BoundSha256))
            {
                return Results.BadRequest(new { error = "Kaynak dosya için doğrulanmış analiz sonucu bulunamadı. Dönüştürme öncesinde analiz zorunludur." });
            }

            // Preflight 50-item evaluation limit blocker
            if (sourceEntry.AnalysisResult.Preflight.HasTrialBlocker)
            {
                return Results.BadRequest(new { error = $"[ÖN KONTROL ENGELİ] {sourceEntry.AnalysisResult.Preflight.TrialBlockerReason}" });
            }

            // Verify source file has not changed in size since registration
            var currentFi = new FileInfo(sourceEntry.FullPath);
            if (currentFi.Length != sourceEntry.SizeBytes)
            {
                return Results.BadRequest(new { error = "Kaynak OST dosyasında değişiklik tespit edildi. Lütfen analizi yenileyin." });
            }

            string verifiedSha256 = sourceEntry.BoundSha256;
            int verifiedItemCount = sourceEntry.BoundItemCount;

            // Reject if client supplied an inconsistent expected hash
            if (!string.IsNullOrEmpty(req.ExpectedSourceSha256) &&
                !string.Equals(req.ExpectedSourceSha256, verifiedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "İstemci tarafından sağlanan hash değeri sunucunun doğrulanmış analiz sonucuyla uyuşmuyor." });
            }

            // If selectionId is provided, look up and validate immutable registered selection
            RegisteredSelection? selection = null;
            if (!string.IsNullOrWhiteSpace(req.SelectionId))
            {
                selection = handleRegistry.GetSelection(req.SelectionId);
                if (selection == null)
                {
                    return Results.BadRequest(new { error = "Geçersiz veya süresi dolmuş seçim tanıtıcısı (selectionId). Lütfen filtre önizlemesini yenileyin." });
                }

                if (!string.Equals(selection.SourceHandle, req.SourceHandle, StringComparison.Ordinal) ||
                    !string.Equals(selection.SourceSha256, verifiedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { error = "Seçim tanıtıcısı bu kaynak dosya veya dosya sürümü ile uyuşmuyor." });
                }

                if (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker)
                {
                    return Results.BadRequest(new { error = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "[ÖN KONTROL ENGELİ] Kaynak dosyada ön kontrol engeli bulunmaktadır. Dönüştürme başlatılamaz." });
                }

                if (selection.SelectedMessagesCount == 0)
                {
                    return Results.BadRequest(new { error = "Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili dönüştürme başlatılamaz." });
                }
            }

            var clientContext = req.ClientContext ?? new ClientProjectContext();

            try
            {
                var record = jobManager.StartJob(
                    sourceEntry.FullPath,
                    targetPath,
                    req.IdempotencyKey,
                    clientContext,
                    verifiedSha256,
                    verifiedItemCount,
                    selection,
                    req.EnqueueIfBusy);

                return Results.Ok(record);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 7. Get Job Progress
        app.MapGet("/api/jobs/{jobId}", (HttpContext http,JobManager jobManager,IdentityCatalog catalog,string jobId) =>
        {
            var job = jobManager.GetJob(jobId);
            return job != null&&CanReadJob(http,catalog,job) ? Results.Ok(job) : Results.NotFound(new { error = $"İş bulunamadı: {jobId}" });
        });

        // 8. Get Job Report
        app.MapGet("/api/jobs/{jobId}/report", (HttpContext http,JobManager jobManager,IdentityCatalog catalog,DamagedStoreRecoveryService recovery, string jobId) =>
        {
            var record=jobManager.GetJob(jobId);if(record is not null&&!CanReadJob(http,catalog,record))return Results.NotFound(new{error=$"Rapor bulunamadı: {jobId}"});
            var report = jobManager.GetReport(jobId);
            if(report is not null)return Results.Ok(report);
            if(recovery.Get(jobId) is { ReportAvailable:true } recoveryReport)return Results.Ok(recoveryReport);
            if(jobManager.GetJob(jobId) is {JobKind:"archive-selected-export",Status:"completed"} selected)return Results.Ok(new{selected.JobId,selected.JobKind,selected.ClientContext,selected.ItemsRead,selected.ItemsWritten,selected.FailedItems,selected.OutputDirectoryPath,selected.FolderMappingFingerprint,selected.DuplicatePolicy,selected.SkippedDuplicateItemIds,selected.FrozenSelectionFingerprint,selected.CompletedAt});
            return Results.NotFound(new { error = $"Rapor bulunamadı: {jobId}" });
        });

        // 9. Get All Jobs
        app.MapGet("/api/jobs", (HttpContext http,JobManager jobManager,IdentityCatalog catalog) =>
        {
            var jobs = jobManager.GetAllJobs().Where(job=>CanReadJob(http,catalog,job));
            return Results.Ok(jobs);
        });

        app.MapGet("/api/jobs/page", (HttpContext http,JobManager jobManager,IdentityCatalog catalog,int page = 1, int pageSize = 50, string? status = null, string? search = null, string? jobKind = null) =>
        {
            return Results.Ok(jobManager.GetJobsPage(page, pageSize, status, search, jobKind,job=>CanReadJob(http,catalog,job)));
        });

        app.MapPost("/api/jobs/{jobId}/cancel-pending", (HttpContext http,JobManager jobManager,IdentityCatalog catalog,string jobId, PendingJobCancelRequest req) =>
        {
            var job=jobManager.GetJob(jobId);if(job is null||!CanReadJob(http,catalog,job))return Results.NotFound(new{error=$"İş bulunamadı: {jobId}"});
            try { return Results.Ok(jobManager.CancelPendingJob(jobId, req.CompanyId, req.ProjectId)); }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
        app.MapPost("/api/jobs/{jobId}/priority", (HttpContext http,JobManager jobManager,IdentityCatalog catalog,string jobId,WaitingPriorityRequest req) =>
        {
            var job=jobManager.GetJob(jobId);if(job is null||!CanReadJob(http,catalog,job))return Results.NotFound(new{error=$"İş bulunamadı: {jobId}"});
            try{return Results.Ok(jobManager.SetWaitingPriority(jobId,req.Priority,req.CompanyId,req.ProjectId));}
            catch(InvalidOperationException ex){return Results.Conflict(new{error=ex.Message});}
        });

        // 10. Split Source (PST or OST) Native Picker
        app.MapPost("/api/picker/split-source", async (IFilePickerService pickerService) =>
        {
            var result = await pickerService.PickSplitSourceAsync();
            return Results.Ok(result);
        });

        // 11. Output Directory Native Picker
        app.MapPost("/api/picker/output-dir", async (IFilePickerService pickerService) =>
        {
            var result = await pickerService.PickOutputDirAsync();
            return Results.Ok(result);
        });
        app.MapPost("/api/picker/archive-backup", async (IFilePickerService pickerService) =>
        {
            var result = await pickerService.PickArchiveBackupAsync();
            return Results.Ok(result);
        });

        // 12. Split Source Analysis (Authoritative genuine PST or OST validation)
        app.MapPost("/api/split/source/analyze", async (FileHandleRegistry handleRegistry, OstAnalyzer analyzer, AnalyzeRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req?.SourceHandle))
            {
                return Results.BadRequest(new { error = "sourceHandle parametresi zorunludur." });
            }

            string? sourcePath = handleRegistry.GetSourcePath(req.SourceHandle);
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                return Results.BadRequest(new { error = "Geçersiz veya süresi dolmuş kaynak tanıtıcısı. Lütfen dosyayı tekrar seçin." });
            }

            try
            {
                var result = await analyzer.AnalyzeSplitSourceAsync(sourcePath);
                handleRegistry.AttachAnalysis(req.SourceHandle, result);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 13. Create Split Plan
        app.MapPost("/api/split/plan", HandleSplitPlan);

        // 14. Start Split Job
        app.MapPost("/api/split/start", HandleSplitStart);

        // 15. MIME Source Native Picker
        app.MapPost("/api/picker/mime-source", async (IFilePickerService pickerService, MimePickerRequest? req) =>
        {
            var result = await pickerService.PickMimeSourceAsync(req?.Mode ?? "eml-files");
            return Results.Ok(result);
        });

        // 16. MIME Source Analysis
        app.MapPost("/api/mime/source/analyze", HandleMimeAnalyze);
        app.MapPost("/api/mime/analyze", HandleMimeAnalyze);

        // 17. MIME Selection Preview
        app.MapPost("/api/mime/selection/preview", HandleMimePreview);
        app.MapPost("/api/mime/preview", HandleMimePreview);

        // 18. Start MIME Import Job
        app.MapPost("/api/mime/start", HandleMimeStart);

        // Stage 4: Apple Mail EMLX -> verified EML tree normalization.
        app.MapPost("/api/picker/emlx-source", async (IFilePickerService picker, EmlxPickerRequest? req) =>
            Results.Ok(await picker.PickEmlxSourceAsync(req?.Mode ?? "tree")));
        app.MapPost("/api/emlx/preview", (FileHandleRegistry registry, EmlxPreviewRequest req) =>
        {
            var entry = registry.GetEmlxSourceEntry(req.SourceHandle);
            if (entry is null) return Results.BadRequest(new { error = "Geçersiz veya süresi dolmuş EMLX kaynak tanıtıcısı." });
            return Results.Ok(new { sourceHandle = entry.Handle, sourceFingerprint = entry.Manifest.AggregateFingerprint,
                totalItems = entry.Manifest.Entries.Count, totalBytes = entry.Manifest.TotalBytes,
                qualification = entry.Manifest.Qualification,
                warnings = new[] { "Apple plist metadata sidecar olarak korunur; bayraklar ve tarihler aktarılmaz.", "Harici Apple Mail ek depoları desteklenmez." }, canNormalize = true });
        });
        app.MapPost("/api/emlx/start", (FileHandleRegistry registry, JobManager jobs, EmlxStartRequest req) =>
        {
            var entry = registry.GetEmlxSourceEntry(req.SourceHandle);
            var output = registry.GetOutputDirPath(req.OutputDirHandle);
            if (entry is null || string.IsNullOrEmpty(output)) return Results.BadRequest(new { error = "Kaynak veya çıktı klasörü tanıtıcısı geçersiz." });
            if (!string.Equals(req.ExpectedSourceFingerprint, entry.Manifest.AggregateFingerprint, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Kaynak manifestosu değişmiş veya önizlemeyle eşleşmiyor." });
            try { return Results.Ok(jobs.StartEmlxNormalizationJob(entry.Manifest, output, req.IdempotencyKey, req.ClientContext ?? new(), req.EnqueueIfBusy)); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPost("/api/picker/outlook-eml-source", async (IFilePickerService picker) => Results.Ok(await picker.PickOutlookEmlSourceAsync()));
        app.MapPost("/api/outlook-eml/preview", (FileHandleRegistry registry, OutlookEmlPreviewRequest req) =>
        {
            var entry = registry.GetSourceEntry(req.SourceHandle); if (entry is null) return Results.BadRequest(new { error = "Geçersiz kaynak tanıtıcısı." });
            string ext = Path.GetExtension(entry.FullPath).TrimStart('.').ToLowerInvariant(); if (ext is not ("pst" or "ost" or "olm")) return Results.BadRequest(new { error = "Yalnız PST, OST veya OLM desteklenir." });
            using var fs = File.OpenRead(entry.FullPath); string hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            return Results.Ok(new { sourceHandle = req.SourceHandle, sourceFormat = ext, sourceSha256 = hash, sourceSizeBytes = entry.SizeBytes, qualification = ext == "olm" ? "OLM tarih sadakati belirsiz; özgün XML korunur." : "MAPI→MIME nitelendirilmiş dönüşüm.", canNormalize = true });
        });
        app.MapPost("/api/outlook-eml/start", (FileHandleRegistry registry, JobManager jobs, OutlookEmlStartRequest req) =>
        {
            var source = registry.GetSourcePath(req.SourceHandle); var output = registry.GetOutputDirPath(req.OutputDirHandle); if (source is null || output is null) return Results.BadRequest(new { error = "Kaynak veya çıktı tanıtıcısı geçersiz." });
            try { return Results.Ok(jobs.StartOutlookEmlJob(source, output, req.ExpectedSourceSha256, req.IdempotencyKey, req.ClientContext ?? new(), req.EnqueueIfBusy)); } catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        // 19. IMAP Accounts Management (TASK-014)
        // 19a. List Accounts by Company and Project Scope
        app.MapGet("/api/accounts", (ImapAccountStore store, string? companyId, string? projectId) =>
        {
            if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
            }

            try
            {
                var accounts = store.ListAccounts(companyId, projectId);
                return Results.Ok(accounts);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Hesap listesi alınırken bir hata oluştu." });
            }
        });

        // 19b. Get Account by ID
        app.MapGet("/api/accounts/{accountId}", (ImapAccountStore store, string accountId, string? companyId, string? projectId) =>
        {
            if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
            }

            try
            {
                var account = store.GetAccount(accountId, companyId, projectId);
                return account != null ? Results.Ok(account) : Results.NotFound(new { error = $"Hesap bulunamadı: {accountId}" });
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound(new { error = $"Hesap bulunamadı: {accountId}" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Hesap ayrıntıları alınırken bir hata oluştu." });
            }
        });

        // 19c. Create Account
        app.MapPost("/api/accounts", (ImapAccountStore store, CreateImapAccountRequest req) =>
        {
            if (req == null)
            {
                return Results.BadRequest(new { error = "İstek gövdesi boş olamaz." });
            }

            try
            {
                var created = store.CreateAccount(req);
                return Results.Ok(created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Hesap oluşturulurken bir hata oluştu." });
            }
        });

        // 19d. Update Account
        var handleAccountUpdate = (ImapAccountStore store, string accountId, UpdateImapAccountRequest req) =>
        {
            if (req == null)
            {
                return Results.BadRequest(new { error = "İstek gövdesi boş olamaz." });
            }

            if (!req.ExpectedVersion.HasValue)
            {
                return Results.BadRequest(new { error = "expectedVersion alanı zorunludur." });
            }

            try
            {
                var updated = store.UpdateAccount(accountId, req);
                return Results.Ok(updated);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (AccountVersionConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message, currentVersion = ex.CurrentVersion, expectedVersion = ex.ExpectedVersion });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Hesap güncellenirken bir hata oluştu." });
            }
        };

        app.MapPut("/api/accounts/{accountId}", handleAccountUpdate);
        app.MapPost("/api/accounts/{accountId}/update", handleAccountUpdate);

        // 19e. Delete Account (deletes local record/credentials only; never remote messages or folders)
        var handleAccountDelete = (ImapAccountStore store, string accountId, string? companyId, string? projectId, long? expectedVersion, DeleteAccountRequest? req) =>
        {
            string effectiveCompany = !string.IsNullOrWhiteSpace(companyId) ? companyId : req?.CompanyId ?? string.Empty;
            string effectiveProject = !string.IsNullOrWhiteSpace(projectId) ? projectId : req?.ProjectId ?? string.Empty;
            long? effectiveExpectedVersion = expectedVersion ?? req?.ExpectedVersion;

            if (string.IsNullOrWhiteSpace(effectiveCompany) || string.IsNullOrWhiteSpace(effectiveProject))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
            }

            try
            {
                bool deleted = store.DeleteAccount(accountId, effectiveCompany, effectiveProject, effectiveExpectedVersion);
                if (!deleted)
                {
                    return Results.NotFound(new { error = $"Hesap bulunamadı: {accountId}" });
                }
                return Results.Ok(new { success = true, accountId });
            }
            catch (AccountVersionConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message, currentVersion = ex.CurrentVersion, expectedVersion = ex.ExpectedVersion });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Hesap silinirken bir hata oluştu." });
            }
        };

        app.MapDelete("/api/accounts/{accountId}", (ImapAccountStore store, string accountId, string? companyId, string? projectId, long? expectedVersion) =>
            handleAccountDelete(store, accountId, companyId, projectId, expectedVersion, null));
        app.MapPost("/api/accounts/{accountId}/delete", handleAccountDelete);

        // 19f. Test IMAP Connection (Safe connection test, returns stable redacted errors)
        app.MapPost("/api/accounts/test", async (ImapAccountStore store, ImapClientService clientService, TestImapConnectionRequest req, CancellationToken ct) =>
        {
            if (req == null)
            {
                return Results.BadRequest(new { error = "İstek gövdesi boş olamaz." });
            }

            string host, username, password, tlsMode;
            int port;

            try
            {
                if (!string.IsNullOrWhiteSpace(req.AccountId))
                {
                    if (string.IsNullOrWhiteSpace(req.CompanyId) || string.IsNullOrWhiteSpace(req.ProjectId))
                    {
                        return Results.BadRequest(new { error = "Kayıtlı hesap testi için companyId ve projectId zorunludur." });
                    }

                    var savedResult = await clientService.TestAccountConnectionAsync(req.AccountId, req.CompanyId, req.ProjectId, ct);
                    return savedResult.Success ? Results.Ok(savedResult) : Results.BadRequest(new { success = false, error = savedResult.Error });
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(req.CompanyId) || string.IsNullOrWhiteSpace(req.ProjectId))
                    {
                        return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
                    }

                    ImapConnectionPolicy.ValidateScope(req.CompanyId, req.ProjectId);

                    host = req.Host ?? string.Empty;
                    port = req.Port ?? 0;
                    tlsMode = req.TlsMode ?? string.Empty;
                    username = req.Username ?? string.Empty;
                    password = req.Password ?? string.Empty;
                }

                var result = await clientService.TestConnectionAsync(host, port, tlsMode, new ImapPasswordCredential(username, password, req.AllowUnencryptedConnection == true), ct);
                if (!result.Success)
                {
                    return Results.BadRequest(new { success = false, error = result.Error });
                }

                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ReauthorizationRequiredException ex)
            {
                return Results.Conflict(new { code = ReauthorizationRequiredException.StableCode, error = ex.Message, accountId = ex.AccountId });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { success = false, error = "IMAP bağlantısı kurulamadı. Sunucu ayarlarınızı kontrol edin." });
            }
        });

        app.MapPost("/api/accounts/{accountId}/test", async (ImapAccountStore store, ImapClientService clientService, string accountId, TestImapConnectionRequest? req, string? companyId, string? projectId, CancellationToken ct) =>
        {
            string effectiveCompany = !string.IsNullOrWhiteSpace(companyId) ? companyId : req?.CompanyId ?? string.Empty;
            string effectiveProject = !string.IsNullOrWhiteSpace(projectId) ? projectId : req?.ProjectId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(effectiveCompany) || string.IsNullOrWhiteSpace(effectiveProject))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
            }

            try
            {
                var result = await clientService.TestAccountConnectionAsync(accountId, effectiveCompany, effectiveProject, ct);
                if (!result.Success)
                {
                    return Results.BadRequest(new { success = false, error = result.Error });
                }
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ReauthorizationRequiredException ex)
            {
                return Results.Conflict(new { code = ReauthorizationRequiredException.StableCode, error = ex.Message, accountId = ex.AccountId });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { success = false, error = "IMAP bağlantısı kurulamadı. Sunucu ayarlarınızı kontrol edin." });
            }
        });

        // 19g. List Real Folders (full exact path, delimiter, selectable status, message count)
        var handleAccountFolders = async (ImapAccountStore store, ImapClientService clientService, string accountId, string? companyId, string? projectId, DeleteAccountRequest? req, CancellationToken ct) =>
        {
            string effectiveCompany = !string.IsNullOrWhiteSpace(companyId) ? companyId : req?.CompanyId ?? string.Empty;
            string effectiveProject = !string.IsNullOrWhiteSpace(projectId) ? projectId : req?.ProjectId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(effectiveCompany) || string.IsNullOrWhiteSpace(effectiveProject))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
            }

            try
            {
                var folders = await clientService.ListAccountFoldersAsync(accountId, effectiveCompany, effectiveProject, ct);
                return Results.Ok(new ImapFolderListResponse
                {
                    AccountId = accountId,
                    Folders = folders
                });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ReauthorizationRequiredException ex)
            {
                return Results.Conflict(new { code = ReauthorizationRequiredException.StableCode, error = ex.Message, accountId = ex.AccountId });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "IMAP klasör listesi alınamadı. Sunucu ayarlarınızı kontrol edin." });
            }
        };

        app.MapGet("/api/accounts/{accountId}/folders", (ImapAccountStore store, ImapClientService clientService, string accountId, string? companyId, string? projectId, CancellationToken ct) =>
            handleAccountFolders(store, clientService, accountId, companyId, projectId, null, ct));
        app.MapPost("/api/accounts/{accountId}/folders", handleAccountFolders);

        // 20. IMAP Transfer Endpoints (TASK-014)
        app.MapImapTransferEndpoints();

        // 21. Microsoft OAuth Endpoints (TASK-015)
        app.MapMicrosoftOAuthEndpoints();
        app.MapGoogleOAuthEndpoints();

        // 22. File <-> Account Bridge Transfer Endpoints (TASK-017)
        app.MapBridgeTransferEndpoints();

        // 23. Local Archive & Search Endpoints (TASK-018)
        app.MapArchiveEndpoints();
        app.MapRecoveryEndpoints();
        app.MapStage7Endpoints();
        app.MapPopEndpoints();
    }

    public static IResult HandleSelectionPreview(FileHandleRegistry handleRegistry, SelectionPreviewRequest req)
    {
        if (req.AdvancedFilter is not null || req.FolderMappings?.Count > 0 || req.DuplicatePolicy != BitigMail.Engine.Planning.DuplicatePolicy.PreservePhysical)
            return Results.BadRequest(new { error = "Gelişmiş filtre bu PST/OST seçim bağdaştırıcısında henüz desteklenmiyor; filtre sessizce yok sayılmadı." });
        if (string.IsNullOrWhiteSpace(req?.SourceHandle))
        {
            return Results.BadRequest(new { error = "sourceHandle parametresi zorunludur." });
        }

        var sourceEntry = handleRegistry.GetSourceEntry(req.SourceHandle);
        if (sourceEntry == null || string.IsNullOrEmpty(sourceEntry.FullPath) || !File.Exists(sourceEntry.FullPath))
        {
            return Results.BadRequest(new { error = "Kaynak dosya bulunamadı veya tanıtıcı geçersiz." });
        }

        // Must have completed server-side analysis bound to this handle
        if (sourceEntry.AnalysisResult == null || string.IsNullOrEmpty(sourceEntry.BoundSha256))
        {
            return Results.BadRequest(new { error = "Kaynak dosya için doğrulanmış analiz sonucu bulunamadı. Önizleme öncesinde analiz zorunludur." });
        }

        try
        {
            // Hold read-only lock on the real source stream to eliminate hash-before-read race conditions
            using var fs = new FileStream(sourceEntry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Verify file size while holding stream lock
            if (fs.Length != sourceEntry.SizeBytes)
            {
                return Results.BadRequest(new { error = "Kaynak OST dosyasında değişiklik tespit edildi. Lütfen analizi yenileyin." });
            }

            // Compute actual SHA-256 while holding stream lock
            fs.Position = 0;
            using var sha = SHA256.Create();
            string actualSha256 = System.Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();

            // Fail-closed rejection if actual hash does not match registered BoundSha256
            if (!string.Equals(actualSha256, sourceEntry.BoundSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = $"[BÜTÜNLÜK ENGELİ] Kaynak OST dosyasında analizden sonra değişiklik tespit edildi. Kayıtlı hash: {sourceEntry.BoundSha256}, Güncel hash: {actualSha256}. Lütfen analizi yenileyin." });
            }

            // Reset Position to 0 for PersonalStorage traversal
            fs.Position = 0;

            // Shield underlying stream from premature close upon Aspose storage disposal
            using var ost = Aspose.Email.Storage.Pst.PersonalStorage.FromStream(new NonClosingStream(fs));

            var (preview, registered) = OstSelectionEngine.EvaluateSelection(
                ost,
                req.SourceHandle,
                actualSha256,
                req.FolderIds,
                req.StartDate,
                req.EndDate,
                sourceEntry.AnalysisResult.Preflight,
                CancellationToken.None);

            handleRegistry.RegisterSelection(registered);
            return Results.Ok(preview);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Önizleme oluşturulamadı: {ex.Message}" });
        }
    }

    public static IResult HandleSplitPlan(FileHandleRegistry handleRegistry, SplitPlanRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.SourceHandle))
        {
            return Results.BadRequest(new { error = "sourceHandle parametresi zorunludur." });
        }

        var sourceEntry = handleRegistry.GetSourceEntry(req.SourceHandle);
        if (sourceEntry == null || string.IsNullOrEmpty(sourceEntry.FullPath) || !File.Exists(sourceEntry.FullPath))
        {
            return Results.BadRequest(new { error = "Kaynak dosya bulunamadı veya tanıtıcı geçersiz." });
        }

        if (sourceEntry.AnalysisResult == null || string.IsNullOrEmpty(sourceEntry.BoundSha256))
        {
            return Results.BadRequest(new { error = "Kaynak dosya için doğrulanmış analiz sonucu bulunamadı. Plan öncesinde analiz zorunludur." });
        }

        var optValidation = SplitOptions.Validate(req.SplitMode, req.SizeCapBytes);
        if (!optValidation.IsValid)
        {
            return Results.BadRequest(new { error = optValidation.Error });
        }

        try
        {
            using var fs = new FileStream(sourceEntry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length != sourceEntry.SizeBytes)
            {
                return Results.BadRequest(new { error = "Kaynak veri dosyasında değişiklik tespit edildi. Lütfen analizi yenileyin." });
            }

            fs.Position = 0;
            using var sha = SHA256.Create();
            string actualSha256 = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            if (!string.Equals(actualSha256, sourceEntry.BoundSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = $"[BÜTÜNLÜK ENGELİ] Kaynak dosyada analizden sonra değişiklik tespit edildi. Lütfen analizi yenileyin." });
            }

            fs.Position = 0;
            using var storage = Aspose.Email.Storage.Pst.PersonalStorage.FromStream(new NonClosingStream(fs));

            RegisteredSelection? selection = null;
            if (!string.IsNullOrWhiteSpace(req.SelectionId))
            {
                selection = handleRegistry.GetSelection(req.SelectionId);
                if (selection == null)
                {
                    return Results.BadRequest(new { error = "Geçersiz veya süresi dolmuş seçim tanıtıcısı (selectionId)." });
                }
                if (!string.Equals(selection.SourceHandle, req.SourceHandle, StringComparison.Ordinal) ||
                    !string.Equals(selection.SourceSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new { error = "Seçim tanıtıcısı bu kaynak dosya veya dosya sürümü ile uyuşmuyor." });
                }
            }
            else
            {
                var (preview, registered) = OstSelectionEngine.EvaluateSelection(
                    storage,
                    req.SourceHandle,
                    actualSha256,
                    req.FolderIds,
                    req.StartDate,
                    req.EndDate,
                    sourceEntry.AnalysisResult.Preflight,
                    CancellationToken.None);
                selection = registered;
                handleRegistry.RegisterSelection(registered);
            }

            if (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker)
            {
                string blockerReason = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "[ÖN KONTROL ENGELİ] Kaynak dosyada ön kontrol engeli bulunmaktadır. Bölme başlatılamaz.";
                return Results.Ok(new SplitPlanResult
                {
                    PlanId = string.Empty,
                    SourceHandle = req.SourceHandle,
                    SourceSha256 = actualSha256,
                    SelectionId = selection.SelectionId,
                    SplitMode = req.SplitMode.ToLowerInvariant(),
                    SizeCapBytes = req.SizeCapBytes,
                    YearGroups = new List<SplitYearGroupPreview>(),
                    CanSplit = false,
                    BlockerReason = blockerReason
                });
            }

            if (selection.SelectedMessagesCount == 0)
            {
                return Results.Ok(new SplitPlanResult
                {
                    PlanId = string.Empty,
                    SourceHandle = req.SourceHandle,
                    SourceSha256 = actualSha256,
                    SelectionId = selection.SelectionId,
                    SplitMode = req.SplitMode.ToLowerInvariant(),
                    SizeCapBytes = req.SizeCapBytes,
                    YearGroups = new List<SplitYearGroupPreview>(),
                    CanSplit = false,
                    BlockerReason = "Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili bölme başlatılamaz."
                });
            }

            List<SplitYearGroupPreview> yearGroups = new();
            if (req.SplitMode.Equals(SplitOptions.ModeYear, StringComparison.OrdinalIgnoreCase))
            {
                yearGroups = PstSplitter.CalculateYearGroups(storage, actualSha256, selection, CancellationToken.None);
            }

            string planId = "plan_" + Guid.NewGuid().ToString("N");
            int estimatedPartCount = req.SplitMode.Equals(SplitOptions.ModeYear, StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1, yearGroups.Count)
                : DiskCapacityPlanning.EstimateSizeSplitParts(sourceEntry.SizeBytes, req.SizeCapBytes!.Value);
            long estimatedRequiredBytes = DiskCapacityPlanning.EstimateSplit(
                sourceEntry.SizeBytes, selection.SelectedMessagesCount, estimatedPartCount);
            var registeredPlan = new RegisteredSplitPlan
            {
                PlanId = planId,
                SourceHandle = req.SourceHandle,
                SourceSha256 = actualSha256,
                SelectionId = selection.SelectionId,
                Selection = selection,
                SplitMode = req.SplitMode.ToLowerInvariant(),
                SizeCapBytes = req.SplitMode.Equals(SplitOptions.ModeSize, StringComparison.OrdinalIgnoreCase) ? req.SizeCapBytes : null,
                YearGroups = yearGroups,
                CanSplit = true,
                EstimatedRequiredBytes = estimatedRequiredBytes,
                EstimatedPartCount = estimatedPartCount,
                CreatedAt = DateTimeOffset.UtcNow
            };
            handleRegistry.RegisterSplitPlan(registeredPlan);

            return Results.Ok(new SplitPlanResult
            {
                PlanId = planId,
                SourceHandle = req.SourceHandle,
                SourceSha256 = actualSha256,
                SelectionId = selection.SelectionId,
                SplitMode = registeredPlan.SplitMode,
                SizeCapBytes = registeredPlan.SizeCapBytes,
                YearGroups = yearGroups,
                CanSplit = true,
                EstimatedRequiredBytes = estimatedRequiredBytes,
                EstimatedPartCount = estimatedPartCount,
                EstimateBasis = "Tüm değişmez kaynak boyutu; tahmini PST genişlemesi, geçici/son çıktı birlikteliği ve bölüm ek yükü"
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Plan oluşturulamadı: {ex.Message}" });
        }
    }

    public static IResult HandleSplitStart(FileHandleRegistry handleRegistry, JobManager jobManager, SplitStartRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.PlanId) || string.IsNullOrWhiteSpace(req?.OutputDirHandle))
        {
            return Results.BadRequest(new { error = "planId ve outputDirHandle parametreleri zorunludur." });
        }

        string? outputDirPath = handleRegistry.GetOutputDirPath(req.OutputDirHandle);
        if (string.IsNullOrEmpty(outputDirPath) || !Directory.Exists(outputDirPath))
        {
            return Results.BadRequest(new { error = "Hedef klasör tanıtıcısı geçersiz veya klasör bulunamadı." });
        }

        var plan = handleRegistry.GetSplitPlan(req.PlanId);
        if (plan == null)
        {
            return Results.BadRequest(new { error = "Geçersiz veya süresi dolmuş bölme planı (planId). Lütfen planı tekrar oluşturun." });
        }

        if (!plan.CanSplit)
        {
            return Results.BadRequest(new { error = plan.BlockerReason ?? "Bölme planı geçersizdir." });
        }

        var sourceEntry = handleRegistry.GetSourceEntry(plan.SourceHandle);
        if (sourceEntry == null || string.IsNullOrEmpty(sourceEntry.FullPath) || !File.Exists(sourceEntry.FullPath))
        {
            return Results.BadRequest(new { error = "Kaynak dosya bulunamadı veya tanıtıcı geçersiz." });
        }

        var fi = new FileInfo(sourceEntry.FullPath);
        if (fi.Length != sourceEntry.SizeBytes)
        {
            return Results.BadRequest(new { error = "Kaynak veri dosyasında değişiklik tespit edildi. Lütfen analizi yenileyin." });
        }

        RegisteredSelection? selection = !string.IsNullOrEmpty(plan.SelectionId)
            ? handleRegistry.GetSelection(plan.SelectionId)
            : null;

        var clientContext = req.ClientContext ?? new ClientProjectContext();

        try
        {
            var record = jobManager.StartSplitJob(
                sourceEntry.FullPath,
                outputDirPath,
                req.IdempotencyKey,
                clientContext,
                plan,
                selection,
                runInBackground: true,
                enqueueIfBusy: req.EnqueueIfBusy);

            return Results.Ok(record);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public static async Task<IResult> HandleMimeAnalyze(FileHandleRegistry handleRegistry, AnalyzeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.SourceHandle))
        {
            return Results.BadRequest(new { error = "sourceHandle parametresi zorunludur." });
        }

        var entry = handleRegistry.GetMimeSourceEntry(req.SourceHandle);
        if (entry == null || entry.Manifest == null)
        {
            return Results.BadRequest(new { error = "Geçersiz veya süresi dolmuş MIME kaynak tanıtıcısı. Lütfen dosyaları tekrar seçin." });
        }

        try
        {
            var inspector = new MimeSourceInspector();
            var result = await inspector.AnalyzeAsync(entry.Manifest, req.SourceHandle);
            handleRegistry.AttachMimeAnalysis(req.SourceHandle, result);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public static IResult HandleMimePreview(FileHandleRegistry handleRegistry, SelectionPreviewRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.SourceHandle))
        {
            return Results.BadRequest(new { error = "sourceHandle parametresi zorunludur." });
        }

        var entry = handleRegistry.GetMimeSourceEntry(req.SourceHandle);
        if (entry == null || entry.Manifest == null)
        {
            return Results.BadRequest(new { error = "MIME kaynak tanıtıcısı geçersiz veya bulunamadı." });
        }

        if (entry.AnalysisResult == null || string.IsNullOrEmpty(entry.BoundFingerprint))
        {
            return Results.BadRequest(new { error = "MIME kaynak için doğrulanmış analiz sonucu bulunamadı. Önizleme öncesinde analiz zorunludur." });
        }

        try
        {
            var inspector = new MimeSourceInspector();
            inspector.RevalidateManifest(entry.Manifest);

            var (preview, registered) = MimeSelectionEngine.EvaluateSelection(
                entry.Manifest,
                req.SourceHandle,
                req.FolderIds,
                req.StartDate,
                req.EndDate,
                entry.AnalysisResult.Preflight,
                CancellationToken.None,
                req.AdvancedFilter, req.FolderMappings, req.DuplicatePolicy);

            handleRegistry.RegisterSelection(registered);
            return Results.Ok(preview);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Önizleme oluşturulamadı: {ex.Message}" });
        }
    }

    public static IResult HandleMimeStart(FileHandleRegistry handleRegistry, JobManager jobManager, StartJobRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.SourceHandle) || string.IsNullOrWhiteSpace(req?.TargetHandle))
        {
            return Results.BadRequest(new { error = "sourceHandle ve targetHandle parametreleri zorunludur." });
        }

        var mimeEntry = handleRegistry.GetMimeSourceEntry(req.SourceHandle);
        string? targetPath = handleRegistry.GetTargetPath(req.TargetHandle);

        if (mimeEntry == null || mimeEntry.Manifest == null)
        {
            return Results.BadRequest(new { error = "MIME kaynak bulunamadı veya tanıtıcı geçersiz." });
        }

        if (string.IsNullOrEmpty(targetPath))
        {
            return Results.BadRequest(new { error = "Hedef dosya tanıtıcısı geçersiz." });
        }

        if (mimeEntry.AnalysisResult == null || string.IsNullOrEmpty(mimeEntry.BoundFingerprint))
        {
            return Results.BadRequest(new { error = "Kaynak dosya için doğrulanmış analiz sonucu bulunamadı. İçe aktarma öncesinde analiz zorunludur." });
        }

        if (mimeEntry.AnalysisResult.Preflight.HasTrialBlocker)
        {
            return Results.BadRequest(new { error = $"[ÖN KONTROL ENGELİ] {mimeEntry.AnalysisResult.Preflight.TrialBlockerReason}" });
        }

        string verifiedFingerprint = mimeEntry.BoundFingerprint;

        if (!string.IsNullOrEmpty(req.ExpectedSourceSha256) &&
            !string.Equals(req.ExpectedSourceSha256, verifiedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "İstemci tarafından sağlanan parmak izi sunucunun doğrulanmış analiz sonucuyla uyuşmuyor." });
        }

        RegisteredSelection? selection = null;
        if (!string.IsNullOrWhiteSpace(req.SelectionId))
        {
            selection = handleRegistry.GetSelection(req.SelectionId);
            if (selection == null)
            {
                return Results.BadRequest(new { error = "Geçersiz veya süresi dolmuş seçim tanıtıcısı (selectionId). Lütfen filtre önizlemesini yenileyin." });
            }

            if (!string.Equals(selection.SourceHandle, req.SourceHandle, StringComparison.Ordinal) ||
                !string.Equals(selection.SourceSha256, verifiedFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Seçim tanıtıcısı bu kaynak veya kaynak sürümü ile uyuşmuyor." });
            }

            if (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker)
            {
                return Results.BadRequest(new { error = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "[ÖN KONTROL ENGELİ] Kaynakta ön kontrol engeli bulunmaktadır. İçe aktarma başlatılamaz." });
            }

            if (selection.SelectedMessagesCount == 0)
            {
                return Results.BadRequest(new { error = "Seçim kriterlerinize uyan hiçbir ileti bulunamadı. Sıfır iletili içe aktarma başlatılamaz." });
            }

            var qualification = new NormalizedSourceQualificationReader().Read(mimeEntry.Manifest);
            if (!string.Equals(qualification?.Fingerprint, selection.QualificationFingerprint, StringComparison.Ordinal) ||
                (qualification?.DateFilterBlocked ?? false) != selection.DateFilterBlocked || (qualification?.IsPartial ?? false) != selection.QualificationIsPartial)
                return Results.BadRequest(new { error = "Kaynak qualification bilgisi önizlemeden sonra değişti." });
        }

        var clientContext = req.ClientContext ?? new ClientProjectContext();

        try
        {
            var record = jobManager.StartMimeJob(
                mimeEntry.Manifest,
                targetPath,
                req.IdempotencyKey,
                clientContext,
                selection,
                runInBackground: true,
                enqueueIfBusy: req.EnqueueIfBusy);

            return Results.Ok(record);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

// Request DTOs
public record TargetPickerRequest(string? SourceHandle);
public record PendingJobCancelRequest(string CompanyId, string ProjectId);
public record WaitingPriorityRequest(string CompanyId,string ProjectId,int Priority);
public record MimePickerRequest(string? Mode);
public record EmlxPickerRequest(string? Mode);
public record EmlxPreviewRequest(string SourceHandle);
public record EmlxStartRequest(string SourceHandle, string OutputDirHandle, string ExpectedSourceFingerprint,
    string? IdempotencyKey = null, ClientProjectContext? ClientContext = null, bool EnqueueIfBusy = false);
public record OutlookEmlPreviewRequest(string SourceHandle);
public record OutlookEmlStartRequest(string SourceHandle, string OutputDirHandle, string ExpectedSourceSha256,
    string? IdempotencyKey = null, ClientProjectContext? ClientContext = null, bool EnqueueIfBusy = false);
public record AnalyzeRequest(string SourceHandle = "");
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public record SelectionPreviewRequest(
    string SourceHandle,
    List<string>? FolderIds = null,
    string? StartDate = null,
    string? EndDate = null,
    MailFilterDefinition? AdvancedFilter = null,
    IReadOnlyList<BitigMail.Engine.Planning.FolderMappingRule>? FolderMappings = null,
    BitigMail.Engine.Planning.DuplicatePolicy DuplicatePolicy = BitigMail.Engine.Planning.DuplicatePolicy.PreservePhysical
);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public record StartJobRequest(
    string SourceHandle,
    string TargetHandle,
    string? SelectionId = null,
    string? IdempotencyKey = null,
    string? ExpectedSourceSha256 = null,
    int EstimatedTotalItems = 0,
    ClientProjectContext? ClientContext = null,
    bool EnqueueIfBusy = false
);
public record SplitPlanRequest(
    string SourceHandle,
    string? SelectionId,
    string SplitMode,
    long? SizeCapBytes,
    List<string>? FolderIds,
    string? StartDate,
    string? EndDate
);
public record SplitStartRequest(
    string PlanId,
    string OutputDirHandle,
    string? IdempotencyKey,
    ClientProjectContext? ClientContext,
    bool EnqueueIfBusy = false
);


