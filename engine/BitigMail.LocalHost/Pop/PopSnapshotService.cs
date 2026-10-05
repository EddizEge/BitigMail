using System.Security.Cryptography;
using System.Text.Json;
using BitigMail.Engine.Storage;
using MailKit.Net.Pop3;
using MailKit.Security;
using MimeKit;

namespace BitigMail.LocalHost.Pop;

public interface IPopSnapshotClient : IAsyncDisposable
{
    Task ConnectAsync(PopAccountPublicDto account, string password, CancellationToken ct);
    Task<IReadOnlyList<string>> GetUidsAsync(CancellationToken ct);
    int MessageCount { get; }
    Task<IReadOnlyList<long>> GetSizesAsync(CancellationToken ct);
    Task<Stream> RetrieveAsync(int zeroBasedIndex, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
}
public interface IPopSnapshotClientFactory { IPopSnapshotClient Create(); }

public sealed class MailKitPopSnapshotClientFactory : IPopSnapshotClientFactory { public IPopSnapshotClient Create() => new MailKitPopSnapshotClient(); }
internal sealed class MailKitPopSnapshotClient : IPopSnapshotClient
{
    private readonly Pop3Client _client = new();
    public int MessageCount => _client.Count;
    public async Task ConnectAsync(PopAccountPublicDto account, string password, CancellationToken ct)
    {
        SecureSocketOptions options = account.TlsMode switch { "ssl" => SecureSocketOptions.SslOnConnect, "starttls" => SecureSocketOptions.StartTls, "none" => SecureSocketOptions.None, _ => throw new InvalidOperationException("Geçersiz POP TLS modu.") };
        await _client.ConnectAsync(account.Host, account.Port, options, ct); await _client.AuthenticateAsync(account.Username, password, ct);
    }
    public async Task<IReadOnlyList<string>> GetUidsAsync(CancellationToken ct) => (await _client.GetMessageUidsAsync(ct)).ToArray();
    public async Task<IReadOnlyList<long>> GetSizesAsync(CancellationToken ct) => (await _client.GetMessageSizesAsync(ct)).Select(x => (long)x).ToArray();
    public async Task<Stream> RetrieveAsync(int zeroBasedIndex, CancellationToken ct) => await _client.GetStreamAsync(zeroBasedIndex, false, ct);
    public async Task DisconnectAsync(CancellationToken ct) { if (_client.IsConnected) await _client.DisconnectAsync(true, ct); }
    public ValueTask DisposeAsync() { _client.Dispose(); return ValueTask.CompletedTask; }
}

public sealed class PopSnapshotService
{
    public const long MaxMessageBytes = 64L * 1024 * 1024;
    public const int MaxMessageCount = 100_000;
    private readonly PopAccountStore _accounts; private readonly IPopSnapshotClientFactory _clients; private readonly IDiskCapacityProbe _capacity; private readonly BitigMail.LocalHost.Security.TransientResourceOwnershipRegistry? _owners;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PopSnapshotPlan> _plans = new(StringComparer.Ordinal);
    private readonly string _planDirectory;
    internal Action<int>? AfterEmlCommitHook { get; set; }
    internal Action? BeforePublicationHook { get; set; }
    public PopSnapshotService(PopAccountStore accounts, IPopSnapshotClientFactory clients, IDiskCapacityProbe capacity,BitigMail.LocalHost.Security.TransientResourceOwnershipRegistry? owners=null) { _accounts = accounts; _clients = clients; _capacity = capacity;_owners=owners; _planDirectory = Path.Combine(accounts.StorageDirectory, "plans"); Directory.CreateDirectory(_planDirectory); LoadPlans(); }
    public async Task<PopSnapshotPlan> PreviewAsync(string accountId, string companyId, string projectId, CancellationToken ct)
    {
        var resolved = _accounts.Resolve(accountId, companyId, projectId); await using var client = _clients.Create();
        try
        {
            await client.ConnectAsync(resolved.Account, resolved.Password, ct); ValidateMessageCount(client.MessageCount); var uids = await client.GetUidsAsync(ct); var sizes = await client.GetSizesAsync(ct);
            ValidateSnapshot(uids, sizes); var items = uids.Select((uid, index) => new PopSnapshotItem(index + 1, uid, sizes[index])).ToArray();
            var plan = new PopSnapshotPlan("pop-plan-" + Guid.NewGuid().ToString("N"), accountId, resolved.Account.Version, companyId, projectId, items, SnapshotHash(items), DateTimeOffset.UtcNow);
            PersistPlan(plan); _plans[plan.PlanId] = plan;_owners?.Bind(plan.PlanId); return plan;
        }
        finally { await SafeDisconnectAsync(client); }
    }
    public PopSnapshotPlan GetPlan(string planId, string companyId, string projectId)
    {
        if (!_plans.TryGetValue(planId, out var plan)) throw new KeyNotFoundException("POP planı bulunamadı.");
        if (plan.CompanyId != companyId || plan.ProjectId != projectId) throw new InvalidOperationException("POP planı müşteri/proje kapsamıyla uyuşmuyor.");
        return plan;
    }
    public async Task<PopSnapshotReport> DownloadAsync(PopSnapshotPlan plan, string outputRoot, string jobId, CancellationToken ct)
    {
        outputRoot = Path.GetFullPath(outputRoot); var resolved = _accounts.Resolve(plan.AccountId, plan.CompanyId, plan.ProjectId);
        if (resolved.Account.Version != plan.AccountVersion) throw new InvalidOperationException("POP hesabı plan oluşturulduktan sonra değişti.");
        long required = checked(plan.Items.Sum(x => x.SizeBytes) * 2 + 64L * 1024 * 1024); string? blocker = DiskCapacityPlanning.CapacityBlocker(required, _capacity.Probe(outputRoot)); if (blocker is not null) throw new InvalidOperationException(blocker);
        RejectChain(outputRoot); string final = Path.Combine(outputRoot, "POP-snapshot-" + plan.PlanId), staging = Path.Combine(outputRoot, ".pop-" + plan.PlanId + ".partial");
        if (Directory.Exists(final)) return ValidatePublished(final, plan);
        if (File.Exists(final)) throw new IOException("POP çıktı hedefi zaten mevcut."); if (Directory.Exists(staging) || File.Exists(staging)) RejectChain(staging);
        var outputs = LoadPartial(staging, plan); Directory.CreateDirectory(staging); if (!File.Exists(Path.Combine(staging, "bitigmail-pop-partial.json"))) WritePartial(staging, plan, outputs); await using var client = _clients.Create();
        try
        {
            await client.ConnectAsync(resolved.Account, resolved.Password, ct);
            ValidateMessageCount(client.MessageCount);
            for (int index = outputs.Count; index < plan.Items.Count; index++)
            {
                ct.ThrowIfCancellationRequested(); var currentUids = await client.GetUidsAsync(ct); var currentSizes = await client.GetSizesAsync(ct); ValidateSnapshot(currentUids, currentSizes);
                if (!SnapshotMatches(plan.Items, currentUids, currentSizes)) throw new InvalidDataException("POP UIDL/LIST anlık görüntüsü değişti; yeni önizleme gerekir.");
                var planned = plan.Items[index]; if (planned.SizeBytes > MaxMessageBytes) throw new InvalidDataException("POP iletisi 64 MiB güvenlik sınırını aşıyor.");
                await using Stream remote = await client.RetrieveAsync(index, ct); using var memory = new MemoryStream(); await CopyBoundedAsync(remote, memory, MaxMessageBytes, ct); byte[] bytes = memory.ToArray();
                using (var parse = new MemoryStream(bytes, false)) _ = await MimeMessage.LoadAsync(parse, ct);
                string relative = Path.Combine("POP", $"{index + 1:D6}-{SafeUidl(planned.Uidl)}.eml"), path = ResolveUnder(staging, relative), tempPath = path + ".tmp"; Directory.CreateDirectory(Path.GetDirectoryName(path)!); RejectChain(Path.GetDirectoryName(path)!);
                await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) { await fs.WriteAsync(bytes, ct); await fs.FlushAsync(ct); fs.Flush(true); }
                string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); if (HashFile(tempPath) != hash) throw new InvalidDataException("POP EML yazım hash doğrulaması başarısız."); File.Move(tempPath, path, false);
                AfterEmlCommitHook?.Invoke(index + 1);
                using var reopened = MimeMessage.Load(path); outputs.Add(new(index + 1, planned.Uidl, relative, bytes.LongLength, hash, reopened.Date == DateTimeOffset.MinValue ? null : reopened.Date.ToString("O"), "RFC_MESSAGE_DATE_ONLY; POP_INTERNALDATE_AND_READFLAG_UNAVAILABLE; RETR_STREAM_BYTES"));
                WritePartial(staging, plan, outputs);
            }
            var finalUids = await client.GetUidsAsync(ct); var finalSizes = await client.GetSizesAsync(ct); ValidateSnapshot(finalUids, finalSizes);
            if (!SnapshotMatches(plan.Items, finalUids, finalSizes)) throw new InvalidDataException("POP UIDL/LIST anlık görüntüsü son doğrulamada değişti; yayın engellendi.");
            var report = new PopSnapshotReport("completed_with_qualification", final, plan.SnapshotSha256, plan.Items.Count, outputs.Count, outputs,
                ["POP kaynağında klasör, internal received date ve read flag bulunmaz; bu metadata üretilmedi.", "Sunucu RETR akışındaki satır normalizasyonu özgün mailbox-on-disk baytları olarak sunulmaz.", "Kaynak postalar sunucuda bırakıldı; DELE kullanılmadı."]);
            await File.WriteAllBytesAsync(Path.Combine(staging, "bitigmail-pop-snapshot-manifest.json"), JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true }), ct);
            BeforePublicationHook?.Invoke();
            Directory.Move(staging, final); try { File.Delete(Path.Combine(final, "bitigmail-pop-partial.json")); } catch { } return report;
        }
        catch { throw; }
        finally { await SafeDisconnectAsync(client); }
    }
    internal static void ValidateSnapshot(IReadOnlyList<string> uids, IReadOnlyList<long> sizes)
    {
        if (uids.Count != sizes.Count) throw new InvalidDataException("POP UIDL ve LIST sayıları uyuşmuyor.");
        if (uids.Any(string.IsNullOrWhiteSpace) || uids.Distinct(StringComparer.Ordinal).Count() != uids.Count) throw new InvalidDataException("POP UIDL değerleri eksik veya yinelenmiş.");
        if (sizes.Any(x => x < 0)) throw new InvalidDataException("POP LIST boyutu geçersiz.");
    }
    internal static void ValidateMessageCount(int count) { if (count < 0 || count > MaxMessageCount) throw new InvalidDataException("POP ileti sayısı kaynak sınırını aşıyor."); }
    internal static bool SnapshotMatches(IReadOnlyList<PopSnapshotItem> plan, IReadOnlyList<string> uids, IReadOnlyList<long> sizes) => plan.Count == uids.Count && plan.Select((x, i) => x.Uidl == uids[i] && x.SizeBytes == sizes[i]).All(x => x);
    private static string SnapshotHash(IReadOnlyList<PopSnapshotItem> items) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(items))).ToLowerInvariant();
    private static async Task CopyBoundedAsync(Stream source, Stream target, long max, CancellationToken ct) { byte[] buffer = new byte[81920]; long total = 0; int read; while ((read = await source.ReadAsync(buffer, ct)) > 0) { total += read; if (total > max) throw new InvalidDataException("POP RETR akışı 64 MiB güvenlik sınırını aşıyor."); await target.WriteAsync(buffer.AsMemory(0, read), ct); } }
    private static string SafeUidl(string uid) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uid))).ToLowerInvariant()[..16];
    private static string ResolveUnder(string root, string relative) { string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, full = Path.GetFullPath(Path.Combine(root, relative)); if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("POP çıktı yolu hedef dışına çıkıyor."); return full; }
    private static string HashFile(string path) { using var fs = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant(); }
    private void PersistPlan(PopSnapshotPlan plan) { string final = Path.Combine(_planDirectory, plan.PlanId + ".json"), temp = final + ".tmp"; File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(plan)); File.Move(temp, final, false); }
    private void LoadPlans() { foreach (string file in Directory.GetFiles(_planDirectory, "pop-plan-*.json")) { try { var plan = JsonSerializer.Deserialize<PopSnapshotPlan>(File.ReadAllBytes(file)); if (plan is not null && Path.GetFileNameWithoutExtension(file) == plan.PlanId && SnapshotHash(plan.Items) == plan.SnapshotSha256) _plans[plan.PlanId] = plan; } catch { } } }
    private static List<PopOutputItem> LoadPartial(string staging, PopSnapshotPlan plan)
    {
        if (!Directory.Exists(staging)) return [];
        RejectChain(staging); var entries = GetGuardedTreeEntries(staging); string journal = Path.Combine(staging, "bitigmail-pop-partial.json"); RejectChain(journal); if (!File.Exists(journal)) throw new InvalidDataException("POP kısmi dizininde doğrulanabilir devam günlüğü yok.");
        var items = JsonSerializer.Deserialize<List<PopOutputItem>>(File.ReadAllBytes(journal)) ?? throw new InvalidDataException("POP devam günlüğü okunamadı.");
        for (int i = 0; i < items.Count; i++) { if (i >= plan.Items.Count || items[i].Sequence != i + 1 || items[i].Uidl != plan.Items[i].Uidl || items[i].RelativePath != DeterministicRelative(plan.Items[i], i)) throw new InvalidDataException("POP devam günlüğü planla uyuşmuyor."); string path = ResolveUnder(staging, items[i].RelativePath); RejectChain(path); if (!File.Exists(path) || new FileInfo(path).Length != items[i].SizeBytes || HashFile(path) != items[i].Sha256) throw new InvalidDataException("POP devam çıktısı hash doğrulamasını geçemedi."); }
        var referenced = items.Select(x => ResolveUnder(staging, x.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase); string pendingManifest = Path.Combine(staging, "bitigmail-pop-snapshot-manifest.json");
        var deterministic = plan.Items.Select((planned, index) => ResolveUnder(staging, DeterministicRelative(planned, index))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deterministicTemps = deterministic.Select(path => path + ".tmp").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (FileSystemInfo entry in entries)
        {
            if (entry is DirectoryInfo) { if (!string.Equals(entry.FullName, Path.Combine(staging, "POP"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("POP kısmi dizininde plana ait olmayan dizin bulundu."); continue; }
            string candidate = entry.FullName;
            if (string.Equals(candidate, journal, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate, pendingManifest, StringComparison.OrdinalIgnoreCase) || referenced.Contains(candidate)) continue;
            if (deterministic.Contains(candidate) || deterministicTemps.Contains(candidate)) { File.Delete(candidate); continue; }
            throw new InvalidDataException("POP kısmi dizininde plana ait olmayan dosya bulundu.");
        }
        return items;
    }
    private static void WritePartial(string staging, PopSnapshotPlan plan, IReadOnlyList<PopOutputItem> outputs)
    { string final = Path.Combine(staging, "bitigmail-pop-partial.json"), temp = final + ".tmp"; File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(outputs)); File.Move(temp, final, true); }
    private static async Task SafeDisconnectAsync(IPopSnapshotClient client) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); try { await client.DisconnectAsync(timeout.Token); } catch { } }
    private static void RejectChain(string path) { FileSystemInfo? current = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path); while (current is not null) { if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("POP kaynak/hedef reparse zinciri kabul edilmez."); current = current is DirectoryInfo d ? d.Parent : ((FileInfo)current).Directory; } }
    private static string DeterministicRelative(PopSnapshotItem item, int index) => Path.Combine("POP", $"{index + 1:D6}-{SafeUidl(item.Uidl)}.eml");
    private static IReadOnlyList<FileSystemInfo> GetGuardedTreeEntries(string root)
    {
        RejectChain(root); var result = new List<FileSystemInfo>(); var pending = new Queue<DirectoryInfo>(); pending.Enqueue(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue(); RejectChain(directory.FullName);
            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos()) { if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("POP kaynak/hedef reparse zinciri kabul edilmez."); result.Add(entry); if (entry is DirectoryInfo child) pending.Enqueue(child); }
        }
        return result;
    }
    private static PopSnapshotReport ValidatePublished(string final, PopSnapshotPlan plan)
    {
        final = Path.GetFullPath(final); RejectChain(final); var entries = GetGuardedTreeEntries(final); string manifest = Path.Combine(final, "bitigmail-pop-snapshot-manifest.json"); RejectChain(manifest);
        if (!File.Exists(manifest)) throw new InvalidDataException("Tamamlanmış POP manifesti bulunamadı.");
        var report = JsonSerializer.Deserialize<PopSnapshotReport>(File.ReadAllBytes(manifest)) ?? throw new InvalidDataException("Tamamlanmış POP manifesti okunamadı.");
        if (report.Status != "completed_with_qualification" || !string.Equals(Path.GetFullPath(report.OutputPath), final, StringComparison.OrdinalIgnoreCase) || report.SnapshotSha256 != plan.SnapshotSha256 || report.PlannedItems != plan.Items.Count || report.WrittenItems != plan.Items.Count || report.Items.Count != plan.Items.Count) throw new InvalidDataException("Tamamlanmış POP manifesti donmuş planla uyuşmuyor.");
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifest };
        for (int i = 0; i < plan.Items.Count; i++)
        {
            var planned = plan.Items[i]; var item = report.Items[i]; string relative = DeterministicRelative(planned, i);
            if (item.Sequence != i + 1 || item.Uidl != planned.Uidl || item.RelativePath != relative) throw new InvalidDataException("Tamamlanmış POP öğesi donmuş planla uyuşmuyor.");
            string path = ResolveUnder(final, relative); RejectChain(path); if (!File.Exists(path) || new FileInfo(path).Length != item.SizeBytes || HashFile(path) != item.Sha256) throw new InvalidDataException("Tamamlanmış POP EML doğrulaması başarısız."); allowedFiles.Add(path);
        }
        foreach (FileSystemInfo entry in entries) { if (entry is DirectoryInfo) { if (!string.Equals(entry.FullName, Path.Combine(final, "POP"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Tamamlanmış POP çıktısında beklenmeyen dizin bulundu."); } else if (!allowedFiles.Contains(entry.FullName)) throw new InvalidDataException("Tamamlanmış POP çıktısında beklenmeyen dosya bulundu."); }
        return report;
    }
}
