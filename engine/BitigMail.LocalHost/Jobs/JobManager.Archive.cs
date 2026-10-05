using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;

namespace BitigMail.LocalHost.Jobs;

public partial class JobManager
{
    public virtual LocalJobRecord StartArchiveIngestJob(
        ArchiveIngestPlan plan,
        string? idempotencyKey,
        ClientProjectContext clientContext,
        ArchiveStorageManager storageManager,
        ArchiveSearchIndex searchIndex,
        ArchivePlanStore planStore,
        FileHandleRegistry handleRegistry,
        bool runInBackground = true,
        bool enqueueIfBusy = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clientContext);
        ArgumentNullException.ThrowIfNull(storageManager);
        ArgumentNullException.ThrowIfNull(searchIndex);
        ArgumentNullException.ThrowIfNull(planStore);
        ArgumentNullException.ThrowIfNull(handleRegistry);

        plan = planStore.GetPlan(plan.PlanId) ?? throw new InvalidOperationException("Kalıcı arşivleme planı bulunamadı.");
        if (plan.CompanyId != clientContext.CompanyId || plan.ProjectId != clientContext.ProjectId)
        {
            throw new InvalidOperationException("Arşivleme planı müşteri veya proje kapsamı ile uyuşmuyor.");
        }

        if (plan.Items.Count == 0)
        {
            throw new InvalidOperationException("Arşivleme planında öğe bulunmuyor.");
        }
        if (!plan.CanIngest)
        {
            throw new InvalidOperationException(plan.BlockerReason ?? "Arşivleme planında ön kontrol engeli bulunmaktadır.");
        }

        string jobId;
        LocalJobRecord record;
        ArchiveIngestWorker worker;

        lock (_jobLock)
        {
            // 1. Idempotency check: same key + identical plan returns same job; different plan throws typed error
            if (!string.IsNullOrEmpty(idempotencyKey) && _idempotencyIndex.TryGetValue(idempotencyKey, out string? existingJobId))
            {
                if (_jobs.TryGetValue(existingJobId, out var existingRecord))
                {
                    string? existingPlanId = null;
                    if (_internalJobStates.TryGetValue(existingJobId, out var internalState))
                    {
                        existingPlanId = internalState.PlanId;
                    }
                    existingPlanId ??= existingRecord.PlanId;

                    if (string.Equals(existingRecord.JobKind, "archive-ingest", StringComparison.Ordinal))
                    {
                        bool isIdentical =
                            string.Equals(existingPlanId, plan.PlanId, StringComparison.Ordinal) &&
                            string.Equals(existingRecord.ClientContext.CompanyId, clientContext.CompanyId, StringComparison.Ordinal) &&
                            string.Equals(existingRecord.ClientContext.ProjectId, clientContext.ProjectId, StringComparison.Ordinal);

                        if (isIdentical)
                        {
                            return ReturnExistingAuthorized(existingRecord);
                        }

                        throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı bir arşiv planı veya kapsamı ile kullanılmıştır.");
                    }

                    throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı bir işlem için kullanılmıştır.");
                }
            }

            // 1b. Same plan with different key returns existing job (no duplicate jobs for same plan)
            var duplicateJob = _jobs.Values.FirstOrDefault(j =>
                string.Equals(j.JobKind, "archive-ingest", StringComparison.Ordinal) &&
                string.Equals(j.PlanId, plan.PlanId, StringComparison.Ordinal) &&
                string.Equals(j.ClientContext.CompanyId, clientContext.CompanyId, StringComparison.Ordinal) &&
                string.Equals(j.ClientContext.ProjectId, clientContext.ProjectId, StringComparison.Ordinal));
            if (duplicateJob != null)
            {
                return ReturnExistingAuthorized(duplicateJob);
            }

            // 2. Concurrency limit: shared single active slot with all other jobs
            if (!string.IsNullOrEmpty(_activeRunningJobId) && (!enqueueIfBusy || !runInBackground))
            {
                throw new InvalidOperationException($"Halen devam eden etkin bir yerel işlem bulunmaktadır (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
            }
            if (_activeRunningJobId != null && enqueueIfBusy && _pendingJobs.Count >= PendingJobLimit)
                throw new InvalidOperationException($"Bekleyen iş kuyruğu dolu ({PendingJobLimit}).");

            jobId = "job-" + Guid.NewGuid().ToString("N")[..12];

            record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "archive-ingest",
                PlanId = plan.PlanId,
                ArchiveId = plan.ArchiveId,
                ArchiveName = plan.ArchiveName,
                IdempotencyKey = idempotencyKey,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SourceFileName = plan.ArchiveName,
                TargetFileName = plan.ArchiveName,
                Status = "queued",
                Stage = "Sırada",
                TotalItems = plan.TotalItems,
                TotalSourceMessages = plan.TotalItems,
                SelectedMessagesCount = plan.TotalItems,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var internalStateRecord = new InternalJobState
            {
                JobId = jobId,
                JobKind = "archive-ingest",
                PlanId = plan.PlanId,
                SourcePath = plan.SourceHandle ?? plan.SourceJobId ?? string.Empty,
                TargetPath = plan.ArchiveId,
                ClientContext = record.ClientContext
            };

            IncludeSourceJobScopes(record, plan.SourceJobId);
            PrepareSchedulingLocked(record, enqueueIfBusy);
            try
            {
                SaveJobRecordLocked(CloneJobRecord(record));
            }
            catch
            {
                string filePath = Path.Combine(_jobsDir, $"{jobId}.json");
                try { File.Delete(filePath); } catch { }
                throw;
            }

            _jobs[jobId] = CloneJobRecord(record);
            _internalJobStates[jobId] = internalStateRecord;
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _idempotencyIndex[idempotencyKey] = jobId;
            }

            worker = new ArchiveIngestWorker(
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                this,
                r => SaveJobRecord(r),
                _capacityProbe);

            if (runInBackground)
                ScheduleLocked(record, enqueueIfBusy, async () =>
                {
                    try { await worker.ExecuteIngestAsync(record, plan, CancellationToken.None); }
                    catch { }
                });
            else
                _activeRunningJobId = jobId;
        }

        if (!runInBackground)
        {
            try
            {
                EnsureSynchronousDispatch(record);
                worker.ExecuteIngestAsync(record, plan, CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                CompleteAndDispatch(jobId);
            }
        }

        lock (_jobLock)
        {
            return CloneJobRecord(_jobs.TryGetValue(jobId, out var current) ? current : record);
        }
    }

    public virtual LocalJobRecord ResumeArchiveIngestJob(
        string jobId,
        ClientProjectContext clientContext,
        ArchiveStorageManager storageManager,
        ArchiveSearchIndex searchIndex,
        ArchivePlanStore planStore,
        FileHandleRegistry handleRegistry,
        bool runInBackground = true,
        bool enqueueIfBusy = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(clientContext);

        LocalJobRecord record;
        ArchiveIngestPlan plan;
        ArchiveIngestWorker worker;

        lock (_jobLock)
        {
            if (!_jobs.TryGetValue(jobId, out var existingRecord))
            {
                throw new KeyNotFoundException($"İş bulunamadı: {jobId}");
            }

            if (!string.Equals(existingRecord.JobKind, "archive-ingest", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"'{jobId}' numaralı iş bir arşivleme işi değildir.");
            }

            if (!string.Equals(existingRecord.ClientContext.CompanyId, clientContext.CompanyId, StringComparison.Ordinal) ||
                !string.Equals(existingRecord.ClientContext.ProjectId, clientContext.ProjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu iş ile uyuşmuyor.");
            }

            if (existingRecord.Status == "completed")
            {
                throw new InvalidOperationException("Tamamlanmış bir arşivleme işi tekrar başlatılamaz.");
            }

            if (IsActiveOrPendingLocked(jobId)) return ReturnExistingAuthorized(existingRecord);

            if (existingRecord.NeverStartedQueued)
                throw new InvalidOperationException("Hiç başlatılmamış bekleyen iş devam ettirilemez; yeniden planlama gereklidir.");

            if (!string.IsNullOrEmpty(_activeRunningJobId) && (!enqueueIfBusy || !runInBackground))
            {
                throw new InvalidOperationException($"Halen devam eden etkin bir yerel işlem bulunmaktadır (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
            }
            if (_activeRunningJobId != null && enqueueIfBusy && _pendingJobs.Count >= PendingJobLimit)
                throw new InvalidOperationException($"Bekleyen iş kuyruğu dolu ({PendingJobLimit}).");

            string? planId = existingRecord.PlanId;
            if (string.IsNullOrEmpty(planId) && _internalJobStates.TryGetValue(jobId, out var internalState))
            {
                planId = internalState.PlanId;
            }

            if (string.IsNullOrEmpty(planId))
            {
                throw new InvalidOperationException("İş ile ilişkili arşivleme planı bulunamadı.");
            }

            var loadedPlan = planStore.GetPlan(planId);
            if (loadedPlan == null)
            {
                throw new InvalidOperationException($"Arşivleme planı ('{planId}') diskte bulunamadı.");
            }
            plan = loadedPlan;

            record = CloneJobRecord(existingRecord);
            record.Status = "queued";
            record.Stage = "Yeniden Başlatılıyor";
            record.ErrorMessage = null;
            IncludeSourceJobScopes(record, plan.SourceJobId);
            PrepareSchedulingLocked(record, enqueueIfBusy);
            SaveJobRecordLocked(CloneJobRecord(record));
            _jobs[jobId] = CloneJobRecord(record);

            worker = new ArchiveIngestWorker(
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                this,
                r => SaveJobRecord(r),
                _capacityProbe);

            if (runInBackground)
                ScheduleLocked(record, enqueueIfBusy, async () =>
                {
                    try { await worker.ExecuteIngestAsync(record, plan, CancellationToken.None); }
                    catch { }
                });
            else
                _activeRunningJobId = jobId;
        }

        if (!runInBackground)
        {
            try
            {
                EnsureSynchronousDispatch(record);
                worker.ExecuteIngestAsync(record, plan, CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                CompleteAndDispatch(jobId);
            }
        }

        lock (_jobLock)
        {
            return CloneJobRecord(_jobs.TryGetValue(jobId, out var current) ? current : record);
        }
    }

    public virtual LocalJobRecord StartArchiveReindexJob(
        string archiveId,
        string? idempotencyKey,
        ClientProjectContext clientContext,
        ArchiveStorageManager storageManager,
        ArchiveSearchIndex searchIndex,
        ArchivePlanStore planStore,
        FileHandleRegistry handleRegistry,
        bool runInBackground = true,
        bool enqueueIfBusy = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveId);
        ArgumentNullException.ThrowIfNull(clientContext);
        ArgumentNullException.ThrowIfNull(storageManager);
        ArgumentNullException.ThrowIfNull(searchIndex);

        var manifest = storageManager.GetArchiveManifest(archiveId)
            ?? throw new InvalidOperationException($"Yeniden indekslenecek arşiv bulunamadı: {archiveId}");

        if (!string.Equals(manifest.CompanyId, clientContext.CompanyId, StringComparison.Ordinal) ||
            !string.Equals(manifest.ProjectId, clientContext.ProjectId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Müşteri veya proje kapsamı bu arşiv ile uyuşmuyor.");
        }

        string jobId;
        LocalJobRecord record;
        ArchiveIngestWorker worker;

        lock (_jobLock)
        {
            if (!string.IsNullOrEmpty(idempotencyKey) && _idempotencyIndex.TryGetValue(idempotencyKey, out string? existingJobId))
            {
                if (_jobs.TryGetValue(existingJobId, out var existingRecord))
                {
                    if (string.Equals(existingRecord.JobKind, "archive-reindex", StringComparison.Ordinal) &&
                        string.Equals(existingRecord.ArchiveId, archiveId, StringComparison.Ordinal))
                    {
                        return ReturnExistingAuthorized(existingRecord);
                    }
                    throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı bir işlem için kullanılmıştır.");
                }
            }

            if (!string.IsNullOrEmpty(_activeRunningJobId) && (!enqueueIfBusy || !runInBackground))
            {
                throw new InvalidOperationException($"Halen devam eden etkin bir yerel işlem bulunmaktadır (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
            }
            if (_activeRunningJobId != null && enqueueIfBusy && _pendingJobs.Count >= PendingJobLimit)
                throw new InvalidOperationException($"Bekleyen iş kuyruğu dolu ({PendingJobLimit}).");

            jobId = "job-" + Guid.NewGuid().ToString("N")[..12];

            record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "archive-reindex",
                ArchiveId = archiveId,
                ArchiveName = manifest.ArchiveName,
                IdempotencyKey = idempotencyKey,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SourceFileName = manifest.ArchiveName,
                TargetFileName = manifest.ArchiveName,
                Status = "queued",
                Stage = "Sırada",
                TotalItems = manifest.TotalItems,
                TotalSourceMessages = manifest.TotalItems,
                SelectedMessagesCount = manifest.TotalItems,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var internalStateRecord = new InternalJobState
            {
                JobId = jobId,
                JobKind = "archive-reindex",
                SourcePath = archiveId,
                TargetPath = archiveId,
                ClientContext = record.ClientContext
            };

            PrepareSchedulingLocked(record, enqueueIfBusy);
            try
            {
                SaveJobRecordLocked(CloneJobRecord(record));
            }
            catch
            {
                string filePath = Path.Combine(_jobsDir, $"{jobId}.json");
                try { File.Delete(filePath); } catch { }
                throw;
            }

            _jobs[jobId] = CloneJobRecord(record);
            _internalJobStates[jobId] = internalStateRecord;
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _idempotencyIndex[idempotencyKey] = jobId;
            }

            worker = new ArchiveIngestWorker(
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                this,
                r => SaveJobRecord(r));

            if (runInBackground)
                ScheduleLocked(record, enqueueIfBusy, async () =>
                {
                    try { await worker.ExecuteReindexAsync(record, archiveId, CancellationToken.None); }
                    catch { }
                });
            else
                _activeRunningJobId = jobId;
        }

        if (!runInBackground)
        {
            try
            {
                EnsureSynchronousDispatch(record);
                worker.ExecuteReindexAsync(record, archiveId, CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                CompleteAndDispatch(jobId);
            }
        }

        lock (_jobLock)
        {
            return CloneJobRecord(_jobs.TryGetValue(jobId, out var current) ? current : record);
        }
    }
}
