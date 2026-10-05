using System.Text.Json;

namespace BitigMail.Engine.Distribution;

/// <summary>Desktop profile compatibility marker, accessed only while owning its lifetime lease.</summary>
public static class DesktopProfileSchema
{
    public const int Current = 1;
    public const string FileName = ".bitigmail-data-schema.json";

    public static int ReadOrInitialize(DesktopInstanceLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        string root = lease.HeldProfileDirectory;
        string path = Path.Combine(root, FileName);
        if (!File.Exists(path))
        {
            if (Directory.EnumerateFileSystemEntries(root).Any(p =>
                !Path.GetFileName(p).Equals(DesktopInstanceLease.FileName, StringComparison.Ordinal)))
                throw new InvalidDataException("Mevcut veri profilinin şema kaydı yok. Otomatik sürüm geçişi yapılamaz.");
            // CreateNew never replaces a marker created concurrently. A partial write fails closed on restart.
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write("{\"Product\":\"BitigMail\",\"SchemaVersion\":1}"u8);
            output.Flush(flushToDisk: true);
            return Current;
        }
        PackagePayloadValidator.CheckExistingAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 1 or > 1024) throw new InvalidDataException("Veri şema kaydı boyutu geçersiz.");
        try
        {
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 2 });
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Veri şema kaydı geçersiz.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (property.Name is not ("Product" or "SchemaVersion") || !names.Add(property.Name))
                    throw new InvalidDataException("Veri şema kaydı alanları geçersiz.");
            if (names.Count != 2 || value.GetProperty("Product").ValueKind != JsonValueKind.String ||
                value.GetProperty("Product").GetString() != "BitigMail" ||
                value.GetProperty("SchemaVersion").ValueKind != JsonValueKind.Number ||
                !value.GetProperty("SchemaVersion").TryGetInt32(out int schema) || schema < 1)
                throw new InvalidDataException("Veri şema kaydı geçersiz.");
            return schema;
        }
        catch (JsonException ex) { throw new InvalidDataException("Veri şema kaydı okunamadı.", ex); }
    }

    public static void RequireCurrent(DesktopInstanceLease lease)
    {
        if (ReadOrInitialize(lease) != Current)
            throw new InvalidDataException("Bu BitigMail sürümü mevcut veri profili şemasını desteklemiyor.");
    }
}
