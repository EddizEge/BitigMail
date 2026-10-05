using System.Text.Json;
using System.Text.Json.Serialization;

namespace BitigMail.Engine.Distribution;

/// <summary>Strict, bounded wire-format admission. Payload and publisher validation are separate steps.</summary>
public static class PackageManifestReader
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    private static readonly string[] ManifestFields =
        ["SchemaVersion", "Product", "Version", "MinimumDataSchema", "MaximumDataSchema", "Files"];
    private static readonly string[] FileFields = ["RelativePath", "Length", "Sha256"];

    public static async Task<PackagePayloadManifest> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        var block = new byte[64 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(block.AsMemory(0, (int)Math.Min(block.Length, MaximumBytes + 1L - buffer.Length)), cancellationToken);
            if (read == 0) break;
            buffer.Write(block, 0, read);
            if (buffer.Length > MaximumBytes) throw new InvalidDataException("Paket bildirimi boyut sınırını aşıyor.");
        }
        if (buffer.Length == 0) throw new InvalidDataException("Paket bildirimi boş.");
        try
        {
            using var json = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)), new JsonDocumentOptions { MaxDepth = 8 });
            RequireFields(json.RootElement, ManifestFields);
            var files = json.RootElement.GetProperty("Files");
            if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() is < 1 or > 100_000)
                throw new InvalidDataException("Paket dosya listesi sınır dışında.");
            foreach (var file in files.EnumerateArray()) RequireFields(file, FileFields);
            var manifest = json.RootElement.Deserialize<PackagePayloadManifest>(new JsonSerializerOptions
            {
                MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidDataException("Paket bildirimi okunamadı.");
            if (manifest.Product is null || manifest.Version is null || manifest.Files.Any(f => f.RelativePath is null || f.Sha256 is null))
                throw new InvalidDataException("Paket bildirimi boş alan içeriyor.");
            return manifest with { Files = Array.AsReadOnly(manifest.Files.ToArray()) };
        }
        catch (JsonException ex) { throw new InvalidDataException("Paket bildirimi biçimi geçersiz.", ex); }
    }

    private static void RequireFields(JsonElement element, IReadOnlyCollection<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Paket bildirimi nesnesi geçersiz.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name))
                throw new InvalidDataException("Paket bildirimi bilinmeyen veya yinelenen alan içeriyor.");
        if (names.Count != expected.Count) throw new InvalidDataException("Paket bildirimi zorunlu alanları eksik.");
    }
}
