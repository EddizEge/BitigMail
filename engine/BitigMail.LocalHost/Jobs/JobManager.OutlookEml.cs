using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;

namespace BitigMail.LocalHost.Jobs;

public partial class JobManager
{
    internal Func<OutlookToEmlNormalizationService> OutlookEmlServiceFactory { get; set; } = () => new OutlookToEmlNormalizationService();
    public LocalJobRecord StartOutlookEmlJob(string source, string output, string expectedHash, string? idempotencyKey, ClientProjectContext owner, bool enqueueIfBusy)
    {
        string actual; using (var fs = File.OpenRead(source)) actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)).ToLowerInvariant();
        if (actual != expectedHash) throw new InvalidDataException("Kaynak önizlemeden sonra değişti.");
        string expectedFormat = Path.GetExtension(source).TrimStart('.').ToLowerInvariant();
        lock (_jobLock)
        {
            if (_activeRunningJobId is not null && !enqueueIfBusy) throw new InvalidOperationException("Halen devam eden etkin bir yerel işlem bulunmaktadır.");
            string fingerprint = $"outlook-eml:{actual}:{Path.GetFullPath(output)}:{owner.CompanyId}:{owner.ProjectId}";
            if (!string.IsNullOrEmpty(idempotencyKey) && _idempotencyIndex.TryGetValue(idempotencyKey, out var old)) { if (_jobs[old].RequestFingerprint == fingerprint) return ReturnExistingAuthorized(_jobs[old]); throw new InvalidOperationException("Idempotency anahtarı farklı parametrelerle kullanılmıştır."); }
            string id = "job-" + Guid.NewGuid().ToString("N")[..12]; var frozen = new ClientProjectContext { CompanyId = owner.CompanyId, CompanyName = owner.CompanyName, ProjectId = owner.ProjectId, ProjectName = owner.ProjectName };
            var record = new LocalJobRecord { JobId = id, JobKind = "outlook-eml-normalize", IdempotencyKey = idempotencyKey, RequestFingerprint = fingerprint, ClientContext = frozen, SourceFileName = Path.GetFileName(source), TargetFileName = "Doğrulanmış EML klasörleri", OutputDirectoryPath = Path.GetFullPath(output), Status = _activeRunningJobId is null ? "converting" : "queued", Stage = _activeRunningJobId is null ? "İletiler çıkarılıyor" : "Sırada", CreatedAt = DateTimeOffset.UtcNow, StartedAt = _activeRunningJobId is null ? DateTimeOffset.UtcNow : null };
            PrepareSchedulingLocked(record, enqueueIfBusy); _jobs[id] = record; if (!string.IsNullOrEmpty(idempotencyKey)) _idempotencyIndex[idempotencyKey] = id;
            try { SaveJobRecordLocked(CloneJobRecord(record)); }
            catch { _jobs.TryRemove(id, out _); if (!string.IsNullOrEmpty(idempotencyKey)) _idempotencyIndex.TryRemove(idempotencyKey, out _); throw; }
            ScheduleLocked(record, enqueueIfBusy, () => Task.Run(() => ExecuteOutlookEml(id, source, output, frozen, expectedHash, expectedFormat))); return CloneJobRecord(record);
        }
    }
    private void ExecuteOutlookEml(string id, string source, string output, ClientProjectContext owner, string expectedHash, string expectedFormat)
    {
        try
        {
            var r = OutlookEmlServiceFactory().Normalize(source, output, id, _capacityProbe, expectedSourceSha256: expectedHash, expectedFormat: expectedFormat);
            bool rawOlmVerified = r.SourceFormat == "olm"; int exactAttachments = r.Items.Sum(x => x.Fidelity?.ExactAttachments ?? 0);
            var report = new ConversionReport { JobId = id, JobKind = "outlook-eml-normalize", EvidenceLabel = "QUALIFIED_OUTLOOK_TO_EML", ClientContext = owner, SourceFileName = Path.GetFileName(source), SourceSizeBytes = new FileInfo(source).Length, SourceSha256Before = r.SourceSha256Before, SourceSha256After = r.SourceSha256After, SourceHashMatch = r.SourceSha256Before == r.SourceSha256After, OutputPath = r.OutputPath, OutputDirectoryPath = r.OutputPath, OutputPstFileName = Path.GetFileName(r.OutputPath), ConversionSuccess = r.FailedItems == 0 && r.MailItems > 0, ItemsRead = r.MailItems + r.FailedItems + r.ExcludedNonMailItems, ItemsWritten = r.MailItems, FailedItems = r.FailedItems, TotalSourceMessages = r.MailItems + r.FailedItems, FidelityStatus = rawOlmVerified ? "QUALIFIED_DATE_FIDELITY_UNRESOLVED" : "QUALIFIED_GENERIC_FIELDS_AND_ATTACHMENTS_COMPARED", OverallStatus = r.Status, Warnings = r.Warnings.Concat(r.Items.SelectMany(x => x.Fidelity?.QualifiedDifferences ?? [])).Distinct(StringComparer.Ordinal).ToList(), Errors = r.Failures.Select(x => $"{x.FolderPath} | {x.SourceIdentity} | {x.ErrorType}: {x.ErrorMessage}").ToList(), ReopenedPstVerification = new() { VerifiedWith = "MimeKit persisted EML parse + attachment SHA-256", VerificationSuccess = r.MailItems > 0, TotalPhysicalItemsFound = r.MailItems, ItemCountMatch = r.FailedItems == 0, TotalAttachmentsVerified = exactAttachments, VerificationNotes = rawOlmVerified ? ["OLM original attachment bytes and metadata verified after persisted EML parse; date semantic fidelity remains unresolved."] : ["PST/OST generic subject, message-id, addresses, body/date representation and attachment payloads compared per emitted item; qualified differences are reported."] } };
            SaveReportLocked(report); _reports[id] = report; lock (_jobLock)
            {
                var job = _jobs[id];
                job.Status = report.ConversionSuccess ? "completed" : r.MailItems > 0 ? "partially_completed" : "failed";
                job.Stage = report.ConversionSuccess ? "İletiler çıkarıldı; bilinen farklar raporda" : r.MailItems > 0 ? "Bazı iletiler çıkarılamadı; raporu inceleyin" : "Çıkarılabilecek e-posta bulunamadı";
                job.ErrorMessage = report.ConversionSuccess ? null : r.MailItems > 0 ? $"{r.FailedItems} ileti çıkarılamadı; doğrulanmış çıktılar yayımlandı." : "Kaynakta çıkarılabilecek e-posta bulunamadı. Raporu inceleyin.";
                job.ItemsRead = report.ItemsRead; job.ItemsWritten = report.ItemsWritten; job.TotalItems = report.ItemsRead;
                job.FailedItems = report.FailedItems;
                job.OutputPath = r.OutputPath; job.PercentComplete = 100; job.CompletedAt = DateTimeOffset.UtcNow;
                SaveJobRecordLocked(CloneJobRecord(job));
            }
        }
        catch (Exception ex) { lock (_jobLock) { var job = _jobs[id]; job.Status = "failed"; job.Stage = "Çıkarım başarısız"; job.ErrorMessage = ex.Message; job.FailedItems = Math.Max(1, job.TotalItems - job.ItemsWritten); job.CompletedAt = DateTimeOffset.UtcNow; try { SaveJobRecordLocked(CloneJobRecord(job)); } catch { } } }
    }
}
