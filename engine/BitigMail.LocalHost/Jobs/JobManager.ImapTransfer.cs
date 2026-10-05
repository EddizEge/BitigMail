using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;

namespace BitigMail.LocalHost.Jobs;

public partial class JobManager
{
    public LocalJobRecord StartImapTransferJob(
        ImapTransferPlan plan,
        string? idempotencyKey,
        ClientProjectContext clientContext,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        ImapTransferJournal transferJournal,
        BitigMail.LocalHost.Security.IImapCredentialResolver? credentialResolver = null,
        bool enqueueIfBusy = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clientContext);
        ArgumentNullException.ThrowIfNull(accountStore);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(transferJournal);

        plan = transferJournal.GetPlan(plan.PlanId) ?? throw new InvalidOperationException("Kalıcı aktarım planı bulunamadı.");
        if (plan.CompanyId != clientContext.CompanyId || plan.ProjectId != clientContext.ProjectId)
            throw new InvalidOperationException("Aktarım planı müşteri veya proje kapsamı ile uyuşmuyor.");

        // Blocked preview cannot start server-side: enforce authoritative CanTransfer/blocker state
        if (!plan.CanTransfer || plan.Items.Count == 0)
        {
            throw new InvalidOperationException(plan.BlockerReason ?? "Aktarım planında engel bulunmaktadır. Aktarım başlatılamaz.");
        }

        lock (_jobLock)
        {
            // 1. Idempotency check: same key + identical plan returns same job; different plan throws typed 409
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

                    if (string.Equals(existingRecord.JobKind, "imap-transfer", StringComparison.Ordinal))
                    {
                        bool isIdentical =
                            string.Equals(existingPlanId, plan.PlanId, StringComparison.Ordinal) &&
                            string.Equals(existingRecord.ClientContext.CompanyId, clientContext.CompanyId, StringComparison.Ordinal) &&
                            string.Equals(existingRecord.ClientContext.ProjectId, clientContext.ProjectId, StringComparison.Ordinal);

                        if (isIdentical)
                        {
                            return ReturnExistingAuthorized(existingRecord);
                        }

                        throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı bir IMAP aktarım planı veya kapsamı ile kullanılmıştır.");
                    }

                    throw new InvalidOperationException($"Idempotency key '{idempotencyKey}' daha önce farklı bir işlem için kullanılmıştır.");
                }
            }

            // 2. Concurrency limit: shared single active slot with PST jobs
            if (!string.IsNullOrEmpty(_activeRunningJobId) && !enqueueIfBusy)
            {
                throw new InvalidOperationException($"Halen devam eden etkin bir yerel işlem bulunmaktadır (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
            }
            if (_activeRunningJobId != null && enqueueIfBusy && _pendingJobs.Count >= PendingJobLimit)
                throw new InvalidOperationException($"Bekleyen iş kuyruğu dolu ({PendingJobLimit}).");

            string jobId = "job-" + Guid.NewGuid().ToString("N")[..12];

            var record = new LocalJobRecord
            {
                JobId = jobId,
                JobKind = "imap-transfer",
                AdvancedFilterCanonicalJson = plan.AdvancedFilterCanonicalJson,
                AdvancedFilterFingerprint = plan.AdvancedFilterFingerprint,
                AdvancedFilterUnknownCount = plan.AdvancedFilterUnknownCount,
                PlanId = plan.PlanId,
                IdempotencyKey = idempotencyKey,
                ClientContext = new ClientProjectContext
                {
                    CompanyId = clientContext.CompanyId,
                    CompanyName = clientContext.CompanyName,
                    ProjectId = clientContext.ProjectId,
                    ProjectName = clientContext.ProjectName
                },
                SourceFileName = plan.SourceDisplayName ?? plan.SourceAccountId,
                TargetFileName = plan.TargetDisplayName ?? plan.TargetAccountId,
                Status = "queued",
                Stage = "Sırada",
                TotalItems = plan.Items.Count,
                TotalSourceMessages = plan.Preview?.TotalSourceItems ?? plan.Items.Count,
                ExcludedMessagesCount = plan.Preview?.ExcludedCount ?? 0,
                MissingDateExcludedCount = plan.Preview?.MissingDateExcludedCount ?? 0,
                IsFiltered = !string.IsNullOrEmpty(plan.StartDate) || !string.IsNullOrEmpty(plan.EndDate),
                SelectedMessagesCount = plan.Items.Count,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var internalStateRecord = new InternalJobState
            {
                JobId = jobId,
                JobKind = "imap-transfer",
                PlanId = plan.PlanId,
                SourcePath = plan.SourceAccountId,
                TargetPath = plan.TargetAccountId,
                ClientContext = record.ClientContext
            };

            // Critical: Initial job/idempotency/internal dictionaries must not be published before first durable job record.
            // Persistence failure rolls back/no phantom job or idempotency and performs no APPEND.
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

            ScheduleLocked(record, enqueueIfBusy, async () =>
            {
                try
                {
                    var worker = new ImapTransferWorker(
                        accountStore,
                        clientFactory,
                        transferJournal,
                        SaveJobRecord,
                        SaveReport,
                        credentialResolver);

                    await worker.ExecuteAsync(record, plan, CancellationToken.None);
                }
                catch (Exception)
                {
                    lock (_jobLock)
                    {
                        record.Status = "failed";
                        record.Stage = "Hata";
                        record.ErrorMessage = "İşlem yürütülürken bir hata oluştu.";
                        record.CompletedAt = DateTimeOffset.UtcNow;
                        try
                        {
                            SaveJobRecordLocked(CloneJobRecord(record));
                        }
                        catch { }
                        _jobs[jobId] = CloneJobRecord(record);
                    }
                }
            });

            return CloneJobRecord(record);
        }
    }

    public LocalJobRecord ResumeImapTransferJob(
        string jobId,
        ClientProjectContext clientContext,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        ImapTransferJournal transferJournal,
        BitigMail.LocalHost.Security.IImapCredentialResolver? credentialResolver = null,
        bool enqueueIfBusy = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(clientContext);

        lock (_jobLock)
        {
            if (!_jobs.TryGetValue(jobId, out var record))
            {
                throw new KeyNotFoundException($"İş bulunamadı: {jobId}");
            }

            if (!string.Equals(record.JobKind, "imap-transfer", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"'{jobId}' numaralı iş bir IMAP aktarım işi değildir.");
            }

            if (!string.Equals(record.ClientContext.CompanyId, clientContext.CompanyId, StringComparison.Ordinal) ||
                !string.Equals(record.ClientContext.ProjectId, clientContext.ProjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Müşteri veya proje kapsamı bu iş ile uyuşmuyor.");
            }

            if (record.Status == "completed")
            {
                throw new InvalidOperationException("Tamamlanmış bir aktarım işi tekrar başlatılamaz.");
            }

            if (IsActiveOrPendingLocked(jobId)) return ReturnExistingAuthorized(record);

            if (!string.IsNullOrEmpty(_activeRunningJobId) && !enqueueIfBusy)
            {
                throw new InvalidOperationException($"Halen devam eden etkin bir yerel işlem bulunmaktadır (İş No: {_activeRunningJobId}). Aynı anda yalnızca tek bir yerel işlem yürütülebilir.");
            }
            if (_activeRunningJobId != null && enqueueIfBusy && _pendingJobs.Count >= PendingJobLimit)
                throw new InvalidOperationException($"Bekleyen iş kuyruğu dolu ({PendingJobLimit}).");

            string? planId = record.PlanId;
            if (string.IsNullOrEmpty(planId) && _internalJobStates.TryGetValue(jobId, out var internalState))
            {
                planId = internalState.PlanId;
            }

            var journal = transferJournal.GetJournal(jobId);
            if (record.NeverStartedQueued)
                throw new InvalidOperationException("Hiç başlatılmamış bekleyen iş devam ettirilemez; yeniden planlama gereklidir.");
            if (journal == null)
            {
                throw new InvalidOperationException("Aktarım günlüğü bulunamadı veya hasarlı. Kesintiye uğrayan iş devam ettirilemez.");
            }

            if (string.IsNullOrEmpty(planId))
            {
                planId = journal.PlanId;
            }

            if (string.IsNullOrEmpty(planId))
            {
                throw new InvalidOperationException("İş ile ilişkili aktarım planı bulunamadı.");
            }

            var plan = transferJournal.GetPlan(planId);
            if (plan == null)
            {
                throw new InvalidOperationException($"Aktarım planı ('{planId}') diskte bulunamadı.");
            }

            if (!plan.CanTransfer || plan.Items.Count == 0)
            {
                throw new InvalidOperationException(plan.BlockerReason ?? "Aktarım planında engel bulunmaktadır. Yeniden başlatılamaz.");
            }

            record = CloneJobRecord(record);
            record.Status = "queued";
            record.Stage = "Yeniden Başlatılıyor";
            record.ErrorMessage = null;
            PrepareSchedulingLocked(record, enqueueIfBusy);
            SaveJobRecordLocked(CloneJobRecord(record));
            _jobs[jobId] = CloneJobRecord(record);

            ScheduleLocked(record, enqueueIfBusy, async () =>
            {
                try
                {
                    var worker = new ImapTransferWorker(
                        accountStore,
                        clientFactory,
                        transferJournal,
                        SaveJobRecord,
                        SaveReport,
                        credentialResolver);

                    await worker.ExecuteAsync(record, plan, CancellationToken.None);
                }
                catch (Exception)
                {
                    lock (_jobLock)
                    {
                        record.Status = "failed";
                        record.Stage = "Hata";
                        record.ErrorMessage = "İşlem yürütülürken bir hata oluştu.";
                        record.CompletedAt = DateTimeOffset.UtcNow;
                        try
                        {
                            SaveJobRecordLocked(CloneJobRecord(record));
                        }
                        catch { }
                        _jobs[jobId] = CloneJobRecord(record);
                    }
                }
            });

            return CloneJobRecord(record);
        }
    }

    public void SaveJobRecord(LocalJobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        OnBeforeSaveJobRecord?.Invoke(record);

        var cloned = CloneJobRecord(record);

        lock (_persistLock)
        {
            string filePath = Path.Combine(_jobsDir, $"{record.JobId}.json");
            string json = JsonSerializer.Serialize(cloned, JsonOptions);
            JobSnapshotPersistence.Write(filePath, json);
        }

        lock (_jobLock)
        {
            _jobs[record.JobId] = cloned;
        }
    }

    public void SaveReport(ConversionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        OnBeforeSaveReport?.Invoke(report);

        lock (_persistLock)
        {
            string filePath = Path.Combine(_reportsDir, $"{report.JobId}.json");
            string json = JsonSerializer.Serialize(report, JsonOptions);
            JobSnapshotPersistence.Write(filePath, json);
        }

        _reports[report.JobId] = report;
    }
}
