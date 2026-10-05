using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BitigMail.Engine.Planning;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record FolderMappingRule(string SourceFolderId,string TargetFolderPath);
public sealed record ValidatedFolderMapping(IReadOnlyDictionary<string,string> Rules,string Fingerprint);
public static class FolderMappingPolicy
{
    public static ValidatedFolderMapping Validate(IEnumerable<FolderMappingRule> rules)
    {
        var materialized=rules.Take(257).ToArray();if(materialized.Length>256)throw new InvalidDataException("Klasör eşleme sayısı sınırı aşıyor.");var map=new Dictionary<string,string>(StringComparer.Ordinal);var targets=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var rule in materialized){if(string.IsNullOrWhiteSpace(rule.SourceFolderId)||rule.SourceFolderId.Length>512||map.ContainsKey(rule.SourceFolderId))throw new InvalidDataException("Kaynak klasör eşlemesi yineleniyor.");string target=Normalize(rule.TargetFolderPath);if(targets.TryGetValue(target,out string? prior)&&prior!=rule.SourceFolderId)throw new InvalidDataException("İlişkisiz kaynak klasörler aynı hedefe örtük birleştirilemez.");map[rule.SourceFolderId]=target;targets[target]=rule.SourceFolderId;}
        string json=JsonSerializer.Serialize(map.OrderBy(x=>x.Key,StringComparer.Ordinal));return new(map,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
    }
    private static string Normalize(string value){string normalized=value.Normalize(NormalizationForm.FormC);BitigMail.Engine.Distribution.PackagePayloadValidator.ValidateRelativePath(normalized);return normalized;}
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum DuplicatePolicy { PreservePhysical, ContentOnly, ContentAndMetadata }
public sealed record DuplicateCandidate(string PhysicalItemId,string RawContentSha256,string MetadataFingerprint);
public sealed record DuplicateDecision(IReadOnlyList<DuplicateCandidate> Included,IReadOnlyList<string> SkippedPhysicalItemIds);
public static class DuplicatePolicyEvaluator
{
    public static DuplicateDecision Apply(IEnumerable<DuplicateCandidate> items,DuplicatePolicy policy)
    {
        var included=new List<DuplicateCandidate>();var skipped=new List<string>();var physical=new HashSet<string>(StringComparer.Ordinal);var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in items){if(!physical.Add(item.PhysicalItemId)||item.PhysicalItemId.Length>1024||!IsHash(item.RawContentSha256)||item.MetadataFingerprint.Length>1024)throw new InvalidDataException("Fiziksel öğe kimliği veya içerik özeti geçersiz.");string? identity=policy switch{DuplicatePolicy.PreservePhysical=>null,DuplicatePolicy.ContentOnly=>item.RawContentSha256,DuplicatePolicy.ContentAndMetadata=>item.RawContentSha256+":"+item.MetadataFingerprint,_=>throw new InvalidDataException()};if(identity is not null&&!identities.Add(identity)){skipped.Add(item.PhysicalItemId);continue;}included.Add(item);}return new(included,skipped);
    }
    private static bool IsHash(string value)=>value.Length==64&&value.All(Uri.IsHexDigit);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransferTemplate(int Version,string Name,string? CanonicalFilterJson,IReadOnlyList<FolderMappingRule> FolderMappings,DuplicatePolicy DuplicatePolicy,int WaitingPriority);
public static class TransferTemplatePolicy
{
    public static string Serialize(TransferTemplate template){if(template.Version!=1||string.IsNullOrWhiteSpace(template.Name)||template.Name.Length>128||template.WaitingPriority is < -100 or > 100)throw new InvalidDataException("Şablon geçersiz.");_ = FolderMappingPolicy.Validate(template.FolderMappings);if(template.CanonicalFilterJson is not null){if(template.CanonicalFilterJson.Length>64*1024)throw new InvalidDataException("Şablon filtresi boyut sınırını aşıyor.");var definition=JsonSerializer.Deserialize<BitigMail.Engine.Models.MailFilterDefinition>(template.CanonicalFilterJson,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Şablon filtresi geçersiz.");_ = BitigMail.Engine.Models.AdvancedMailFilter.Compile(definition);}return JsonSerializer.Serialize(template);}
}

public sealed record ArchiveSearchSelectionItem(string PhysicalItemId,string RawContentSha256);
public sealed record FrozenArchiveSearchSelection(string OwnerFingerprint,string QueryFingerprint,IReadOnlyList<ArchiveSearchSelectionItem> Items,string Fingerprint);
public static class ArchiveSearchSelectionPolicy
{
    public static FrozenArchiveSearchSelection Freeze(string ownerFingerprint,string queryFingerprint,IEnumerable<ArchiveSearchSelectionItem> items){var list=items.Select(x=>new ArchiveSearchSelectionItem(x.PhysicalItemId,x.RawContentSha256.ToLowerInvariant())).ToArray();if(string.IsNullOrWhiteSpace(ownerFingerprint)||string.IsNullOrWhiteSpace(queryFingerprint)||list.Length==0||list.Length>250_000||list.Select(x=>x.PhysicalItemId).Distinct(StringComparer.Ordinal).Count()!=list.Length||list.Any(x=>x.PhysicalItemId.Length>1024||x.RawContentSha256.Length!=64||!x.RawContentSha256.All(Uri.IsHexDigit)))throw new InvalidDataException("Arama seçimi geçersiz.");string canonical=Canonical(ownerFingerprint,queryFingerprint,list);return new(ownerFingerprint,queryFingerprint,Array.AsReadOnly(list),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());}
    public static void Revalidate(FrozenArchiveSearchSelection frozen,string owner,IReadOnlyDictionary<string,string> current){string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(frozen.OwnerFingerprint,frozen.QueryFingerprint,frozen.Items)))).ToLowerInvariant();if(!string.Equals(fingerprint,frozen.Fingerprint,StringComparison.Ordinal)||!string.Equals(frozen.OwnerFingerprint,owner,StringComparison.Ordinal)||frozen.Items.Any(x=>!current.TryGetValue(x.PhysicalItemId,out string? hash)||!string.Equals(hash,x.RawContentSha256,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Seçili arama öğeleri eksik, değişmiş veya yetkisiz.");}
    private static string Canonical(string owner,string query,IEnumerable<ArchiveSearchSelectionItem> items)=>JsonSerializer.Serialize(new{ownerFingerprint=owner,queryFingerprint=query,items=items});
}
