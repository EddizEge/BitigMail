using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BitigMail.Engine.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MailFilterNode(string Kind, string? Field = null, string? Operator = null,
    string? Text = null, long? Number = null, DateTimeOffset? Date = null, bool? Boolean = null,
    IReadOnlyList<MailFilterNode>? Children = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MailFilterDefinition(int Version, MailFilterNode Root);
public sealed record MailFilterInput(string Subject, string Body, IReadOnlyList<string> Senders,
    IReadOnlyList<string> Recipients, DateTimeOffset? Date, long? SizeBytes,
    bool? HasAttachment, IReadOnlyList<string>? AttachmentNames);
public enum MailFilterMatch { NoMatch, Match, UnknownMetadata }

/// <summary>Shared pure semantics; callers must count/report UnknownMetadata, never claim full selection parity by guessing.</summary>
public sealed class CompiledMailFilter
{
    private readonly MailFilterNode _root;
    public string CanonicalJson { get; }
    public string Fingerprint { get; }
    public bool UsesDate { get; }
    public bool UsesBody { get; }
    internal CompiledMailFilter(MailFilterNode root)
    {
        _root = root;
        CanonicalJson = JsonSerializer.Serialize(new MailFilterDefinition(1, root));
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson)));
        UsesDate = Fields(root).Contains("date", StringComparer.Ordinal);
        UsesBody = Fields(root).Contains("body", StringComparer.Ordinal);
    }
    public MailFilterMatch Evaluate(MailFilterInput input) => EvaluateNode(_root, input);
    private static IEnumerable<string> Fields(MailFilterNode node) => node.Kind == "condition"
        ? [node.Field!] : node.Children!.SelectMany(Fields);
    private static MailFilterMatch EvaluateNode(MailFilterNode node, MailFilterInput input)
    {
        if (node.Kind is "and" or "or")
        {
            bool unknown = false;
            foreach (var child in node.Children!)
            {
                var result = EvaluateNode(child, input);
                if (node.Kind == "and" && result == MailFilterMatch.NoMatch) return result;
                if (node.Kind == "or" && result == MailFilterMatch.Match) return result;
                unknown |= result == MailFilterMatch.UnknownMetadata;
            }
            return unknown ? MailFilterMatch.UnknownMetadata : node.Kind == "and" ? MailFilterMatch.Match : MailFilterMatch.NoMatch;
        }
        bool TextMatches(string value) => node.Operator == "eq"
            ? Normalize(value).Equals(node.Text, StringComparison.OrdinalIgnoreCase)
            : Normalize(value).Contains(node.Text!, StringComparison.OrdinalIgnoreCase);
        bool Compare(long value, long target) => node.Operator switch { "eq" => value == target, "gte" => value >= target, "gt" => value > target, "lte" => value <= target, "lt" => value < target, _ => throw new InvalidOperationException() };
        bool? match = node.Field switch
        {
            "subject" => TextMatches(input.Subject),
            "body" => TextMatches(input.Body),
            "sender" => input.Senders.Any(TextMatches),
            "recipient" => input.Recipients.Any(TextMatches),
            "attachmentName" => input.AttachmentNames?.Any(TextMatches),
            "hasAttachment" => input.HasAttachment.HasValue ? input.HasAttachment == node.Boolean : null,
            "size" => input.SizeBytes.HasValue ? Compare(input.SizeBytes.Value, node.Number!.Value) : null,
            "date" => input.Date.HasValue ? Compare(input.Date.Value.UtcTicks, node.Date!.Value.UtcTicks) : null,
            _ => throw new InvalidOperationException("Derlenmemiş filtre alanı.")
        };
        return match.HasValue ? match.Value ? MailFilterMatch.Match : MailFilterMatch.NoMatch : MailFilterMatch.UnknownMetadata;
    }
    internal static string Normalize(string text) => text.Normalize(NormalizationForm.FormC);
}

public static class AdvancedMailFilter
{
    private static readonly HashSet<string> AllFields = new(StringComparer.Ordinal)
        { "subject", "body", "sender", "recipient", "attachmentName", "hasAttachment", "size", "date" };
    public static CompiledMailFilter Compile(MailFilterDefinition definition, IReadOnlySet<string>? availableFields = null, bool dateFidelityUnresolved = false)
    {
        if (definition.Version != 1 || definition.Root is null) throw new InvalidDataException("Filtre sürümü veya kökü geçersiz.");
        int nodes = 0;
        MailFilterNode ValidateAndClone(MailFilterNode node, int depth)
        {
            if (node is null || ++nodes > 256 || depth > 8) throw new InvalidDataException("Filtre derinliği veya öğe sınırı aşıldı.");
            if (node.Kind is "and" or "or")
            {
                if (node.Children is null || node.Children.Count > 64 || node.Kind == "or" && node.Children.Count == 0 ||
                    node.Field is not null || node.Operator is not null || node.Text is not null || node.Number is not null || node.Date is not null || node.Boolean is not null)
                    throw new InvalidDataException("Filtre grubu geçersiz.");
                return new(node.Kind, Children: node.Children.Select(child => ValidateAndClone(child, depth + 1)).ToArray());
            }
            if (node.Kind != "condition" || node.Children is not null || node.Field is null || !AllFields.Contains(node.Field) || availableFields is not null && !availableFields.Contains(node.Field))
                throw new InvalidDataException("Kaynak bu filtre alanını desteklemiyor.");
            bool text = node.Field is "subject" or "body" or "sender" or "recipient" or "attachmentName";
            if (text)
            {
                if (node.Operator is not ("eq" or "contains") || string.IsNullOrEmpty(node.Text) || node.Text.Length > 4096 || node.Number is not null || node.Date is not null || node.Boolean is not null)
                    throw new InvalidDataException("Metin filtresi geçersiz.");
                return node with { Text = CompiledMailFilter.Normalize(node.Text) };
            }
            if (node.Field == "hasAttachment")
            {
                if (node.Operator != "eq" || node.Boolean is null || node.Text is not null || node.Number is not null || node.Date is not null)
                    throw new InvalidDataException("Ek filtresi geçersiz.");
                return node with { };
            }
            if (node.Operator is not ("eq" or "gte" or "gt" or "lte" or "lt") || node.Text is not null || node.Boolean is not null)
                throw new InvalidDataException("Karşılaştırma filtresi geçersiz.");
            if (node.Field == "size" && (node.Number is null || node.Number < 0 || node.Date is not null))
                throw new InvalidDataException("Boyut filtresi geçersiz.");
            if (node.Field == "date" && (node.Date is null || node.Date.Value.Offset != TimeSpan.Zero || node.Number is not null || dateFidelityUnresolved))
                throw new InvalidDataException("Tarih filtresi UTC olmalı ve kaynak tarih anlamı doğrulanmış olmalıdır.");
            return node with { };
        }
        return new(ValidateAndClone(definition.Root, 0));
    }
}
