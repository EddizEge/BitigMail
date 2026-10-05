using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost.Jobs;

public partial class JobManager
{
    private readonly string _runtimeDir;
    private readonly string _jobsDir;
    private readonly string _reportsDir;
    private readonly ConcurrentDictionary<string, LocalJobRecord> _jobs = new();
    private readonly ConcurrentDictionary<string, InternalJobState> _internalJobStates = new();
    private readonly ConcurrentDictionary<string, ConversionReport> _reports = new();
    private readonly ConcurrentDictionary<string, string> _idempotencyIndex = new(); // IdempotencyKey -> JobId
    private readonly object _jobLock = new();
    private readonly object _persistLock = new();
    private string? _activeRunningJobId;
    private readonly List<PendingJobExecution> _pendingJobs = new();
    private long _pendingSequence;
    private const int PendingJobLimit = 32;
    private readonly IDiskCapacityProbe _capacityProbe;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IdentityCatalog? _identityCatalog;

    internal Action<LocalJobRecord>? OnBeforeSaveJobRecord { get; set; }
    internal Action<ConversionReport>? OnBeforeSaveReport { get; set; }

    private sealed record PendingJobExecution(string JobId, LocalJobRecord Record, Func<Task> Execute,long Sequence);

    private bool IsActiveOrPendingLocked(string jobId) =>
        _activeRunningJobId == jobId || _pendingJobs.Any(item => item.JobId == jobId);

    private void PrepareSchedulingLocked(LocalJobRecord record, bool enqueueIfBusy)
    {
        if (_desktopShutdownRequested) throw new InvalidOperationException("Uygulama kapanırken yeni iş başlatılamaz.");
        if (_httpContextAccessor?.HttpContext?.Items[typeof(AuthenticatedSessionPrincipal)] is AuthenticatedSessionPrincipal actor)
        { record.ActorUserId=actor.UserId;record.ActorSecurityVersion=actor.SecurityVersion; }
        if (_identityCatalog is not null) record.RequiredScopes = FreezeScopes(record.RequiredScopes.Append(new(record.ClientContext.CompanyId, record.ClientContext.ProjectId)));
        if (!CanDispatch(record)) throw new UnauthorizedAccessException("İş için güncel kullanıcı ve kapsam yetkisi gerekli.");
        if (_dispatchBlocked) throw new InvalidOperationException("Sonlandırıldığı doğrulanamayan yerel işçi nedeniyle global iş kuyruğu güvenlik için kilitlidir.");
        if (_activeRunningJobId == null)
        {
            record.WaitingAtShutdown = false;
            record.NeverStartedQueued = false;
            return;
        }

        if (!enqueueIfBusy)
            throw new InvalidOperationException($"Halen devam eden etkin bir yerel işlem bulunmaktadır (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
        if (_pendingJobs.Count >= PendingJobLimit)
            throw new InvalidOperationException($"Bekleyen iş kuyruğu dolu ({PendingJobLimit}).");

        record.Status = "queued";
        record.Stage = "Sırada";
        record.WaitingAtShutdown = true;
        record.NeverStartedQueued = record.StartedAt == null;
    }

    private void ScheduleLocked(LocalJobRecord record, bool enqueueIfBusy, Func<Task> execute)
    {
        if (_activeRunningJobId != null)
        {
            _pendingJobs.Add(new PendingJobExecution(record.JobId, record, execute, _pendingSequence++));
            return;
        }

        _activeRunningJobId = record.JobId;
        record.WaitingAtShutdown = false;
        LaunchExecution(new PendingJobExecution(record.JobId, record, execute, _pendingSequence++));
    }

    private void LaunchExecution(PendingJobExecution pending)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Covers the initially idle slot too; the caller's request can end before Task.Run starts.
                if (!CanDispatch(pending.Record)) { lock (_jobLock) RejectDispatchLocked(pending.Record); return; }
                await pending.Execute();
            }
            finally { CompleteAndDispatch(pending.JobId); }
        });
    }

    private void CompleteAndDispatch(string jobId)
    {
        PendingJobExecution? next = null;
        lock (_jobLock)
        {
            if (_dispatchBlocked) return;
            if (_activeRunningJobId == jobId) _activeRunningJobId = null;
            if (_desktopShutdownRequested) return;
            while (_pendingJobs.Count > 0)
            {
                next = _pendingJobs.OrderByDescending(x=>x.Record.WaitingPriority).ThenBy(x=>x.Sequence).First();
                _pendingJobs.Remove(next);
                if (!CanDispatch(next.Record))
                {
                    RejectDispatchLocked(next.Record);next=null;if(_dispatchBlocked)break;continue;
                }
                next.Record.WaitingAtShutdown = false;
                bool neverStarted = next.Record.NeverStartedQueued;
                next.Record.NeverStartedQueued = false;
                if (next.Record.StartedAt == null && next.Record.JobKind is "convert" or "split" or "mime-import" or "emlx-normalize" or "outlook-eml-normalize" or "pop-snapshot")
                {
                    next.Record.StartedAt = DateTimeOffset.UtcNow;
                    next.Record.Status = "converting";
                    next.Record.Stage = next.Record.JobKind switch
                    {
                        "convert" => "Dönüştürülüyor",
                        "split" => "Bölümleniyor",
                        "emlx-normalize" => "EMLX normalleştiriliyor",
                        "outlook-eml-normalize" => "İletiler çıkarılıyor",
                        "pop-snapshot" => "POP iletileri indiriliyor",
                        _ => "İçe aktarılıyor"
                    };
                }
                try
                {
                    SaveJobRecordLocked(CloneJobRecord(next.Record));
                    _jobs[next.JobId] = CloneJobRecord(next.Record);
                    _activeRunningJobId = next.JobId;
                }
                catch
                {
                    next.Record.Status = "failed";
                    next.Record.Stage = "Kuyruk aktivasyonu başarısız";
                    next.Record.ErrorMessage = "Bekleyen iş kalıcı durum yazılamadığı için başlatılmadı.";
                    next.Record.CompletedAt = DateTimeOffset.UtcNow;
                    next.Record.WaitingAtShutdown = true;
                    next.Record.NeverStartedQueued = neverStarted;
                    _jobs[next.JobId] = CloneJobRecord(next.Record);
                    try { SaveJobRecordLocked(CloneJobRecord(next.Record)); } catch { }
                    next = null;
                }
                if (next != null) break;
            }
        }
        if (next != null) LaunchExecution(next);
    }

    public string? ActiveRunningJobId
    {
        get
        {
            lock (_jobLock)
            {
                return _activeRunningJobId;
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JobManager(string? runtimeDir = null, IDiskCapacityProbe? capacityProbe = null,IHttpContextAccessor? httpContextAccessor=null,IdentityCatalog? identityCatalog=null)
    {
        _capacityProbe = capacityProbe ?? new WindowsDiskCapacityProbe();
        _httpContextAccessor=httpContextAccessor;_identityCatalog=identityCatalog;
        _runtimeDir = !string.IsNullOrEmpty(runtimeDir) ? runtimeDir : ResolveDefaultRuntimeDirectory();
        _jobsDir = Path.Combine(_runtimeDir, "jobs");
        _reportsDir = Path.Combine(_runtimeDir, "reports");

        Directory.CreateDirectory(_jobsDir);
        Directory.CreateDirectory(_reportsDir);
        _dispatchBlocked = File.Exists(Path.Combine(_runtimeDir, "recovery-worker-unresolved.flag")) || File.Exists(Path.Combine(_runtimeDir, "security-dispatch-unresolved.flag"));

        RecoverInterruptedJobsOnStartup();
    }

    private bool CanDispatch(LocalJobRecord record)
    {
        if(_identityCatalog is null)return true;
        if(string.IsNullOrWhiteSpace(record.ActorUserId)||record.ActorSecurityVersion is null)return false;
        return CanAccessJob(new AuthenticatedSessionPrincipal(record.ActorUserId,"dispatch",record.ActorSecurityVersion.Value),record);
    }

    public string RuntimeDirectory => _runtimeDir;

    private static string ResolveDefaultRuntimeDirectory()
    {
        string current = Directory.GetCurrentDirectory();
        for (int i = 0; i < 5; i++)
        {
            if (Directory.Exists(Path.Combine(current, "engine")) || Directory.Exists(Path.Combine(current, "prototype")))
            {
                return Path.Combine(current, "runtime", "local-engine");
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == null || parent == current) break;
            current = parent;
        }

        current = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            if (Directory.Exists(Path.Combine(current, "engine")) || Directory.Exists(Path.Combine(current, "prototype")))
            {
                return Path.Combine(current, "runtime", "local-engine");
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == null || parent == current) break;
            current = parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "runtime", "local-engine");
    }

    private void RecoverInterruptedJobsOnStartup()
    {
        try
        {
            var jobFiles = Directory.GetFiles(_jobsDir, "*.json");
            foreach (var file in jobFiles)
            {
                try
                {
                    string json = ReadFileWithRetry(file);
                    var record = JsonSerializer.Deserialize<LocalJobRecord>(json, JsonOptions);
                    if (record != null)
                    {
                        if (record.RecoveryWorkerUnresolved || record.Stage == "İşçi durumu çözülemedi" ||
                            record.JobKind == "damaged-recovery" && record.Status is "converting" or "verifying")
                        {
                            _dispatchBlocked = true;
                            record.RecoveryWorkerUnresolved = true;
                            record.RecoveryOutcome = "unresolved_worker";
                            record.Status = "failed";
                            record.Stage = "İşçi durumu çözülemedi";
                            record.ErrorMessage = "Önceki kurtarma işçisinin sonlandığı doğrulanmadan yeni işlem başlatılamaz.";
                            JobSnapshotPersistence.Write(Path.Combine(_runtimeDir, "recovery-worker-unresolved.flag"), record.JobId);
                            SaveJobRecordLocked(record);
                        }
                        if (string.IsNullOrEmpty(record.SelectionContentHash))
                        {
                            record.SelectionContentHash = OstSelectionEngine.ComputeSelectionContentHash(record.IsFiltered, record.SelectionFilter);
                        }

                        if (record.Status == "converting" || record.Status == "queued" || record.Status == "verifying")
                        {
                            record.Status = "interrupted";
                            record.Stage = "Kesintiye Uğradı";
                            record.ErrorMessage = record.NeverStartedQueued
                                ? "İş uygulama kapanırken sırada bekliyordu ve hiç başlatılmadı. Yeniden planlama / yeniden başlatma gereklidir."
                                : "Uygulama veya oturum kapandığı için işlem kesintiye uğradı. Veri bütünlüğünü korumak için işlem tamamlanmadı olarak işaretlendi.";
                            record.CompletedAt = DateTimeOffset.UtcNow;
                            record.WaitingAtShutdown = false;
                            SaveJobRecordLocked(record);
                        }

                        _jobs[record.JobId] = record;
                        if (!string.IsNullOrEmpty(record.IdempotencyKey))
                        {
                            _idempotencyIndex[record.IdempotencyKey] = record.JobId;
                        }
                    }
                }
                catch
                {
                    // Skip corrupt files on startup
                }
            }

            var reportFiles = Directory.GetFiles(_reportsDir, "*.json");
            foreach (var file in reportFiles)
            {
                try
                {
                    string json = ReadFileWithRetry(file);
                    var report = JsonSerializer.Deserialize<ConversionReport>(json, JsonOptions);
                    if (report != null && !string.IsNullOrEmpty(report.JobId))
                    {
                        _reports[report.JobId] = report;
                    }
                }
                catch
                {
                    // Skip corrupt report files
                }
            }
        }
        catch
        {
            // Ignore directory scanning errors on startup
        }
    }

    public LocalJobRecord StartJob(
        string sourcePath,
        string targetPath,
        string? idempotencyKey,
        ClientProjectContext clientContext,
        string expectedSourceSha256,
        int estimatedTotalItems,
        RegisteredSelection? selection = null,
        bool enqueueIfBusy = false)
    {
        lock (_jobLock)
        {
            // Reject blocked selections fail-closed before creating any job
            if (selection != null && (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker))
            {
                string reason = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "Kaynak dosyada ön kontrol engeli bulunmaktadır. Dönüştürme başlatılamaz.";
                if (!reason.Contains("[ÖN KONTROL ENGELİ]"))
                {
                    reason = $"[ÖN KONTROL ENGELİ] {reason}";
                }
                throw new InvalidOperationException(reason);
            }

            string requestedSelectionContentHash = selection != null
                ? selection.SelectionContentHash
                : OstSelectionEngine.ComputeSelectionContentHash(isFiltered: false, filter: null);

            // 1. Idempotency check: same key + identical request returns same job; different request causes 409 conflict
            if (!string.IsNullOrEmpty(idempotencyKey) && _idempotencyIndex.TryGetValue(idempotencyKey, out string? existingJobId))
            {
                if (_jobs.TryGetValue(existingJobId, out var existingRecord))
                {
                    string existingSelectionHash = existingRecord.SelectionContentHash ??
                        OstSelectionEngine.ComputeSelectionContentHash(existingRecord.IsFiltered, existingRecord.SelectionFilter);

                    if (_internalJobStates.TryGetValue(existingJobId, out var internalState))
                    {
                        bool isIdentical =
                            (internalState.JobKind == null || string.Equals(internalState.JobKind, "convert", StringComparison.Ordinal)) &&
                            string.Equals(internalState.SourcePath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(internalState.TargetPath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(internalState.ClientContext.CompanyId, clientContext.CompanyId, StringComparison.Ordinal) &&
                            string.Equals(internalState.ClientContext.ProjectId, clientContext.ProjectId, StringComparison.Ordinal) &&
                            string.Equals(internalState.ClientContext.CompanyName, clientContext.CompanyName, StringComparison.Ordinal) &&
                            string.Equals(internalState.ClientContext.ProjectName, clientContext.ProjectName, StringComparison.Ordinal) &&
                            string.Equals(internalState.SelectionContentHash ?? existingSelectionHash, requestedSelectionContentHash, StringComparison.Ordinal);

                        if (isIdentical)
                        {
                            return ReturnExistingAuthorized(existingRecord);
                        }

                        throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı parametrelerle (kaynak, hedef, şirket, proje veya seçim/filtre kriterleri) kullanılmıştır. Yeni işlem için yeni anahtar kullanılmalıdır.");
                    }

                    // Fallback for recovered jobs on disk where full paths are excluded for secrecy
                    bool matchesPublic =
                        (existingRecord.JobKind == null || string.Equals(existingRecord.JobKind, "convert", StringComparison.Ordinal)) &&
                        string.Equals(existingRecord.SourceFileName, Path.GetFileName(sourcePath), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existingRecord.TargetFileName, Path.GetFileName(targetPath), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existingRecord.ClientContext.CompanyId, clientContext.CompanyId, StringComparison.Ordinal) &&
                        string.Equals(existingRecord.ClientContext.ProjectId, clientContext.ProjectId, StringComparison.Ordinal) &&
                        string.Equals(existingRecord.ClientContext.CompanyName, clientContext.CompanyName, StringComparison.Ordinal) &&
                        string.Equals(existingRecord.ClientContext.ProjectName, clientContext.ProjectName, StringComparison.Ordinal) &&
                        string.Equals(existingSelectionHash, requestedSelectionContentHash, StringComparison.Ordinal);

                    if (matchesPublic)
                    {
                        return ReturnExistingAuthorized(existingRecord);
                    }

                    throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı parametrelerle (seçim veya filtre kriterleri dahil) kullanılmıştır.");
                }
            }

            // 2. Concurrency limit: strictly ONE active running job
            if (!string.IsNullOrEmpty(_activeRunningJobId) && !enqueueIfBusy)
            {
                throw new InvalidOperationException($"Halen devam eden etkin bir dönüştürme işlemi var (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
            }

            // 3. Create new Job Record (Public record excludes absolute paths for secrecy)
            string jobId = "job-" + Guid.NewGuid().ToString("N")[..12];
            int effectiveTotal = selection != null ? selection.SelectedMessagesCount : estimatedTotalItems;

            var record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "convert",
                IdempotencyKey = idempotencyKey,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SourceFileName = Path.GetFileName(sourcePath),
                TargetFileName = Path.GetFileName(targetPath),
                OutputPath = Path.GetFullPath(targetPath),
                Status = _activeRunningJobId == null ? "converting" : "queued",
                Stage = _activeRunningJobId == null ? "Dönüştürülüyor" : "Sırada",
                TotalItems = effectiveTotal,
                IsFiltered = selection != null,
                SelectionFilter = selection?.Filters,
                SelectionId = selection?.SelectionId,
                SelectionContentHash = requestedSelectionContentHash,
                TotalSourceMessages = selection != null ? selection.TotalSourceMessages : estimatedTotalItems,
                SelectedMessagesCount = selection != null ? selection.SelectedMessagesCount : estimatedTotalItems,
                ExcludedMessagesCount = selection != null ? selection.ExcludedMessagesCount : 0,
                MissingDateExcludedCount = selection != null ? selection.MissingDateExcludedCount : 0,
                SelectedAttachmentsCount = selection != null ? selection.SelectedAttachmentsCount : 0,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = _activeRunningJobId == null ? DateTimeOffset.UtcNow : null
            };

            PrepareSchedulingLocked(record, enqueueIfBusy);
            // Store internal state securely in memory for idempotency verification without leaking to API
            _internalJobStates[jobId] = new InternalJobState
            {
                JobId = jobId,
                JobKind = "convert",
                SourcePath = Path.GetFullPath(sourcePath),
                TargetPath = Path.GetFullPath(targetPath),
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SelectionContentHash = requestedSelectionContentHash
            };

            _jobs[jobId] = record;
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _idempotencyIndex[idempotencyKey] = jobId;
            }

            try
            {
                SaveJobRecordLocked(CloneJobRecord(record));
            }
            catch
            {
                _jobs.Remove(jobId, out _);
                _internalJobStates.Remove(jobId, out _);
                if (!string.IsNullOrEmpty(idempotencyKey) &&
                    _idempotencyIndex.TryGetValue(idempotencyKey, out var mappedId) &&
                    mappedId == jobId)
                {
                    _idempotencyIndex.Remove(idempotencyKey, out _);
                }
                throw;
            }
            ScheduleLocked(record, enqueueIfBusy, () => Task.Run(() => ExecuteConversionJob(jobId, sourcePath, targetPath, record.ClientContext, expectedSourceSha256, selection)));

            return record;
        }
    }

    private void ExecuteConversionJob(
        string jobId,
        string sourcePath,
        string targetPath,
        ClientProjectContext clientContext,
        string expectedSourceSha256,
        RegisteredSelection? selection)
    {
        var converter = new OstToPstConverter(_capacityProbe);
        var progressHandler = new SynchronousConversionProgress(p =>
        {
            UpdateProgress(jobId, p);
        });

        try
        {
            var report = converter.Convert(
                sourcePath,
                targetPath,
                jobId,
                clientContext,
                expectedSourceSha256,
                progress: progressHandler,
                cancellationToken: CancellationToken.None,
                selection: selection);

            _reports[jobId] = report;

            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var r))
                {
                    r.Status = report.ConversionSuccess ? "completed" : "failed";
                    r.Stage = report.ConversionSuccess ? "Tamamlandı" : "Hata";
                    r.CompletedAt = DateTimeOffset.UtcNow;
                    r.PercentComplete = 100;
                    r.ItemsRead = report.ItemsRead;
                    r.ItemsWritten = report.ItemsWritten;
                    r.FailedItems = report.FailedItems;
                    r.TotalSourceMessages = report.TotalSourceMessages;
                    r.SelectedMessagesCount = report.SelectedMessagesCount;
                    r.ExcludedMessagesCount = report.ExcludedMessagesCount;
                    r.MissingDateExcludedCount = report.MissingDateExcludedCount;
                    r.SelectedAttachmentsCount = report.SelectedAttachmentsCount;
                    r.OutputPath = report.OutputPath;
                    if (!report.ConversionSuccess)
                    {
                        r.ErrorMessage = report.Errors.FirstOrDefault() ?? "Dönüştürme doğrulamasında uyumsuzluklar tespit edildi.";
                    }
                }
            }

            SaveReportLocked(report);

            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var r))
                {
                    SaveJobRecordLocked(CloneJobRecord(r));
                }
            }
        }
        catch (Exception ex)
        {
            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var r))
                {
                    r.Status = "failed";
                    r.Stage = "Hata";
                    r.ErrorMessage = ex.Message;
                    r.CompletedAt = DateTimeOffset.UtcNow;
                    try
                    {
                        SaveJobRecordLocked(CloneJobRecord(r));
                    }
                    catch { }
                }
            }
        }
    }

    internal int PendingJobCount
    {
        get { lock (_jobLock) return _pendingJobs.Count; }
    }

    public LocalJobRecord StartSplitJob(
        string sourcePath,
        string outputDirParentPath,
        string? idempotencyKey,
        ClientProjectContext clientContext,
        RegisteredSplitPlan splitPlan,
        RegisteredSelection? selection = null,
        bool runInBackground = true,
        bool enqueueIfBusy = false)
    {
        lock (_jobLock)
        {
            // Reject blocked selections fail-closed before creating any job
            if (selection != null && (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker))
            {
                string reason = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "Kaynak dosyada ön kontrol engeli bulunmaktadır. Bölme işlemi başlatılamaz.";
                if (!reason.Contains("[ÖN KONTROL ENGELİ]"))
                {
                    reason = $"[ÖN KONTROL ENGELİ] {reason}";
                }
                throw new InvalidOperationException(reason);
            }

            if (!splitPlan.CanSplit)
            {
                throw new InvalidOperationException(splitPlan.BlockerReason ?? "Bölme planı geçersizdir.");
            }

            string requestedSelectionContentHash = selection != null
                ? selection.SelectionContentHash
                : OstSelectionEngine.ComputeSelectionContentHash(isFiltered: false, filter: null);

            string fingerprint = ComputeSplitRequestFingerprint(
                sourcePath,
                splitPlan.SourceSha256,
                outputDirParentPath,
                requestedSelectionContentHash,
                splitPlan.SplitMode,
                splitPlan.SizeCapBytes,
                clientContext);

            // 1. Idempotency check: same key + identical canonical request returns same job; different request causes 409 conflict
            if (!string.IsNullOrEmpty(idempotencyKey) && _idempotencyIndex.TryGetValue(idempotencyKey, out string? existingJobId))
            {
                if (_jobs.TryGetValue(existingJobId, out var existingRecord))
                {
                    string? existingFingerprint = null;
                    if (_internalJobStates.TryGetValue(existingJobId, out var internalState) && !string.IsNullOrEmpty(internalState.RequestFingerprint))
                    {
                        existingFingerprint = internalState.RequestFingerprint;
                    }
                    else if (!string.IsNullOrEmpty(existingRecord.RequestFingerprint))
                    {
                        existingFingerprint = existingRecord.RequestFingerprint;
                    }

                    if (!string.IsNullOrEmpty(existingFingerprint))
                    {
                        if (string.Equals(existingFingerprint, fingerprint, StringComparison.Ordinal))
                        {
                            return ReturnExistingAuthorized(existingRecord);
                        }

                        throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı parametrelerle (kaynak dosya/yol/hash, hedef dizin, bölme modu/boyutu, şirket, proje veya seçim kriterleri) kullanılmıştır. Yeni işlem için yeni anahtar kullanılmalıdır.");
                    }

                    // Jobs without fingerprint remain readable for backward compatibility, but must not be unsafely matched by basename; if an old key cannot be proven identical, return conflict/require a new idempotency key.
                    throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce kullanılmıştır ve kayıtlı parametreler parmak iziyle doğrulanamamaktadır. Yeni işlem için yeni anahtar kullanılmalıdır.");
                }
            }

            // 2. Concurrency limit: strictly ONE active running job
            if (!string.IsNullOrEmpty(_activeRunningJobId) && (!enqueueIfBusy || !runInBackground))
            {
                throw new InvalidOperationException($"Halen devam eden etkin bir işlem var (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
            }

            // 3. Create new Split Job Record
            string jobId = "job-" + Guid.NewGuid().ToString("N")[..12];
            int effectiveTotal = selection != null ? selection.SelectedMessagesCount : (splitPlan.YearGroups?.Sum(y => y.MessageCount) ?? 0);

            var record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "split",
                SplitMode = splitPlan.SplitMode,
                SplitSizeCapBytes = splitPlan.SizeCapBytes,
                IdempotencyKey = idempotencyKey,
                RequestFingerprint = fingerprint,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SourceFileName = Path.GetFileName(sourcePath),
                TargetFileName = string.Empty,
                OutputPath = string.Empty,
                OutputDirectoryPath = string.Empty,
                Status = _activeRunningJobId == null ? "converting" : "queued",
                Stage = _activeRunningJobId == null ? "Bölünüyor" : "Sırada",
                TotalItems = effectiveTotal,
                IsFiltered = selection != null,
                SelectionFilter = selection?.Filters,
                SelectionId = selection?.SelectionId,
                SelectionContentHash = requestedSelectionContentHash,
                TotalSourceMessages = selection != null ? selection.TotalSourceMessages : effectiveTotal,
                SelectedMessagesCount = selection != null ? selection.SelectedMessagesCount : effectiveTotal,
                ExcludedMessagesCount = selection != null ? selection.ExcludedMessagesCount : 0,
                MissingDateExcludedCount = selection != null ? selection.MissingDateExcludedCount : 0,
                SelectedAttachmentsCount = selection != null ? selection.SelectedAttachmentsCount : 0,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = _activeRunningJobId == null ? DateTimeOffset.UtcNow : null
            };

            PrepareSchedulingLocked(record, enqueueIfBusy);
            _internalJobStates[jobId] = new InternalJobState
            {
                JobId = jobId,
                JobKind = "split",
                SplitMode = splitPlan.SplitMode,
                SplitSizeCapBytes = splitPlan.SizeCapBytes,
                PlanId = splitPlan.PlanId,
                SourcePath = Path.GetFullPath(sourcePath),
                TargetPath = string.Empty,
                OutputDirectoryPath = Path.GetFullPath(outputDirParentPath),
                RequestFingerprint = fingerprint,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SelectionContentHash = requestedSelectionContentHash
            };

            _jobs[jobId] = record;
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _idempotencyIndex[idempotencyKey] = jobId;
            }

            try
            {
                SaveJobRecordLocked(CloneJobRecord(record));
            }
            catch
            {
                _jobs.Remove(jobId, out _);
                _internalJobStates.Remove(jobId, out _);
                if (!string.IsNullOrEmpty(idempotencyKey) &&
                    _idempotencyIndex.TryGetValue(idempotencyKey, out var mappedId) &&
                    mappedId == jobId)
                {
                    _idempotencyIndex.Remove(idempotencyKey, out _);
                }
                throw;
            }
            // Start split execution in background if requested
            if (runInBackground)
            {
                ScheduleLocked(record, enqueueIfBusy, () => Task.Run(() => ExecuteSplitJob(jobId, sourcePath, outputDirParentPath, record.ClientContext, splitPlan, selection, releaseSlot: false)));
            }
            else
            {
                _activeRunningJobId = jobId;
            }

            return record;
        }
    }

    internal void ExecuteSplitJob(
        string jobId,
        string sourcePath,
        string outputDirParentPath,
        ClientProjectContext clientContext,
        RegisteredSplitPlan splitPlan,
        RegisteredSelection? selection,
        bool releaseSlot = true)
    {
        var splitter = new PstSplitter(_capacityProbe);
        var progressHandler = new SynchronousConversionProgress(p =>
        {
            UpdateProgress(jobId, p);
        });

        try
        {
            var report = splitter.Split(
                sourcePath,
                outputDirParentPath,
                jobId,
                clientContext,
                splitPlan,
                progress: progressHandler,
                cancellationToken: CancellationToken.None,
                selection: selection);

            // CRITICAL: PstSplitter.Split returns only after bundle publication.
            // Populate the in-memory job/report with known final bundle path, manifest path, parts and counts
            // BEFORE the first post-publication SaveReportLocked/SaveJobRecordLocked attempt!
            _reports[jobId] = report;

            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var r))
                {
                    // The bundle exists, but success is not public until both durable records are saved.
                    r.Status = "verifying";
                    r.Stage = "İşlem kaydı tamamlanıyor";
                    r.CompletedAt = null;
                    r.PercentComplete = 99;
                    r.ItemsRead = report.ItemsRead;
                    r.ItemsWritten = report.ItemsWritten;
                    r.FailedItems = report.FailedItems;
                    r.TotalSourceMessages = report.TotalSourceMessages;
                    r.SelectedMessagesCount = report.SelectedMessagesCount;
                    r.ExcludedMessagesCount = report.ExcludedMessagesCount;
                    r.MissingDateExcludedCount = report.MissingDateExcludedCount;
                    r.SelectedAttachmentsCount = report.SelectedAttachmentsCount;
                    r.OutputPath = report.OutputPath;
                    r.OutputDirectoryPath = report.OutputDirectoryPath;
                    r.Parts = report.Parts?.Select(p => new SplitPartReport
                    {
                        PartFileName = p.PartFileName,
                        PartFullPath = p.PartFullPath,
                        PartSizeBytes = p.PartSizeBytes,
                        PartSha256 = p.PartSha256,
                        ItemsWritten = p.ItemsWritten,
                        TotalAttachmentsVerified = p.TotalAttachmentsVerified,
                        TotalCidVerified = p.TotalCidVerified,
                        GroupKey = p.GroupKey,
                        ReopenedPstVerification = p.ReopenedPstVerification
                    }).ToList() ?? new List<SplitPartReport>();

                    if (!report.ConversionSuccess)
                    {
                        r.ErrorMessage = report.Errors.FirstOrDefault() ?? "Bölme doğrulamasında uyumsuzluklar tespit edildi.";
                    }
                }
            }

            SaveReportLocked(report);

            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var r))
                {
                    var completed = CloneJobRecord(r);
                    completed.Status = report.ConversionSuccess ? "completed" : "failed";
                    completed.Stage = report.ConversionSuccess ? "Tamamlandı" : "Hata";
                    completed.CompletedAt = DateTimeOffset.UtcNow;
                    completed.PercentComplete = 100;
                    SaveJobRecordLocked(completed);
                    // Publish a complete immutable snapshot only after persistence succeeds.
                    _jobs[jobId] = completed;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var r))
                {
                    r.Status = "failed";
                    r.Stage = "Hata";
                    r.ErrorMessage = $"Bölümleme tamamlanamadı: {ex.Message}";
                    r.CompletedAt = DateTimeOffset.UtcNow;

                    // A secondary persistence failure in catch must NOT prevent _activeRunningJobId release in finally!
                    try
                    {
                        SaveJobRecordLocked(CloneJobRecord(r));
                    }
                    catch
                    {
                        // Swallowed in catch so execution proceeds cleanly to finally
                    }
                }
            }
        }
        finally
        {
            if (releaseSlot) CompleteAndDispatch(jobId);
        }
    }

    public LocalJobRecord StartMimeJob(
        MimeSourceManifest manifest,
        string targetPath,
        string? idempotencyKey,
        ClientProjectContext clientContext,
        RegisteredSelection? selection = null,
        bool runInBackground = true,
        bool enqueueIfBusy = false)
    {
        manifest = System.Text.Json.JsonSerializer.Deserialize<MimeSourceManifest>(System.Text.Json.JsonSerializer.Serialize(manifest)) ?? throw new InvalidDataException("MIME kaynağı dondurulamadı.");
        if (selection is not null)
        {
            selection = System.Text.Json.JsonSerializer.Deserialize<RegisteredSelection>(System.Text.Json.JsonSerializer.Serialize(selection)) ?? throw new InvalidDataException("MIME seçimi dondurulamadı.");
            if (selection.ExecutionPolicy is not null) _ = BitigMail.Engine.Planning.MimeExecutionPolicy.Validate(manifest, selection);
        }
        lock (_jobLock)
        {
            var sourceQualification = new NormalizedSourceQualificationReader().Read(manifest);
            if (selection?.QualificationFingerprint is not null &&
                (!string.Equals(sourceQualification?.Fingerprint, selection.QualificationFingerprint, StringComparison.Ordinal) ||
                 sourceQualification?.DateFilterBlocked != selection.DateFilterBlocked || sourceQualification?.IsPartial != selection.QualificationIsPartial))
                throw new InvalidOperationException("Kaynak qualification bilgisi dondurulmuş seçim planıyla uyuşmuyor.");
            if (selection != null && (!selection.CanConvert || selection.HasTrialBlocker || selection.HasPreflightBlocker))
            {
                string reason = selection.PreflightBlockerReason ?? selection.TrialBlockerReason ?? selection.BlockerReason ?? "Kaynakta ön kontrol engeli bulunmaktadır. İçe aktarma başlatılamaz.";
                if (!reason.Contains("[ÖN KONTROL ENGELİ]"))
                {
                    reason = $"[ÖN KONTROL ENGELİ] {reason}";
                }
                throw new InvalidOperationException(reason);
            }

            string requestedSelectionContentHash = selection != null
                ? selection.SelectionContentHash
                : OstSelectionEngine.ComputeSelectionContentHash(isFiltered: false, filter: null);
            string fingerprint = ComputeMimeRequestFingerprint(
                manifest.AggregateFingerprint + "|qualification:" + (sourceQualification?.Fingerprint ?? "ordinary-eml"),
                targetPath,
                requestedSelectionContentHash,
                clientContext);

            if (!string.IsNullOrEmpty(idempotencyKey) && _idempotencyIndex.TryGetValue(idempotencyKey, out string? existingJobId))
            {
                if (_jobs.TryGetValue(existingJobId, out var existingRecord))
                {
                    string? existingFingerprint = null;
                    if (_internalJobStates.TryGetValue(existingJobId, out var internalState) && !string.IsNullOrEmpty(internalState.RequestFingerprint))
                    {
                        existingFingerprint = internalState.RequestFingerprint;
                    }
                    else if (!string.IsNullOrEmpty(existingRecord.RequestFingerprint))
                    {
                        existingFingerprint = existingRecord.RequestFingerprint;
                    }

                    if (!string.IsNullOrEmpty(existingFingerprint))
                    {
                        if (string.Equals(existingFingerprint, fingerprint, StringComparison.Ordinal))
                        {
                            return ReturnExistingAuthorized(existingRecord);
                        }

                        throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı parametrelerle (kaynak manifestosu, hedef PST, şirket, proje veya seçim kriterleri) kullanılmıştır. Yeni işlem için yeni anahtar kullanılmalıdır.");
                    }

                    throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce kullanılmıştır ve kayıtlı parametreler parmak iziyle doğrulanamamaktadır. Yeni işlem için yeni anahtar kullanılmalıdır.");
                }
            }

            if (!string.IsNullOrEmpty(_activeRunningJobId) && (!enqueueIfBusy || !runInBackground))
            {
                throw new InvalidOperationException($"Halen devam eden etkin bir işlem var (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
            }

            string jobId = "job-" + Guid.NewGuid().ToString("N")[..12];
            int effectiveTotal = selection != null ? selection.SelectedMessagesCount : manifest.Entries.Count;

            var record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "mime-import",
                IdempotencyKey = idempotencyKey,
                RequestFingerprint = fingerprint,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SourceFileName = manifest.SourceKind == "mbox" ? Path.GetFileName(manifest.RootPath) : $"{manifest.TotalFiles} EML Dosyası",
                TargetFileName = Path.GetFileName(targetPath),
                OutputPath = Path.GetFullPath(targetPath),
                Status = _activeRunningJobId == null ? "converting" : "queued",
                Stage = _activeRunningJobId == null ? "İçe Aktarılıyor" : "Sırada",
                TotalItems = effectiveTotal,
                IsFiltered = selection != null && selection.SelectionContentHash != "unfiltered",
                SelectionFilter = selection?.Filters,
                SelectionId = selection?.SelectionId,
                SelectionContentHash = requestedSelectionContentHash,
                SourceKind = manifest.SourceKind,
                Dialect = manifest.Dialect,
                SourceSetFingerprint = manifest.AggregateFingerprint,
                IgnoredNonEmlFilesCount = manifest.IgnoredNonEmlFilesCount,
                TotalSourceMessages = manifest.Entries.Count,
                SelectedMessagesCount = effectiveTotal,
                ExcludedMessagesCount = selection != null ? selection.ExcludedMessagesCount : 0,
                MissingDateExcludedCount = selection != null ? selection.MissingDateExcludedCount : 0,
                SelectedAttachmentsCount = selection != null ? selection.SelectedAttachmentsCount : 0,
                QualificationFingerprint = sourceQualification?.Fingerprint,
                DateFilterBlocked = sourceQualification?.DateFilterBlocked ?? false,
                QualificationIsPartial = sourceQualification?.IsPartial ?? false,
                QualificationWarnings = sourceQualification?.Warnings.ToList() ?? new(),
                SdkQualification = AsposeSdkStartupService.CurrentStatus.Qualification,
                FolderMappingFingerprint = selection?.ExecutionPolicy?.MappingFingerprint,
                DuplicatePolicy = selection?.ExecutionPolicy?.DuplicatePolicy.ToString(),
                SkippedDuplicateItemIds = selection?.ExecutionPolicy?.SkippedIds.ToList() ?? new(),
                FrozenSelectionFingerprint = selection?.ExecutionPolicy?.Fingerprint,
                AdvancedFilterCanonicalJson = selection?.AdvancedFilterCanonicalJson,
                AdvancedFilterFingerprint = selection?.AdvancedFilterFingerprint,
                AdvancedFilterUnknownCount = selection?.AdvancedFilterUnknownCount ?? 0,
                CreatedAt = DateTimeOffset.UtcNow,
                StartedAt = _activeRunningJobId == null ? DateTimeOffset.UtcNow : null
            };

            PrepareSchedulingLocked(record, enqueueIfBusy);
            _internalJobStates[jobId] = new InternalJobState
            {
                JobId = jobId,
                JobKind = "mime-import",
                SourcePath = manifest.RootPath,
                TargetPath = Path.GetFullPath(targetPath),
                RequestFingerprint = fingerprint,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SelectionContentHash = requestedSelectionContentHash
            };

            _jobs[jobId] = record;
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _idempotencyIndex[idempotencyKey] = jobId;
            }

            try
            {
                SaveJobRecordLocked(CloneJobRecord(record));
            }
            catch
            {
                _jobs.Remove(jobId, out _);
                _internalJobStates.Remove(jobId, out _);
                if (!string.IsNullOrEmpty(idempotencyKey) &&
                    _idempotencyIndex.TryGetValue(idempotencyKey, out var mappedId) &&
                    mappedId == jobId)
                {
                    _idempotencyIndex.Remove(idempotencyKey, out _);
                }
                throw;
            }
            if (runInBackground)
            {
                ScheduleLocked(record, enqueueIfBusy, () => Task.Run(() => ExecuteMimeImportJob(jobId, manifest, targetPath, record.ClientContext, selection)));
            }
            else
            {
                _activeRunningJobId = jobId;
                try { ExecuteMimeImportJob(jobId, manifest, targetPath, record.ClientContext, selection); }
                finally { CompleteAndDispatch(jobId); }
            }

            return _jobs.TryGetValue(jobId, out var latest) ? CloneJobRecord(latest) : record;
        }
    }

    internal void ExecuteMimeImportJob(
        string jobId,
        MimeSourceManifest manifest,
        string targetPath,
        ClientProjectContext clientContext,
        RegisteredSelection? selection)
    {
        var converter = new MimeToPstConverter(_capacityProbe);
        var progressHandler = new SynchronousConversionProgress(p =>
        {
            UpdateProgress(jobId, p);
        });

        try
        {
            LocalJobRecord frozen;
            lock (_jobLock) frozen = _jobs.TryGetValue(jobId, out var current) ? CloneJobRecord(current) : throw new InvalidOperationException("MIME iş planı bulunamadı.");
            var qualification = new NormalizedSourceQualificationReader().Read(manifest);
            if (!string.Equals(qualification?.Fingerprint, frozen.QualificationFingerprint, StringComparison.Ordinal) ||
                (qualification?.DateFilterBlocked ?? false) != frozen.DateFilterBlocked || (qualification?.IsPartial ?? false) != frozen.QualificationIsPartial)
                throw new InvalidOperationException("Kaynak qualification bilgisi kuyrukta beklerken değişti.");
            var report = converter.Convert(
                manifest,
                targetPath,
                jobId,
                clientContext,
                progress: progressHandler,
                cancellationToken: CancellationToken.None,
                selection: selection);
            if (report.MimeImport is not null)
            {
                report.MimeImport.QualificationFingerprint = frozen.QualificationFingerprint;
                report.MimeImport.DateFilterBlocked = frozen.DateFilterBlocked;
                report.MimeImport.QualificationIsPartial = frozen.QualificationIsPartial;
                report.MimeImport.QualificationWarnings = frozen.QualificationWarnings.ToList();
                report.MimeImport.SdkQualification = frozen.SdkQualification;
            }

            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var pending))
                {
                    pending.Status = "verifying";
                    pending.Stage = "Sonuç kaydediliyor";
                    pending.OutputPath = report.OutputPath;
                    pending.ItemsRead = report.ItemsRead;
                    pending.ItemsWritten = report.ItemsWritten;
                    pending.MimeImport = report.MimeImport;
                }
            }
            SaveReportLocked(report);
            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var pending))
                {
                    var completed = CloneJobRecord(pending);
                    completed.Status = "completed";
                    completed.Stage = "Tamamlandı";
                    completed.PercentComplete = 100;
                    completed.CompletedAt = DateTimeOffset.UtcNow;
                    SaveJobRecordLocked(completed);
                    _reports[jobId] = report;
                    _jobs[jobId] = completed;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_jobLock)
            {
                if (_jobs.TryGetValue(jobId, out var r))
                {
                    r.Status = "failed";
                    r.Stage = "Hata";
                    r.ErrorMessage = $"İçe aktarma tamamlanamadı: {ex.Message}";
                    r.CompletedAt = DateTimeOffset.UtcNow;

                    try
                    {
                        SaveJobRecordLocked(CloneJobRecord(r));
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private void UpdateProgress(string jobId, ConversionJobProgress progress)
    {
        lock (_jobLock)
        {
            if (!_jobs.TryGetValue(jobId, out var r)) return;

            // Completed, failed, or interrupted job CANNOT regress to converting or verifying
            if (r.Status == "completed" || r.Status == "failed" || r.Status == "interrupted") return;

            r.ItemsRead = progress.ItemsRead;
            r.ItemsWritten = progress.ItemsWritten;
            r.FailedItems = progress.FailedItems;
            r.CurrentFolder = progress.CurrentFolder;
            r.ProgressPhase = progress.ProgressPhase;
            r.PhaseCompleted = progress.PhaseCompleted;
            r.PhaseTotal = progress.PhaseTotal;
            r.Stage = progress.Stage;
            r.Status = progress.Status;
            if (r.TotalItems > 0)
            {
                r.PercentComplete = Math.Min(99, (int)((double)r.ItemsWritten / r.TotalItems * 100));
            }

            SaveJobRecordLocked(CloneJobRecord(r));
        }
    }

    public LocalJobRecord? GetJob(string jobId)
    {
        return _jobs.TryGetValue(jobId, out var record) ? CloneJobRecord(record) : null;
    }

    public ConversionReport? GetReport(string jobId)
    {
        return _reports.TryGetValue(jobId, out var report) ? report : null;
    }

    public List<LocalJobRecord> GetAllJobs()
    {
        return _jobs.Values.OrderByDescending(j => j.CreatedAt).Select(CloneJobRecord).ToList();
    }

    public PagedJobsResult GetJobsPage(int page = 1, int pageSize = 50, string? status = null, string? search = null, string? jobKind = null,Func<LocalJobRecord,bool>? authorize=null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        IEnumerable<LocalJobRecord> query = _jobs.Values;
        if(authorize is not null)query=query.Where(authorize);
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = status.ToLowerInvariant() switch
            {
                "running" => query.Where(job => job.Status is "converting" or "verifying"),
                "active" => query.Where(job => job.Status is "converting" or "verifying" or "queued"),
                "attention" => query.Where(job => job.Status is "interrupted" or "failed"),
                "completed" => query.Where(job => job.Status == "completed"),
                _ => query.Where(job => string.Equals(job.Status, status, StringComparison.OrdinalIgnoreCase))
            };
        }
        if (!string.IsNullOrWhiteSpace(jobKind))
            query = query.Where(job => string.Equals(job.JobKind, jobKind.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(job =>
                job.JobId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                job.JobKind.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                job.SourceFileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                job.TargetFileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                job.ClientContext.CompanyName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                job.ClientContext.ProjectName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        var ordered = query.OrderByDescending(job => job.CreatedAt).ThenByDescending(job => job.JobId);
        int totalCount = ordered.Count();
        long rawOffset = ((long)page - 1L) * pageSize;
        var items = rawOffset > int.MaxValue
            ? new List<LocalJobRecord>()
            : ordered.Skip((int)rawOffset).Take(pageSize).Select(CloneJobRecord).ToList();
        return new PagedJobsResult
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public LocalJobRecord CancelPendingJob(string jobId, string companyId, string projectId)
    {
        lock (_jobLock)
        {
            if (!_jobs.TryGetValue(jobId, out var record)) throw new KeyNotFoundException($"İş bulunamadı: {jobId}");
            if (record.ClientContext.CompanyId != companyId || record.ClientContext.ProjectId != projectId)
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu iş ile uyuşmuyor.");
            if (_activeRunningJobId == jobId || record.Status != "queued" || !record.WaitingAtShutdown)
                throw new InvalidOperationException("Yalnızca henüz başlamamış bekleyen işler iptal edilebilir.");
            var retained = _pendingJobs.Where(item => item.JobId != jobId).ToArray();
            if (retained.Length == _pendingJobs.Count) throw new InvalidOperationException("Bekleyen iş kuyrukta bulunamadı.");
            var cancelled = CloneJobRecord(record);
            cancelled.Status = cancelled.NeverStartedQueued ? "cancelled" : "interrupted";
            cancelled.Stage = cancelled.NeverStartedQueued ? "Sıradan kaldırıldı" : "Yeniden başlatma beklemesi iptal edildi";
            cancelled.ErrorMessage = cancelled.NeverStartedQueued ? null : "Bekleyen yeniden başlatma isteği iptal edildi; önceki aktarım kanıtları korunmuştur ve iş yeniden devam ettirilebilir.";
            cancelled.WaitingAtShutdown = false;
            cancelled.CompletedAt = DateTimeOffset.UtcNow;
            SaveJobRecordLocked(cancelled);
            _pendingJobs.Clear();
            _pendingJobs.AddRange(retained);
            _jobs[jobId] = CloneJobRecord(cancelled);
            return CloneJobRecord(cancelled);
        }
    }

    private void SaveJobRecordLocked(LocalJobRecord record)
    {
        OnBeforeSaveJobRecord?.Invoke(record);

        lock (_persistLock)
        {
            string filePath = Path.Combine(_jobsDir, $"{record.JobId}.json");
            string json = JsonSerializer.Serialize(record, JsonOptions);
            JobSnapshotPersistence.Write(filePath, json);
        }
    }

    private void SaveReportLocked(ConversionReport report)
    {
        OnBeforeSaveReport?.Invoke(report);

        lock (_persistLock)
        {
            string filePath = Path.Combine(_reportsDir, $"{report.JobId}.json");
            string json = JsonSerializer.Serialize(report, JsonOptions);
            JobSnapshotPersistence.Write(filePath, json);
        }
    }

    private static string ReadFileWithRetry(string path, int maxRetries = 5)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                Thread.Sleep(50);
            }
        }
        return File.ReadAllText(path);
    }

    private static LocalJobRecord CloneJobRecord(LocalJobRecord r)
    {
        return new LocalJobRecord
        {
            JobId = r.JobId,
            RecoveryWorkerUnresolved = r.RecoveryWorkerUnresolved,
            RecoveryOutcome = r.RecoveryOutcome,
            RecoveryOriginalTotal = r.RecoveryOriginalTotal,
            RecoveryFailedBoundaryCount = r.RecoveryFailedBoundaryCount,
            RecoveryReportSha256 = r.RecoveryReportSha256,
            ActorUserId = r.ActorUserId,
            ActorSecurityVersion = r.ActorSecurityVersion,
            RequiredScopes = Array.AsReadOnly((r.RequiredScopes ?? Array.Empty<JobAuthorizationScope>()).Select(s => new JobAuthorizationScope(s.CompanyId, s.ProjectId)).ToArray()),
            IdempotencyKey = r.IdempotencyKey,
            SourceFileName = r.SourceFileName,
            TargetFileName = r.TargetFileName,
            OutputPath = r.OutputPath,
            Status = r.Status,
            Stage = r.Stage,
            ItemsRead = r.ItemsRead,
            ItemsWritten = r.ItemsWritten,
            FailedItems = r.FailedItems,
            TotalItems = r.TotalItems,
            ProgressPhase = r.ProgressPhase,
            PhaseCompleted = r.PhaseCompleted,
            PhaseTotal = r.PhaseTotal,
            PercentComplete = r.PercentComplete,
            CurrentFolder = r.CurrentFolder,
            ErrorMessage = r.ErrorMessage,
            IsFiltered = r.IsFiltered,
            SelectionFilter = r.SelectionFilter != null ? new ConversionSelectionFilter
            {
                FolderIds = r.SelectionFilter.FolderIds?.ToList() ?? new List<string>(),
                SelectedFolders = r.SelectionFilter.SelectedFolders?.Select(s => new SelectedFolderSnapshot
                {
                    FolderId = s.FolderId,
                    FolderPath = s.FolderPath,
                    DisplayName = s.DisplayName
                }).ToList() ?? new List<SelectedFolderSnapshot>(),
                StartDate = r.SelectionFilter.StartDate,
                EndDate = r.SelectionFilter.EndDate,
                TimeZone = r.SelectionFilter.TimeZone,
                DatePolicy = r.SelectionFilter.DatePolicy
            } : null,
            SelectionId = r.SelectionId,
            SelectionContentHash = r.SelectionContentHash,
            TotalSourceMessages = r.TotalSourceMessages,
            SelectedMessagesCount = r.SelectedMessagesCount,
            ExcludedMessagesCount = r.ExcludedMessagesCount,
            MissingDateExcludedCount = r.MissingDateExcludedCount,
            SelectedAttachmentsCount = r.SelectedAttachmentsCount,
            CreatedAt = r.CreatedAt,
            StartedAt = r.StartedAt,
            CompletedAt = r.CompletedAt,
            JobKind = r.JobKind,
            SplitMode = r.SplitMode,
            SplitSizeCapBytes = r.SplitSizeCapBytes,
            OutputDirectoryPath = r.OutputDirectoryPath,
            RequestFingerprint = r.RequestFingerprint,
            SourceKind = r.SourceKind,
            Dialect = r.Dialect,
            SourceSetFingerprint = r.SourceSetFingerprint,
            IgnoredNonEmlFilesCount = r.IgnoredNonEmlFilesCount,
            MimeImport = r.MimeImport,
            QualificationFingerprint = r.QualificationFingerprint,
            DateFilterBlocked = r.DateFilterBlocked,
            QualificationIsPartial = r.QualificationIsPartial,
            QualificationWarnings = r.QualificationWarnings.ToList(),
            SdkQualification = r.SdkQualification,
            ImapTransfer = r.ImapTransfer,
            BridgeTransfer = r.BridgeTransfer,
            PlanId = r.PlanId,
            ArchiveId = r.ArchiveId,
            ArchiveName = r.ArchiveName,
            WaitingAtShutdown = r.WaitingAtShutdown,
            NeverStartedQueued = r.NeverStartedQueued,
            WaitingPriority = r.WaitingPriority,
            AdvancedFilterCanonicalJson = r.AdvancedFilterCanonicalJson,
            AdvancedFilterFingerprint = r.AdvancedFilterFingerprint,
            AdvancedFilterUnknownCount = r.AdvancedFilterUnknownCount,
            FolderMappingFingerprint = r.FolderMappingFingerprint,
            DuplicatePolicy = r.DuplicatePolicy,
            SkippedDuplicateItemIds = r.SkippedDuplicateItemIds.ToList(),
            FrozenSelectionFingerprint = r.FrozenSelectionFingerprint,
            Parts = r.Parts?.Select(p => new SplitPartReport
            {
                PartFileName = p.PartFileName,
                PartFullPath = p.PartFullPath,
                PartSizeBytes = p.PartSizeBytes,
                PartSha256 = p.PartSha256,
                ItemsWritten = p.ItemsWritten,
                TotalAttachmentsVerified = p.TotalAttachmentsVerified,
                TotalCidVerified = p.TotalCidVerified,
                GroupKey = p.GroupKey,
                ReopenedPstVerification = p.ReopenedPstVerification
            }).ToList() ?? new List<SplitPartReport>(),
            ClientContext = new ClientProjectContext
            {
                CompanyId = r.ClientContext.CompanyId,
                CompanyName = r.ClientContext.CompanyName,
                ProjectId = r.ClientContext.ProjectId,
                ProjectName = r.ClientContext.ProjectName
            }
        };
    }

    public static string ComputeSplitRequestFingerprint(
        string sourcePath,
        string sourceSha256,
        string outputDirParentPath,
        string selectionContentHash,
        string splitMode,
        long? sizeCapBytes,
        ClientProjectContext clientContext)
    {
        string normSource = Path.GetFullPath(sourcePath).Trim();
        string normSha = (sourceSha256 ?? string.Empty).Trim().ToLowerInvariant();
        string normTargetParent = Path.GetFullPath(outputDirParentPath).Trim();
        string normSelHash = (selectionContentHash ?? string.Empty).Trim();
        string normMode = (splitMode ?? string.Empty).Trim().ToLowerInvariant();
        string normCap = sizeCapBytes?.ToString() ?? string.Empty;
        string companyId = clientContext.CompanyId ?? string.Empty;
        string projectId = clientContext.ProjectId ?? string.Empty;
        string companyName = clientContext.CompanyName ?? string.Empty;
        string projectName = clientContext.ProjectName ?? string.Empty;

        string raw = string.Join("\n", new[]
        {
            normSource,
            normSha,
            normTargetParent,
            normSelHash,
            normMode,
            normCap,
            companyId,
            projectId,
            companyName,
            projectName
        });

        using var sha = SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string ComputeMimeRequestFingerprint(
        string manifestFingerprint,
        string targetPstPath,
        string selectionContentHash,
        ClientProjectContext clientContext)
    {
        string normFingerprint = (manifestFingerprint ?? string.Empty).Trim().ToLowerInvariant();
        string normTarget = Path.GetFullPath(targetPstPath).Trim();
        string normSelHash = (selectionContentHash ?? string.Empty).Trim();
        string companyId = clientContext.CompanyId ?? string.Empty;
        string projectId = clientContext.ProjectId ?? string.Empty;
        string companyName = clientContext.CompanyName ?? string.Empty;
        string projectName = clientContext.ProjectName ?? string.Empty;

        string raw = string.Join("\n", new[]
        {
            "mime-import",
            normFingerprint,
            normTarget,
            normSelHash,
            companyId,
            projectId,
            companyName,
            projectName
        });

        using var sha = SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    // Persist before the converter mutates its progress object again. Callback
    // failures must propagate into ExecuteConversionJob's failure handling.
    private sealed class SynchronousConversionProgress : IProgress<ConversionJobProgress>
    {
        private readonly Action<ConversionJobProgress> _handler;

        public SynchronousConversionProgress(Action<ConversionJobProgress> handler)
        {
            _handler = handler;
        }

        public void Report(ConversionJobProgress value) => _handler(value);
    }

    private class InternalJobState
    {
        public string JobId { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string OutputDirectoryPath { get; set; } = string.Empty;
        public string? JobKind { get; set; }
        public string? SplitMode { get; set; }
        public long? SplitSizeCapBytes { get; set; }
        public string? PlanId { get; set; }
        public string? RequestFingerprint { get; set; }
        public ClientProjectContext ClientContext { get; set; } = new();
        public string? SelectionContentHash { get; set; }
    }
}
