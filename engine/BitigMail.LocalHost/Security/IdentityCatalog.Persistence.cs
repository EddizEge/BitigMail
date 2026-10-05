using System.Buffers.Binary;
using System.Text.Json;

namespace BitigMail.LocalHost.Security;

public sealed partial class IdentityCatalog
{
    private const int MaximumCatalogBytes = 4 * 1024 * 1024;

    private IdentityCatalogDocument Load()
    {
        RejectLinks(_path);
        if (!File.Exists(_path)) return new(1, Array.Empty<LocalUser>(), Array.Empty<LocalCompany>());
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is < 1 or > MaximumCatalogBytes) throw new InvalidDataException();
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException();
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            ValidateShape(json.RootElement);
            var doc = json.Deserialize<IdentityCatalogDocument>(Json) ?? throw new InvalidDataException();
            Validate(doc);
            return doc;
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or FormatException or OverflowException or InvalidOperationException)
        { throw new InvalidDataException("Kimlik kataloğu bozuk; güvenli biçimde başlatma durduruldu.", ex); }
    }

    private static void ValidateShape(JsonElement root)
    {
        Fields(root, "version", "users", "companies");
        foreach (var user in root.GetProperty("users").EnumerateArray())
        {
            Fields(user, "userId", "userName", "normalizedUserName", "passwordHash", "role", "active", "securityVersion", "grants", "createdAtUtc");
            foreach (var grant in user.GetProperty("grants").EnumerateArray()) Fields(grant, "companyId", "projectIds");
        }
        foreach (var company in root.GetProperty("companies").EnumerateArray())
        {
            Fields(company, "companyId", "name", "active", "projects");
            foreach (var project in company.GetProperty("projects").EnumerateArray()) Fields(project, "projectId", "name", "active");
        }
    }

    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name) || property.Value.ValueKind == JsonValueKind.Null)
                throw new InvalidDataException();
        if (seen.Count != names.Length) throw new InvalidDataException();
    }

    private static void Validate(IdentityCatalogDocument doc)
    {
        if (doc.Version != 1 || doc.Users is null || doc.Companies is null || doc.Users.Count > 1000 || doc.Companies.Count > 10000)
            throw new InvalidDataException();
        var companies = new Dictionary<string, LocalCompany>(StringComparer.Ordinal);
        var projects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var company in doc.Companies)
        {
            if (company is null || !ValidId(company.CompanyId) || !ValidName(company.Name) || company.Projects is null ||
                company.Projects.Count > 10000 || !companies.TryAdd(company.CompanyId, company)) throw new InvalidDataException();
            foreach (var project in company.Projects)
                if (project is null || !ValidId(project.ProjectId) || !ValidName(project.Name) || !projects.Add(project.ProjectId) || projects.Count > 100000)
                    throw new InvalidDataException();
        }
        var ids = new HashSet<string>(StringComparer.Ordinal); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in doc.Users)
        {
            if (user is null || !ValidId(user.UserId) || !ids.Add(user.UserId) || !ValidName(user.UserName) ||
                user.NormalizedUserName != Normalize(user.UserName) || !names.Add(user.NormalizedUserName) ||
                !Enum.IsDefined(user.Role) || user.SecurityVersion < 1 || user.CreatedAtUtc.Offset != TimeSpan.Zero || !ValidPasswordHash(user.PasswordHash))
                throw new InvalidDataException();
            IReadOnlyList<CompanyGrant> grants;
            try { grants = CopyGrants(user.Grants); } catch (ArgumentException ex) { throw new InvalidDataException("Kullanıcı kapsamı bozuk.", ex); }
            foreach (var grant in grants)
                if (!companies.TryGetValue(grant.CompanyId, out var company) || grant.ProjectIds.Any(p => !company.Projects.Any(x => x.ProjectId == p)))
                    throw new InvalidDataException();
        }
        if (doc.Users.Count != 0 && !doc.Users.Any(u => u.Active && u.Role == LocalUserRole.Admin)) throw new InvalidDataException("Etkin yönetici eksik.");
        if (doc.Users.Count == 0 && doc.Companies.Count != 0) throw new InvalidDataException();
    }

    private static bool ValidPasswordHash(string hash)
    {
        if (hash is null || hash.Length > 512) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(hash); } catch (FormatException) { return false; }
        if (bytes.Length == 49 && bytes[0] == 0) return true;
        if (bytes.Length < 13 || bytes[0] != 1) return false;
        uint prf = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4));
        uint iterations = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4));
        uint saltLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9, 4));
        return prf <= 2 && iterations is >= 1 and <= 1_000_000 && saltLength is >= 16 and <= 64 && bytes.Length - 13 - saltLength is >= 16 and <= 64;
    }

    private static IReadOnlyList<CompanyGrant> CopyGrants(IReadOnlyList<CompanyGrant> grants)
    {
        if (grants is null || grants.Count > 10000) throw new ArgumentException("Kullanıcı kapsamı geçersiz.");
        var seen = new HashSet<string>(StringComparer.Ordinal); var result = new List<CompanyGrant>();
        foreach (var grant in grants)
        {
            if (grant is null || !ValidId(grant.CompanyId) || !seen.Add(grant.CompanyId) || grant.ProjectIds is null || grant.ProjectIds.Count > 10000)
                throw new ArgumentException("Kullanıcı kapsamı geçersiz.");
            var ids = grant.ProjectIds.ToArray();
            if (ids.Any(p => !ValidId(p)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("Kullanıcı kapsamı geçersiz.");
            result.Add(new(grant.CompanyId, Array.AsReadOnly(ids)));
        }
        return result.AsReadOnly();
    }

    private static void ValidateRequestedGrants(IdentityCatalogDocument doc, IReadOnlyList<CompanyGrant> grants)
    {
        foreach (var grant in grants)
            if (!doc.Companies.Any(c => c.Active && c.CompanyId == grant.CompanyId && grant.ProjectIds.All(p => c.Projects.Any(x => x.Active && x.ProjectId == p))))
                throw new ArgumentException("Kullanıcı kapsamı geçersiz.");
    }

    private void Save(IdentityCatalogDocument doc)
    {
        Validate(doc);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(doc, Json);
        if (bytes.Length > MaximumCatalogBytes) throw new InvalidOperationException("Kimlik kataloğu boyut sınırı aşıldı.");
        RejectLinks(_path);
        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            File.Move(temp, _path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Kimlik yolu bağlantı içeremez."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
    }
    private static bool ValidId(string value) => value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    private static bool ValidName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
}
