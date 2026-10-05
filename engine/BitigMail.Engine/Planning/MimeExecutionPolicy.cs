using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;

namespace BitigMail.Engine.Planning;

public sealed record MimeExecutionPolicySnapshot(string SourceFingerprint, IReadOnlyList<string> CandidateIds,
    IReadOnlyList<string> IncludedIds, IReadOnlyList<string> SkippedIds, IReadOnlyList<FolderMappingRule> Mappings,
    DuplicatePolicy DuplicatePolicy, string MappingFingerprint, string Fingerprint);

public static class MimeExecutionPolicy
{
    public static MimeExecutionPolicySnapshot Freeze(MimeSourceManifest manifest, IEnumerable<string> candidateIds,
        IEnumerable<FolderMappingRule> mappings, DuplicatePolicy duplicatePolicy)
    {
        if (!Enum.IsDefined(duplicatePolicy) || manifest.Entries.Count > 100_000) throw new InvalidDataException("MIME işlem politikası veya öğe sınırı geçersiz.");
        var rules = mappings.Take(257).Select(r => new FolderMappingRule(r.SourceFolderId, r.TargetFolderPath)).ToArray();
        var targets = ResolveTargets(manifest, rules);
        var ids = candidateIds.ToHashSet(StringComparer.Ordinal);
        var selected = manifest.Entries.OrderBy(e => e.PhysicalOrdinal).Where(e => ids.Contains(e.PhysicalOrdinal.ToString())).ToArray();
        if (selected.Length != ids.Count || selected.Select(e => e.PhysicalOrdinal).Distinct().Count() != selected.Length)
            throw new InvalidDataException("MIME fiziksel seçimi kaynak ile uyuşmuyor.");
        var candidates = selected.Select(e => new DuplicateCandidate(e.PhysicalOrdinal.ToString(), e.Sha256,
            Hash(e.MappedFolder))).ToArray();
        var decision = DuplicatePolicyEvaluator.Apply(candidates, duplicatePolicy);
        var canonicalIds = selected.Select(e => e.PhysicalOrdinal.ToString()).ToArray();
        var included = decision.Included.Select(e => e.PhysicalItemId).ToArray();
        var skipped = decision.SkippedPhysicalItemIds.ToArray();
        string mappingHash = Hash(JsonSerializer.Serialize(targets.OrderBy(x => x.Key, StringComparer.Ordinal)));
        string fingerprint = Hash(JsonSerializer.Serialize(new { Source = manifest.AggregateFingerprint, candidates, included, skipped, mappingHash, duplicatePolicy }));
        return new(manifest.AggregateFingerprint, Array.AsReadOnly(canonicalIds), Array.AsReadOnly(included),
            Array.AsReadOnly(skipped), Array.AsReadOnly(rules), duplicatePolicy, mappingHash, fingerprint);
    }

    public static IReadOnlyDictionary<string, string> Validate(MimeSourceManifest manifest, RegisteredSelection selection)
    {
        var snapshot = selection.ExecutionPolicy ?? throw new InvalidDataException("MIME işlem politikası eksik.");
        var expected = Freeze(manifest, snapshot.CandidateIds, snapshot.Mappings, snapshot.DuplicatePolicy);
        if (snapshot.SourceFingerprint != manifest.AggregateFingerprint || expected.Fingerprint != snapshot.Fingerprint ||
            expected.MappingFingerprint != snapshot.MappingFingerprint || !expected.CandidateIds.SequenceEqual(snapshot.CandidateIds) ||
            selection.SelectedMessagesCount != expected.IncludedIds.Count || !expected.IncludedIds.SequenceEqual(snapshot.IncludedIds) ||
            !expected.SkippedIds.SequenceEqual(snapshot.SkippedIds) || !selection.SelectedMessageKeys.SetEquals(expected.IncludedIds))
            throw new InvalidDataException("MIME işlem politikası önizleme sonrasında değişmiş.");
        return ResolveTargets(manifest, snapshot.Mappings);
    }

    private static IReadOnlyDictionary<string, string> ResolveTargets(MimeSourceManifest manifest, IEnumerable<FolderMappingRule> rules)
    {
        var overrides = FolderMappingPolicy.Validate(rules).Rules;
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string folder in manifest.Entries.Select(e => e.MappedFolder).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string id = MimeSourceInspector.ComputeFolderId(manifest.AggregateFingerprint, folder);
            sourceIds.Add(id);
            string target = overrides.TryGetValue(id, out var mapped) ? mapped : folder;
            if (!occupied.Add(target)) throw new InvalidDataException("Kaynak klasörler aynı hedefe örtük birleştirilemez.");
            targets[folder] = target;
        }
        if (overrides.Keys.Any(id => !sourceIds.Contains(id))) throw new InvalidDataException("Klasör eşlemesi bu kaynağa ait değil.");
        return targets;
    }

    public static MimeSourceEntry ForOutput(MimeSourceEntry source, IReadOnlyDictionary<string, string>? targets) => new()
    {
        CanonicalPath = source.CanonicalPath, RelativePath = source.RelativePath, MappedFolder = targets is null ? source.MappedFolder : targets[source.MappedFolder],
        SizeBytes = source.SizeBytes, Sha256 = source.Sha256, PhysicalOrdinal = source.PhysicalOrdinal
    };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
