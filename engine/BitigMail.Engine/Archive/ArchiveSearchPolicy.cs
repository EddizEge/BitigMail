using System.Globalization;
using System.Text;

namespace BitigMail.Engine.Archive;

public sealed record ArchiveScopeSelection(string CompanyId, string ProjectId, string ArchiveId);

public enum ArchiveSearchField { All, Subject, Sender, Recipient, Body, AttachmentName }

public sealed record ArchiveDateBounds(DateTimeOffset? StartUtcInclusive, DateTimeOffset? EndUtcExclusive);

public sealed class ArchiveSearchPolicyException(string message) : ArgumentException(message) { }

/// <summary>Pure boundary policy. SQL storage and HTTP mapping remain separate.</summary>
public static class ArchiveSearchPolicy
{
    public const int MaxQueryLength = 512;
    public const int MaxTerms = 32;
    public const int MaxSelectedArchives = 100;
    public const int MaxPageSize = 100;
    public const int MaxPage = 1_000_000;

    public static string NormalizeSearchText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            return value.Normalize(NormalizationForm.FormC)
                .ToLower(CultureInfo.GetCultureInfo("tr-TR")).Replace('ı', 'i');
        }
        catch (ArgumentException)
        {
            throw new ArchiveSearchPolicyException("Arama metni geçerli Unicode olmalıdır.");
        }
    }

    public static string? CompileLiteralQuery(string? query, ArchiveSearchField field = ArchiveSearchField.All)
    {
        var column = GetSearchColumn(field);
        if (query is null) return null;
        if (query.Length > MaxQueryLength)
            throw new ArchiveSearchPolicyException("Arama metni en fazla 512 karakter olabilir.");
        if (query.Any(c => char.IsControl(c) && !char.IsWhiteSpace(c)))
            throw new ArchiveSearchPolicyException("Arama metni desteklenmeyen kontrol karakteri içeriyor.");
        var terms = NormalizeSearchText(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > MaxTerms)
            throw new ArchiveSearchPolicyException("Arama en fazla 32 terim içerebilir.");
        if (terms.Length == 0) return null;
        var literal = string.Join(" AND ", terms.Select(term => "\"" + term.Replace("\"", "\"\"") + "\""));
        return column is null ? literal : column + " : (" + literal + ")";
    }

    // These are identifiers chosen by the application, never caller-supplied SQL.
    public static string? GetSearchColumn(ArchiveSearchField field) => field switch
    {
        ArchiveSearchField.All => null,
        ArchiveSearchField.Subject => "subject_search",
        ArchiveSearchField.Sender => "sender_search",
        ArchiveSearchField.Recipient => "recipient_search",
        ArchiveSearchField.Body => "body_search",
        ArchiveSearchField.AttachmentName => "attachment_search",
        _ => throw new ArchiveSearchPolicyException("Arama alanı desteklenmiyor.")
    };

    public static IReadOnlyList<ArchiveScopeSelection> ValidateSelections(IEnumerable<ArchiveScopeSelection>? selections)
    {
        if (selections is null)
            throw new ArchiveSearchPolicyException("Arama kapsamı belirtilmelidir.");
        var result = new List<ArchiveScopeSelection>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in selections)
        {
            if (result.Count >= MaxSelectedArchives)
                throw new ArchiveSearchPolicyException("Bir aramada en fazla 100 arşiv seçilebilir.");
            ValidateScope(scope);
            if (!ids.Add(scope.ArchiveId))
                throw new ArchiveSearchPolicyException("Arama kapsamında aynı arşiv birden fazla kez bulunamaz.");
            result.Add(scope);
        }
        // Empty stays empty; consumers must not substitute a wildcard.
        return result.AsReadOnly();
    }

    public static void ValidateScope(ArchiveScopeSelection? scope)
    {
        if (scope is null || !IsContextId(scope.CompanyId) || !IsContextId(scope.ProjectId) || !IsArchiveId(scope.ArchiveId))
            throw new ArchiveSearchPolicyException("Şirket, proje ve arşiv kapsamı geçerli olmalıdır.");
    }

    public static void EnsureOwnedScope(ArchiveScopeSelection requested, ArchiveScopeSelection registeredOwner)
    {
        ValidateScope(requested);
        ValidateScope(registeredOwner);
        if (requested != registeredOwner)
            throw new ArchiveSearchPolicyException("Arşiv seçilen şirket ve proje kapsamına ait değil.");
    }

    public static void EnsureSelectedOwner(IReadOnlyList<ArchiveScopeSelection> validatedSelections, ArchiveScopeSelection registeredOwner)
    {
        ArgumentNullException.ThrowIfNull(validatedSelections);
        ValidateScope(registeredOwner);
        if (!validatedSelections.Contains(registeredOwner))
            throw new ArchiveSearchPolicyException("İleti seçili arama kapsamında bulunmuyor.");
    }

    public static long ValidatePageAndGetOffset(int page, int pageSize)
    {
        if (page is < 1 or > MaxPage || pageSize is < 1 or > MaxPageSize)
            throw new ArchiveSearchPolicyException("Sayfa veya sayfa boyutu desteklenen aralığın dışında.");
        return ((long)page - 1) * pageSize;
    }

    public static ArchiveDateBounds ParseDates(string? startDate, string? endDate)
    {
        var start = ParseDay(startDate);
        var end = ParseDay(endDate);
        if (start is not null && end is not null && start > end)
            throw new ArchiveSearchPolicyException("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        try
        {
            var offset = TimeSpan.FromHours(3);
            return new ArchiveDateBounds(
                start is null ? null : new DateTimeOffset(start.Value, offset).ToUniversalTime(),
                end is null ? null : new DateTimeOffset(end.Value.AddDays(1), offset).ToUniversalTime());
        }
        catch (ArgumentException)
        {
            throw new ArchiveSearchPolicyException("Tarih aralığı desteklenen sınırların dışında.");
        }
    }

    public static bool IncludesDate(ArchiveDateBounds bounds, DateTimeOffset? mimeDate)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds.StartUtcInclusive is null && bounds.EndUtcExclusive is null) return true;
        return mimeDate is not null &&
            (bounds.StartUtcInclusive is null || mimeDate.Value >= bounds.StartUtcInclusive.Value) &&
            (bounds.EndUtcExclusive is null || mimeDate.Value < bounds.EndUtcExclusive.Value);
    }

    private static DateTime? ParseDay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new ArchiveSearchPolicyException("Tarihler yıl-ay-gün biçiminde geçerli bir gün olmalıdır.");
        return DateTime.SpecifyKind(day, DateTimeKind.Unspecified);
    }

    private static bool IsContextId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
    private static bool IsArchiveId(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
