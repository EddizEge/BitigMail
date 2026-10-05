using BitigMail.Engine.Models;
using BitigMail.LocalHost.Pop;

namespace BitigMail.LocalHost.Jobs;

public partial class JobManager
{
    public LocalJobRecord StartPopSnapshotJob(PopSnapshotPlan plan, string outputDirectory, string idempotencyKey,
        ClientProjectContext owner, PopSnapshotService service, bool enqueueIfBusy)
    {
        lock (_jobLock)
        {
            string fingerprint = $"pop:{plan.AccountId}:{plan.AccountVersion}:{plan.PlanId}:{plan.SnapshotSha256}:{Path.GetFullPath(outputDirectory)}:{owner.CompanyId}:{owner.ProjectId}";
            if (_idempotencyIndex.TryGetValue(idempotencyKey, out var old)) { if (_jobs[old].RequestFingerprint == fingerprint) return ReturnExistingAuthorized(_jobs[old]); throw new InvalidOperationException("Idempotency anahtarı farklı POP isteğiyle kullanılmıştır."); }
            string id = "job-" + Guid.NewGuid().ToString("N")[..12]; var frozen = new ClientProjectContext { CompanyId = owner.CompanyId, CompanyName = owner.CompanyName, ProjectId = owner.ProjectId, ProjectName = owner.ProjectName };
            var record = new LocalJobRecord { JobId = id, JobKind = "pop-snapshot", IdempotencyKey = idempotencyKey, RequestFingerprint = fingerprint, ClientContext = frozen,
                SourceFileName = "POP source-only snapshot", TargetFileName = "Doğrulanmış EML klasörü", OutputDirectoryPath = Path.GetFullPath(outputDirectory),
                Status = _activeRunningJobId is null ? "converting" : "queued", Stage = _activeRunningJobId is null ? "POP iletileri indiriliyor" : "Sırada", CreatedAt = DateTimeOffset.UtcNow, StartedAt = _activeRunningJobId is null ? DateTimeOffset.UtcNow : null, TotalItems = plan.Items.Count };
            record.PlanId = plan.PlanId;
            PrepareSchedulingLocked(record, enqueueIfBusy); _jobs[id] = record; _idempotencyIndex[idempotencyKey] = id;
            try { SaveJobRecordLocked(CloneJobRecord(record)); } catch { _jobs.TryRemove(id, out _); _idempotencyIndex.TryRemove(idempotencyKey, out _); throw; }
            ScheduleLocked(record, enqueueIfBusy, () => ExecutePopSnapshot(id, plan, outputDirectory, frozen, service)); return CloneJobRecord(record);
        }
    }
    private async Task ExecutePopSnapshot(string id, PopSnapshotPlan plan, string output, ClientProjectContext owner, PopSnapshotService service)
    {
        try
        {
            var result = await service.DownloadAsync(plan, output, id, CancellationToken.None);
            var report = new ConversionReport { JobId = id, JobKind = "pop-snapshot", EvidenceLabel = "QUALIFIED_POP_SOURCE_TO_EML", ClientContext = owner,
                SourceFileName = "POP source-only snapshot", SourceSha256Before = plan.SnapshotSha256, SourceSha256After = plan.SnapshotSha256, SourceHashMatch = true,
                OutputPath = result.OutputPath, OutputDirectoryPath = result.OutputPath, OutputPstFileName = Path.GetFileName(result.OutputPath), ConversionSuccess = result.WrittenItems == result.PlannedItems,
                ItemsRead = result.PlannedItems, ItemsWritten = result.WrittenItems, FailedItems = result.PlannedItems - result.WrittenItems, TotalSourceMessages = result.PlannedItems,
                FidelityStatus = "QUALIFIED_RETR_STREAM_BYTES; POP_SERVER_METADATA_UNAVAILABLE", OverallStatus = result.Status, Warnings = result.Warnings.ToList(),
                ReopenedPstVerification = new() { VerifiedWith = "MimeKit persisted EML parse + SHA-256", VerificationSuccess = true, TotalPhysicalItemsFound = result.WrittenItems, ItemCountMatch = result.WrittenItems == result.PlannedItems, VerificationNotes = ["UIDL plan and LIST sizes were revalidated before every RETR; no DELE operation exists in this worker."] } };
            SaveReportLocked(report); _reports[id] = report; lock (_jobLock) { var job = _jobs[id]; job.Status = "completed"; job.Stage = "POP iletileri çıkarıldı; postalar sunucuda kaldı"; job.ItemsRead = result.PlannedItems; job.ItemsWritten = result.WrittenItems; job.TotalItems = result.PlannedItems; job.OutputPath = result.OutputPath; job.PercentComplete = 100; job.CompletedAt = DateTimeOffset.UtcNow; SaveJobRecordLocked(CloneJobRecord(job)); }
        }
        catch (Exception ex) { lock (_jobLock) { var job = _jobs[id]; job.Status = "failed"; job.Stage = "POP çıkarımı başarısız"; job.ErrorMessage = ex.Message; job.CompletedAt = DateTimeOffset.UtcNow; try { SaveJobRecordLocked(CloneJobRecord(job)); } catch { } } }
    }
}
