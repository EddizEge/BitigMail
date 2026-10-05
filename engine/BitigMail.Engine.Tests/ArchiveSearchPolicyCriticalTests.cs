using BitigMail.Engine.Archive;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class ArchiveSearchPolicyCriticalTests
{
    [Theory]
    [InlineData("istanbul", ArchiveSearchField.All, 3)]
    [InlineData("İSTANBUL", ArchiveSearchField.Subject, 2)]
    [InlineData("I\u0307STANBUL", ArchiveSearchField.Subject, 2)]
    [InlineData("istanbul", ArchiveSearchField.Body, 1)]
    [InlineData("invoice", ArchiveSearchField.Body, 2)]
    [InlineData("INVOICE", ArchiveSearchField.Body, 2)]
    [InlineData("IĞDIR", ArchiveSearchField.Body, 2)]
    [InlineData("ığdır", ArchiveSearchField.Body, 2)]
    [InlineData("igdir", ArchiveSearchField.Body, 0)]
    [InlineData("şartname", ArchiveSearchField.AttachmentName, 2)]
    [InlineData("OR", ArchiveSearchField.All, 1)]
    [InlineData("istanbul OR başka", ArchiveSearchField.All, 0)]
    [InlineData("subject_search:başka", ArchiveSearchField.All, 0)]
    [InlineData("\" OR \"istanbul", ArchiveSearchField.All, 0)]
    [InlineData("*", ArchiveSearchField.All, 0)]
    public void RealFts_CompiledQueryKeepsLanguageFieldsLiteralsAndPhysicalRows(string query, ArchiveSearchField field, long expected)
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        database.Open();
        using (var create = database.CreateCommand())
        {
            create.CommandText = "CREATE VIRTUAL TABLE messages USING fts5(subject_search,sender_search,recipient_search,body_search,attachment_search, tokenize='unicode61 remove_diacritics 0');";
            create.ExecuteNonQuery();
        }
        foreach (var row in new[] {
            new[] { "İSTANBUL Projesi", "Ahmet", "Mehmet", "IĞDIR INVOICE", "şartname_özeti_2024.txt" },
            new[] { "Başka", "Can", "Cem", "istanbul", "" },
            new[] { "Bilgilendirme", "Can", "Cem", "literal OR NEAR", "" },
            new[] { "İSTANBUL Projesi", "Ahmet", "Mehmet", "IĞDIR INVOICE", "şartname_özeti_2024.txt" } })
        {
            using var insert = database.CreateCommand();
            insert.CommandText = "INSERT INTO messages VALUES ($s,$f,$t,$b,$a)";
            var names = new[] { "$s", "$f", "$t", "$b", "$a" };
            for (var i = 0; i < row.Length; i++) insert.Parameters.AddWithValue(names[i], ArchiveSearchPolicy.NormalizeSearchText(row[i]));
            insert.ExecuteNonQuery();
        }
        using var search = database.CreateCommand();
        search.CommandText = "SELECT count(*) FROM messages WHERE messages MATCH $query";
        search.Parameters.AddWithValue("$query", ArchiveSearchPolicy.CompileLiteralQuery(query, field)!);
        Assert.Equal(expected, (long)search.ExecuteScalar()!);
    }

    [Fact]
    public void Query_InvalidControlUnicodeLengthTermCountAndField_AreRejectedWithoutEcho()
    {
        foreach (var input in new[] { "secret\0data", "\ud800", new string('a', 513), string.Join(' ', Enumerable.Repeat("a", 33)) })
        {
            var error = Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.CompileLiteralQuery(input));
            Assert.DoesNotContain(input, error.Message);
        }
        Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.CompileLiteralQuery(null, (ArchiveSearchField)99));
        Assert.Null(ArchiveSearchPolicy.CompileLiteralQuery(" \t\n "));
    }

    [Fact]
    public void EmptyScopeNeverGrantsPreview_AndScopesAreDetachedFromCallerArray()
    {
        var owner = new ArchiveScopeSelection("company-a", "project-a", "arc_1");
        var empty = ArchiveSearchPolicy.ValidateSelections(Array.Empty<ArchiveScopeSelection>());
        Assert.Empty(empty);
        Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.EnsureSelectedOwner(empty, owner));
        var input = new[] { owner };
        var validated = ArchiveSearchPolicy.ValidateSelections(input);
        input[0] = new ArchiveScopeSelection("company-b", "project-b", "arc_2");
        ArchiveSearchPolicy.EnsureSelectedOwner(validated, owner);
        Assert.Equal(owner, validated.Single());
    }

    [Theory]
    [InlineData("company-b", "project-a", "arc_1")]
    [InlineData("company-a", "project-b", "arc_1")]
    [InlineData("company-a", "project-a", "arc_2")]
    [InlineData("Company-a", "project-a", "arc_1")]
    public void OwnerRequiresExactCompanyProjectAndArchive(string company, string project, string archive)
    {
        var owner = new ArchiveScopeSelection("company-a", "project-a", "arc_1");
        var requested = new ArchiveScopeSelection(company, project, archive);
        Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.EnsureOwnedScope(requested, owner));
        Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.EnsureSelectedOwner(ArchiveSearchPolicy.ValidateSelections(new[] { requested }), owner));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a:stream")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    public void ArchiveIdentifiersCannotBecomePaths(string archive)
        => Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.ValidateScope(new("company-a", "project-a", archive)));

    [Fact]
    public void SelectionLimitsAndDuplicateArchiveIdsRejectRatherThanExpandScope()
    {
        Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.ValidateSelections(null));
        Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.ValidateSelections(Enumerable.Range(0, 101).Select(i => new ArchiveScopeSelection("a", "b", "arc_" + i))));
        Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.ValidateSelections(new[] {
            new ArchiveScopeSelection("a", "b", "arc_1"), new ArchiveScopeSelection("foreign", "b", "arc_1") }));
        Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.ValidateScope(new(" ", "b", "arc_1")));
    }

    [Fact]
    public void DateFilterUsesInclusiveUtcPlusThreeDaysAndExplicitMissingDateBehavior()
    {
        var range = ArchiveSearchPolicy.ParseDates("2024-01-01", "2024-01-01");
        var start = DateTimeOffset.Parse("2023-12-31T21:00:00Z");
        var end = DateTimeOffset.Parse("2024-01-01T21:00:00Z");
        Assert.Equal(start, range.StartUtcInclusive);
        Assert.Equal(end, range.EndUtcExclusive);
        Assert.True(ArchiveSearchPolicy.IncludesDate(range, start));
        Assert.True(ArchiveSearchPolicy.IncludesDate(range, end.AddTicks(-1)));
        Assert.False(ArchiveSearchPolicy.IncludesDate(range, start.AddTicks(-1)));
        Assert.False(ArchiveSearchPolicy.IncludesDate(range, end));
        Assert.False(ArchiveSearchPolicy.IncludesDate(range, null));
        Assert.True(ArchiveSearchPolicy.IncludesDate(ArchiveSearchPolicy.ParseDates(null, null), null));
    }

    [Theory]
    [InlineData("2024-02-30", null)]
    [InlineData("2024-02-02", "2024-02-01")]
    [InlineData("0001-01-01", null)]
    [InlineData(null, "9999-12-31")]
    [InlineData("2024-01-01T00:00:00Z", null)]
    public void MalformedAndUnrepresentableDateBoundsAreControlledErrors(string? start, string? end)
        => Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.ParseDates(start, end));

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void InvalidPagesAreRejected(int page, int size)
        => Assert.Throws<ArchiveSearchPolicyException>(() => ArchiveSearchPolicy.ValidatePageAndGetOffset(page, size));

    [Fact]
    public void ValidPaginationDoesNotOverflow()
        => Assert.Equal(99_999_900L, ArchiveSearchPolicy.ValidatePageAndGetOffset(1_000_000, 100));
}
