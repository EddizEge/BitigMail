using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using BitigMail.Engine.Execution;
using BitigMail.Engine.Models;
using BitigMail.Engine.Recovery;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost.Recovery;
public sealed record RecoveryJobView(string JobId, string SourceFileName, string Status, string Stage,
    string Outcome, int? OriginalTotal, int RecoveredCount, int FailedBoundaryCount,
    bool? Partial, bool ReportAvailable, string? Error);

public sealed class DamagedStoreRecoveryService
{
    private readonly OwnedWorkerProcessRunner _runner = new();
    private readonly JobManager _manager;
    private readonly RecoverySdkBootstrap _bootstrap;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();
    public DamagedStoreRecoveryService(JobManager manager, RecoverySdkBootstrap bootstrap)
        { _manager = manager; _bootstrap = bootstrap; }

    public RecoveryJobView Start(string sourcePath, string outputDirectory, string expectedHash,
        ClientProjectContext owner, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(1)) throw new InvalidOperationException("Kurtarma süresi geçersiz.");
        string source = Path.GetFullPath(sourcePath), target = Path.GetFullPath(outputDirectory);
        RejectLinks(source); RejectLinks(target);
        if (!File.Exists(source) || !Directory.Exists(target) ||
            Path.GetExtension(source).ToLowerInvariant() is not (".pst" or ".ost"))
            throw new InvalidOperationException("PST/OST kaynağı veya hedef bulunamadı.");
        string current = DamagedStoreResultValidator.HashFile(source);
        if (!string.Equals(current, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kaynak önizlemeden sonra değişti.");
        string? capacityError = DiskCapacityPlanning.CapacityBlocker(
            checked(new FileInfo(source).Length * 3 + 256L * 1024 * 1024), new WindowsDiskCapacityProbe().Probe(target));
        if (capacityError is not null) throw new InvalidOperationException(capacityError);
        var record = _manager.ScheduleRecovery(Path.GetFileName(source), current, owner,
            r => RunAsync(r, source, target, current, timeout));
        return Get(record.JobId)!;
    }

    public RecoveryJobView? Get(string id)
    {
        var record = _manager.GetJob(id);
        if (record is null || record.JobKind != "damaged-recovery") return null;
        return new(record.JobId, record.SourceFileName, record.Status == "converting" ? "running" : record.Status,
            record.Stage, record.RecoveryOutcome ?? "pending", record.RecoveryOriginalTotal,
            record.ItemsWritten, record.RecoveryFailedBoundaryCount, record.QualificationIsPartial,
            record.OutputDirectoryPath is not null && File.Exists(Path.Combine(record.OutputDirectoryPath, "bitigmail-recovery-manifest.json")),
            record.ErrorMessage);
    }

    public bool Cancel(string id)
    {
        if (_cancellations.TryGetValue(id, out var cancellation))
        {
            try { cancellation.Cancel(); return true; } catch (ObjectDisposedException) { return false; }
        }
        var record = _manager.GetJob(id);
        if (record?.JobKind != "damaged-recovery" || record.Status != "queued") return false;
        try { _manager.CancelPendingJob(id, record.ClientContext.CompanyId, record.ClientContext.ProjectId); return true; }
        catch (InvalidOperationException) { return false; }
    }

    public byte[]? GetReportBytes(string id)
    {
        var record = _manager.GetJob(id);
        if (record?.JobKind != "damaged-recovery" || record.OutputDirectoryPath is null || record.RecoveryReportSha256 is null) return null;
        string path = Path.Combine(record.OutputDirectoryPath, "bitigmail-recovery-manifest.json");
        RejectLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > DamagedStoreResultValidator.MaxManifestBytes) throw new InvalidDataException("Kurtarma raporu boyutu geçersiz.");
        byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(record.RecoveryReportSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Kurtarma raporu değişmiş; rapor sunulmadı.");
        return bytes;
    }

    private async Task RunAsync(LocalJobRecord record, string source, string selectedOutput, string frozenHash, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource();
        _cancellations[record.JobId] = cancellation;
        string invocation = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(selectedOutput, $".bitigmail-staging-{record.JobId}-{invocation}");
        string published = Path.Combine(selectedOutput, record.JobId);
        byte[] bootstrap = [];
        bool launchAttempted = false, terminationConfirmed = false;
        try
        {
            RejectLinks(source); RejectLinks(selectedOutput);
            if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException("Çalışma alanı zaten mevcut.");
            Directory.CreateDirectory(staging); RejectLinks(staging);
            if (!string.Equals(DamagedStoreResultValidator.HashFile(source, cancellation.Token), frozenHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Kaynak dispatch öncesinde değişti.");
            record.Status = "converting"; record.Stage = "Salt okunur tarama çalışıyor";
            record.StartedAt ??= DateTimeOffset.UtcNow; _manager.UpdateRecovery(record);
            var request = new DamagedStoreWorkerRequest(1, invocation, record.JobId, source, frozenHash,
                staging, 100_000, 250_000, 128, DateTime.UtcNow.Add(timeout).Ticks);
            string requestPath = Path.Combine(staging, "request.json"), resultPath = Path.Combine(staging, "bitigmail-recovery-manifest.json");
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request));
            string executable = ResolveWorker(); bootstrap = _bootstrap.Copy();
            cancellation.Token.ThrowIfCancellationRequested();
            launchAttempted = true;
            var process = await _runner.RunAsync(executable, [requestPath, resultPath], staging, timeout, cancellation.Token, bootstrap);
            terminationConfirmed = process.TerminationConfirmed;
            if (!terminationConfirmed) { _manager.LatchUnresolvedWorker(record); return; }
            if (process.Outcome != "completed")
            {
                FinishFailure(record, process.Outcome, process.Outcome == "cancelled" ? "cancelled" : "failed",
                    "Kurtarma işçisi tamamlanmadı; geçici çıktı yayımlanmadı."); return;
            }
            File.Delete(requestPath);
            var result = new DamagedStoreResultValidator().Validate(request, resultPath, source, cancellation.Token);
            record.RecoveryReportSha256 = DamagedStoreResultValidator.HashFile(resultPath, cancellation.Token);
            RejectLinks(selectedOutput); RejectLinks(staging);
            if (Directory.Exists(published) || File.Exists(published)) throw new IOException("Hedef iş klasörü zaten var.");
            Directory.Move(staging, published);
            record.Status = result.Outcome is "healthy_extraction" or "partial_recovered" ? "completed" : "failed";
            record.Stage = result.Partial ? "Kısmi ve doğrulanmış çıktı" : "Kurtarma sonucu doğrulandı";
            record.RecoveryOutcome = result.Outcome; record.RecoveryOriginalTotal = result.OriginalTotal;
            record.RecoveryFailedBoundaryCount = result.FailedBoundaryCount;
            record.ExcludedMessagesCount = result.NonMailItemCount;
            record.ItemsRead = result.RecoveredCount; record.ItemsWritten = result.RecoveredCount;
            record.TotalItems = result.OriginalTotal ?? 0; record.PhaseTotal = result.OriginalTotal;
            record.QualificationIsPartial = result.Partial;
            record.QualificationWarnings = ["Kurtarma çıktısı kaynak dosyanın tüm bölgelerinin sağlam olduğunu kanıtlamaz.", "Lisanslı çıktı ve temsilî hasarlı dosya kabulü bekleniyor."];
            record.OutputDirectoryPath = published; record.CompletedAt = DateTimeOffset.UtcNow;
            _manager.UpdateRecovery(record);
        }
        catch (OperationCanceledException)
        {
            if (launchAttempted && !terminationConfirmed) _manager.LatchUnresolvedWorker(record);
            else FinishFailure(record, "cancelled", "cancelled", "İş iptal edildi; geçici çıktı korunuyor.");
        }
        catch (Exception)
        {
            if (launchAttempted && !terminationConfirmed) _manager.LatchUnresolvedWorker(record);
            else FinishFailure(record, "failed", "failed", "Kurtarma çıktısı doğrulanamadı; geçici çıktı korunuyor.");
        }
        finally
        {
            if (bootstrap.Length > 0) CryptographicOperations.ZeroMemory(bootstrap);
            _cancellations.TryRemove(record.JobId, out _);
            // Failed and uncertain staging is intentionally retained. No recursive cleanup.
        }
    }

    private void FinishFailure(LocalJobRecord record, string outcome, string status, string error)
    {
        record.Status = status; record.RecoveryOutcome = outcome; record.Stage = status == "cancelled" ? "İptal edildi" : "Kurtarma tamamlanmadı";
        record.ErrorMessage = error; record.CompletedAt = DateTimeOffset.UtcNow; _manager.UpdateRecovery(record);
    }

    private static void RejectLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Bağlantılı kurtarma yolu kabul edilmez.");
    }

    private static string ResolveWorker()
    {
        string packaged = Path.Combine(AppContext.BaseDirectory, "recovery", "BitigMail.RecoveryWorker.exe");
        if (File.Exists(packaged)) return packaged;
        string root = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6 && !Directory.Exists(Path.Combine(root, "engine")); i++) root = Directory.GetParent(root)?.FullName ?? root;
        foreach (string configuration in new[] { "Release", "Debug" })
        {
            string path = Path.Combine(root, "engine", "BitigMail.RecoveryWorker", "bin", configuration, "net8.0-windows", "BitigMail.RecoveryWorker.exe");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Paketlenmiş kurtarma işçisi bulunamadı.");
    }
}
