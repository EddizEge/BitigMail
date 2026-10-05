using BitigMail.Engine.Models;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class AdvancedMailFilterTests
{
    private static MailFilterInput Input => new("İstanbul bütçe", "bir gövde", ["alice@example.test"], ["bob@example.test"],
        DateTimeOffset.Parse("2026-09-20T00:00:00Z"), 100, true, ["rapor.pdf"]);
    private static MailFilterNode Text(string field, string text) => new("condition", field, "contains", Text: text);
    private static CompiledMailFilter Compile(MailFilterNode root) => AdvancedMailFilter.Compile(new(1, root));
    [Fact] public void NestedAndOrUsesSameFrozenDefinition()
    {
        var filter = Compile(new("and", Children: [Text("sender", "ALICE"), new("or", Children: [Text("subject", "missing"), Text("attachmentName", ".pdf")])]));
        Assert.Equal(MailFilterMatch.Match, filter.Evaluate(Input));
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<MailFilterDefinition>(filter.CanonicalJson)!;
        Assert.Equal(filter.Fingerprint, AdvancedMailFilter.Compile(roundtrip).Fingerprint);
    }
    [Fact] public void UnicodeNormalizationPreservesTurkishDistinction()
    {
        Assert.Equal(MailFilterMatch.Match, Compile(Text("subject", "I\u0307stanbul")).Evaluate(Input));
        Assert.Equal(MailFilterMatch.NoMatch, Compile(Text("subject", "istanbul")).Evaluate(Input));
    }
    [Fact] public void InclusiveStartExclusiveEndHasExactUtcBoundary()
    {
        var date = Input.Date!.Value;
        Assert.Equal(MailFilterMatch.Match, Compile(new("condition", "date", "gte", Date: date)).Evaluate(Input));
        Assert.Equal(MailFilterMatch.NoMatch, Compile(new("condition", "date", "lt", Date: date)).Evaluate(Input));
        Assert.Equal(MailFilterMatch.Match, Compile(new("condition", "date", "eq", Date: date)).Evaluate(Input with { Date = date.ToOffset(TimeSpan.FromHours(3)) }));
    }
    [Fact] public void UnknownMetadataIsNotInvented()
    {
        var filter = Compile(new("condition", "size", "gte", Number: 1));
        Assert.Equal(MailFilterMatch.UnknownMetadata, filter.Evaluate(Input with { SizeBytes = null }));
        Assert.Equal(MailFilterMatch.NoMatch, filter.Evaluate(Input with { SizeBytes = 0 }));
    }
    [Fact] public void LogicalGroupsPropagateOnlyRelevantUnknowns()
    {
        var unknown = new MailFilterNode("condition", "date", "gte", Date: Input.Date);
        Assert.Equal(MailFilterMatch.Match, Compile(new("or", Children: [unknown, Text("subject", "bütçe")])).Evaluate(Input with { Date = null }));
        Assert.Equal(MailFilterMatch.NoMatch, Compile(new("and", Children: [unknown, Text("subject", "missing")])).Evaluate(Input with { Date = null }));
    }
    [Fact] public void EmptyAndIsAllMessagesButEmptyOrRejected()
    { Assert.Equal(MailFilterMatch.Match, Compile(new("and", Children: [])).Evaluate(Input)); Assert.Throws<InvalidDataException>(() => Compile(new("or", Children: []))); }
    [Fact] public void UnsupportedFieldsOrDateQualificationRejectBeforeSelection()
    {
        Assert.Throws<InvalidDataException>(() => AdvancedMailFilter.Compile(new(1, Text("body", "test")), new HashSet<string> { "subject" }));
        Assert.Throws<InvalidDataException>(() => AdvancedMailFilter.Compile(new(1, new("condition", "date", "gte", Date: Input.Date)), dateFidelityUnresolved: true));
    }
    [Fact] public void MutableClientListCannotChangeCompiledFilter()
    {
        var children = new List<MailFilterNode> { Text("subject", "bütçe") }; var filter = Compile(new("and", Children: children));
        string before = filter.Fingerprint; children[0] = Text("subject", "missing");
        Assert.Equal(MailFilterMatch.Match, filter.Evaluate(Input)); Assert.Equal(before, filter.Fingerprint);
    }
    [Fact] public void DepthAndNodeLimitsAreEnforced()
    {
        MailFilterNode node = Text("subject", "a"); for (int i = 0; i < 10; i++) node = new("and", Children: [node]);
        Assert.Throws<InvalidDataException>(() => Compile(node));
        var group = new MailFilterNode("or", Children: Enumerable.Range(0, 64).Select(_ => Text("subject", "a")).ToArray());
        Assert.Throws<InvalidDataException>(() => Compile(new("and", Children: [group, group, group, group])));
    }
    [Theory]
    [InlineData("delete")]
    [InlineData("regex")]
    public void UnknownOperatorCannotBeSilentlyIgnored(string op) => Assert.Throws<InvalidDataException>(() => Compile(new("condition", "subject", op, Text: "a")));
    [Fact] public void AmbiguousValuesAndNonUtcInputAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Compile(new("condition", "size", "gt", Number: 1, Text: "extra")));
        Assert.Throws<InvalidDataException>(() => Compile(new("condition", "date", "gte", Date: Input.Date!.Value.ToOffset(TimeSpan.FromHours(3)))));
    }
    [Fact] public void UnknownJsonOperatorPropertyCannotDisappearDuringBinding()
    {
        const string json = """{"Version":1,"Root":{"Kind":"and","Children":[],"Not":true}}""";
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<MailFilterDefinition>(json));
    }
}
