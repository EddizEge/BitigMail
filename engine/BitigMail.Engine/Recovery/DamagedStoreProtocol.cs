using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BitigMail.Engine.Recovery;

public sealed record DamagedStoreWorkerRequest(
    int SchemaVersion, string InvocationId, string JobId, string SourcePath,
    string SourceSha256, string OutputRoot, int MaxFolders, int MaxItems,
    int MaxDepth, long DeadlineUtcTicks);

public sealed record RecoveredMimeFile(string RelativePath, string Sha256, long Length,
    string? FolderDisplayName = null, string? FolderEntryIdSha256 = null,
    string? SourceEntryIdSha256 = null, string? SdkQualification = null, string? FolderPath = null);
public sealed record RecoveryFailure(string Boundary, string? EntryId, string Reason);
public sealed record DamagedStoreWorkerResult(
    int SchemaVersion, string InvocationId, string JobId, string SourceSha256,
    string Outcome, int? OriginalTotal, IReadOnlyList<RecoveredMimeFile> Messages,
    IReadOnlyList<RecoveryFailure> Failures, bool EnumerationComplete, int NonMailItemCount = 0);

public sealed record ValidatedRecoveryResult(
    string Outcome, int? OriginalTotal, int RecoveredCount, int FailedBoundaryCount,
    bool Partial, IReadOnlyList<RecoveredMimeFile> Messages, int NonMailItemCount = 0);

/// <summary>Validates an untrusted worker result before any recovered output is published.</summary>
public sealed class DamagedStoreResultValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxManifestMessages = 250_000;
    public const int MaxFailures = 25_000;
    public const long MaxManifestBytes = 32L * 1024 * 1024;

    public ValidatedRecoveryResult Validate(
        DamagedStoreWorkerRequest request, string resultPath, string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.SchemaVersion != CurrentSchemaVersion || request.MaxItems is < 1 or > MaxManifestMessages ||
            request.MaxFolders is < 1 or > 100_000 || request.MaxDepth is < 1 or > 128 ||
            string.IsNullOrWhiteSpace(request.InvocationId) || string.IsNullOrWhiteSpace(request.JobId) ||
            !IsHash(request.SourceSha256) || !Path.IsPathFullyQualified(request.SourcePath) ||
            !Path.IsPathFullyQualified(request.OutputRoot) ||
            !Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(request.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("İşçi istek kapsamı geçersiz.");
        RejectReparseChain(sourcePath);
        string stagingRoot = CanonicalDirectory(request.OutputRoot);
        string canonicalResult = Path.GetFullPath(resultPath);
        EnsureUnderRoot(canonicalResult, stagingRoot);
        RejectReparseChain(canonicalResult);
        if (!File.Exists(canonicalResult) || new FileInfo(canonicalResult).Length > MaxManifestBytes)
            throw new InvalidDataException("İşçi sonuç bildirimi eksik veya boyut sınırını aşıyor.");

        DamagedStoreWorkerResult result;
        using (var stream = new FileStream(canonicalResult, FileMode.Open, FileAccess.Read, FileShare.Read,
                   81920, FileOptions.SequentialScan))
        {
            if (stream.Length is < 1 or > MaxManifestBytes) throw new InvalidDataException("Sonuç boyutu geçersiz.");
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException("Sonuç bildirimi değişti.");
            try
            {
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
                RejectDuplicateProperties(document.RootElement);
                result = document.RootElement.Deserialize<DamagedStoreWorkerResult>(new JsonSerializerOptions
                {
                    MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
                }) ?? throw new InvalidDataException("İşçi sonuç bildirimi okunamadı.");
            }
            catch (JsonException ex) { throw new InvalidDataException("İşçi sonuç biçimi geçersiz.", ex); }
        }

        if (result.SchemaVersion != CurrentSchemaVersion || result.SchemaVersion != request.SchemaVersion ||
            !Fixed(result.InvocationId, request.InvocationId) || !Fixed(result.JobId, request.JobId) ||
            !Fixed(result.SourceSha256, request.SourceSha256))
            throw new InvalidDataException("İşçi sonucu bu çağrıya ait değil.");
        if (result.Messages is null || result.Failures is null || result.Messages.Any(m => m is null) ||
            result.Failures.Any(f => f is null || string.IsNullOrWhiteSpace(f.Boundary) ||
                string.IsNullOrWhiteSpace(f.Reason) || f.Boundary.Length > 256 || f.Reason.Length > 4096 || f.EntryId?.Length > 4096) ||
            result.Messages.Count > Math.Min(request.MaxItems, MaxManifestMessages) || result.Failures.Count > MaxFailures)
            throw new InvalidDataException("İşçi sonuç sayımları sınırı aşıyor.");
        if (result.NonMailItemCount < 0 || result.Messages.Any(m =>
            m.FolderDisplayName?.Length > 512 || !OptionalHash(m.FolderEntryIdSha256) ||
            !OptionalHash(m.SourceEntryIdSha256) || m.SdkQualification?.Length > 256))
            throw new InvalidDataException("Kurtarma metadata alanları geçersiz.");
        if (result.OriginalTotal is < 0 || (result.OriginalTotal is int total && result.Messages.Count > total))
            throw new InvalidDataException("İşçi sonuç sayımları tutarsız.");
        if (result.NonMailItemCount < 0 || result.NonMailItemCount > request.MaxItems ||
            result.NonMailItemCount + result.Messages.Count > request.MaxItems)
            throw new InvalidDataException("Kurtarma posta dışı öğe sayımı tutarsız.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RecoveredMimeFile message in result.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateMimeRelativePath(message.RelativePath);
            if (message.FolderDisplayName?.Length > 4096 || message.FolderPath?.Length > 32768 ||
                message.FolderEntryIdSha256 is not null && !IsHash(message.FolderEntryIdSha256) ||
                message.SourceEntryIdSha256 is not null && !IsHash(message.SourceEntryIdSha256) ||
                message.SdkQualification?.Length > 256)
                throw new InvalidDataException("Kurtarma öğe kimliği geçersiz.");
            if (!IsHash(message.Sha256) || message.Length is < 0 or > 64L * 1024 * 1024)
                throw new InvalidDataException("Kurtarılan ileti sınırı veya özeti geçersiz.");
            string full = Path.GetFullPath(Path.Combine(stagingRoot, message.RelativePath));
            EnsureUnderRoot(full, stagingRoot);
            RejectReparseChain(full);
            if (!seen.Add(full) || !File.Exists(full)) throw new InvalidDataException("Kurtarılan ileti bildirimi geçersiz.");
            var info = new FileInfo(full);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length != message.Length ||
                !Fixed(HashFile(full, cancellationToken), message.Sha256.ToLowerInvariant()))
                throw new InvalidDataException("Kurtarılan ileti doğrulanamadı.");
        }

        var allowed = seen.Append(canonicalResult).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((stagingRoot, 0));
        int directories = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested(); RejectReparseChain(directory.Path);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("İşçi çıktısı bağlantı içeriyor.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (directory.Depth >= request.MaxDepth || ++directories > request.MaxFolders)
                        throw new InvalidDataException("İşçi çıktı klasör sınırı aşıldı.");
                    pending.Push((entry, directory.Depth + 1));
                }
                else if (!allowed.Contains(Path.GetFullPath(entry))) throw new InvalidDataException("Sahipsiz işçi çıktısı bulundu.");
            }
        }

        string sourceAfter = HashFile(sourcePath, cancellationToken);
        if (!Fixed(sourceAfter, request.SourceSha256))
            throw new InvalidDataException("Kaynak dosya işlem sırasında değişti; çıktı yayımlanamaz.");
        if (result.Outcome is not ("healthy_extraction" or "partial_recovered" or "unreadable_source" or "cancelled" or "failed"))
            throw new InvalidDataException("Bilinmeyen kurtarma sonucu.");
        if (result.Outcome is "healthy_extraction" && (!result.EnumerationComplete || result.Failures.Count != 0))
            throw new InvalidDataException("Sağlıklı çıkarım sonucu başarısız sınırlar içeriyor.");
        if (result.Outcome == "partial_recovered" && (result.Messages.Count == 0 ||
            result.EnumerationComplete && result.Failures.Count == 0) ||
            result.Outcome == "unreadable_source" && (result.Messages.Count != 0 || result.EnumerationComplete || result.Failures.Count == 0))
            throw new InvalidDataException("Kurtarma sonucu sayım ve kapsamıyla uyuşmuyor.");

        return new(result.Outcome, result.OriginalTotal, result.Messages.Count, result.Failures.Count,
            result.Outcome == "partial_recovered", result.Messages, result.NonMailItemCount);
    }

    public static string HashFile(string path, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920]; int count;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested(); count = stream.Read(buffer);
            if (count == 0) break; hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string CanonicalDirectory(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("İşçi çıktı dizini bulunamadı.");
        RejectReparseChain(full);
        return full;
    }

    private static void EnsureUnderRoot(string path, string root)
    {
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("İşçi çıktısı ayrılmış çalışma alanının dışında.");
    }

    private static bool Fixed(string? left, string? right) => left is not null && right is not null &&
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(left), System.Text.Encoding.UTF8.GetBytes(right));

    private static bool IsHash(string? value) => value is not null && Regex.IsMatch(value, @"\A[0-9a-fA-F]{64}\z");
    private static bool OptionalHash(string? value) => value is null || IsHash(value);

    private static void ValidateMimeRelativePath(string? path)
    {
        if (path is null) throw new InvalidDataException("İleti yolu eksik.");
        BitigMail.Engine.Distribution.PackagePayloadValidator.ValidateRelativePath(path);
        if (!path.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("İleti uzantısı geçersiz.");
    }

    private static void RejectReparseChain(string path)
    {
        string? current = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (current.EndsWith(':')) current += Path.DirectorySeparatorChar;
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("İşçi dosya yolu bağlantı içeriyor.");
            current = Path.GetDirectoryName(current);
        }
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Yinelenen sonuç alanı.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectDuplicateProperties(child);
    }
}
