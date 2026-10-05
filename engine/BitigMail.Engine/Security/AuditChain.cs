using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace BitigMail.Engine.Security;

public enum AuditOutcome { Succeeded, Failed, Denied }
// Identifiers and fixed action codes only. Never supply names, subjects, paths, tokens or exception messages.
public sealed record AuditEvent(DateTimeOffset AtUtc, string ActorId, string ActionCode,
    string? CompanyId, string? ProjectId, string? JobId, AuditOutcome Outcome);
public sealed record AuditChainEntry(long Sequence, string PreviousHash, AuditEvent Event, string Hash);
public sealed record AuditChainHead(long Sequence, string Hash);

/// <summary>Detects inconsistency, not authenticity. A privileged writer can rewrite the chain and its local head.</summary>
public static class AuditChain
{
    public static AuditChainHead Empty { get; } = new(0, new string('0', 64));

    public static AuditChainEntry Append(AuditChainHead head, AuditEvent auditEvent)
    {
        ValidateHead(head);
        ValidateEvent(auditEvent);
        long sequence = checked(head.Sequence + 1);
        return new(sequence, head.Hash, auditEvent, Digest(sequence, head.Hash, auditEvent));
    }

    // expectedHead must be retained separately by the caller. Without it suffix removal is undetectable.
    public static AuditChainHead Verify(IEnumerable<AuditChainEntry> entries, AuditChainHead expectedHead,
        CancellationToken cancellationToken = default, long maximumEntries = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ValidateHead(expectedHead);
        if (maximumEntries < 1 || expectedHead.Sequence > maximumEntries)
            throw new InvalidDataException("İşlem kaydı sınırı aşıldı.");
        var head = Empty;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (head.Sequence >= maximumEntries || entry is null || entry.Sequence != head.Sequence + 1 || entry.PreviousHash != head.Hash)
                throw new InvalidDataException("İşlem kaydı sırası veya bağlantısı bozuk.");
            ValidateEvent(entry.Event);
            if (!ValidHash(entry.Hash) || entry.Hash != Digest(entry.Sequence, entry.PreviousHash, entry.Event))
                throw new InvalidDataException("İşlem kaydı içeriği değişmiş.");
            head = new(entry.Sequence, entry.Hash);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (head != expectedHead) throw new InvalidDataException("İşlem kaydı eksik veya beklenen başlıkla uyuşmuyor.");
        return head;
    }

    private static string Digest(long sequence, string previousHash, AuditEvent value)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            json.WriteStringValue("BitigMail.Audit.v1");
            json.WriteNumberValue(sequence);
            json.WriteStringValue(previousHash);
            json.WriteStringValue(value.AtUtc.ToString("O", CultureInfo.InvariantCulture));
            json.WriteStringValue(value.ActorId);
            json.WriteStringValue(value.ActionCode);
            json.WriteStringValue(value.CompanyId);
            json.WriteStringValue(value.ProjectId);
            json.WriteStringValue(value.JobId);
            json.WriteNumberValue((int)value.Outcome);
            json.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    private static void ValidateEvent(AuditEvent value)
    {
        if (value is null || value.AtUtc.Offset != TimeSpan.Zero || !Enum.IsDefined(value.Outcome) ||
            !SafeCode(value.ActorId) || !SafeCode(value.ActionCode) ||
            value.CompanyId is not null && !SafeCode(value.CompanyId) ||
            value.ProjectId is not null && !SafeCode(value.ProjectId) ||
            value.JobId is not null && !SafeCode(value.JobId))
            throw new InvalidDataException("İşlem kaydı yalnız sınırlı kimlikler ve işlem kodları içermelidir.");
    }

    private static bool SafeCode(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static void ValidateHead(AuditChainHead head)
    {
        if (head is null || head.Sequence < 0 || head.Sequence == long.MaxValue || !ValidHash(head.Hash) ||
            head.Sequence == 0 && head.Hash != Empty.Hash)
            throw new InvalidDataException("İşlem kaydı başlığı geçersiz.");
    }
}
