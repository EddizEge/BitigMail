using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using MailKit;
using MimeKit;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed partial class ImapTransferTests
{
    [Theory]
    [InlineData("subject", "chosen", 1)]
    [InlineData("body", "Visible & decoded", 1)]
    [InlineData("body", "hidden-script-value", 0)]
    public async Task AdvancedPreviewAndWorkerUseSameCompleteMime(string field, string text, int expected)
    {
        var dir = CreateTestDir();
        var (store, source, target) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir);
        var client = new FakeImapTransferClient();
        var chosen = CreateMime("chosen");
        chosen.Body = new TextPart("html") { Text = "<html><head><script>hidden-script-value</script></head><body><p>Visible &amp; decoded</p></body></html>" };
        var other = CreateMime("other");
        foreach (var (message, uid) in new[] { (chosen, 1u), (other, 2u) })
            client.AddSourceMessage("INBOX", uid, ImapSerializationAssumptions.Serialize(message), message, DateTimeOffset.Parse("2024-01-01T07:00:00Z"), DateTimeOffset.Parse("2024-01-01T08:00:00Z"), MessageFlags.Seen, []);
        var service = new ImapTransferPreviewService(store, new FakeTransferClientFactory(client), journal,
            new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var filter = new MailFilterDefinition(1, new("condition", field, "contains", Text: text));
        var preview = await service.CreatePreviewAsync(new()
        {
            CompanyId = "comp_test", ProjectId = "proj_test", SourceAccountId = source, TargetAccountId = target,
            SelectedFolders = [new() { SourceFolderPath = "INBOX", TargetFolderPath = "Filtered" }], AdvancedFilter = filter
        });
        Assert.Equal(expected, preview.EligibleItemsCount);
        Assert.Equal(2 - expected, preview.ExcludedCount);
        Assert.Equal(AdvancedMailFilter.Compile(filter).Fingerprint, preview.AdvancedFilterFingerprint);
        var plan = journal.GetPlan(preview.PreviewId)!;
        Assert.Equal(expected, plan.Items.Count);
        if (expected == 0) { Assert.False(preview.CanTransfer); return; }
        var job = new LocalJobRecord { JobId = "filter-job", JobKind = "imap-transfer" };
        ConversionReport? report = null;
        await new ImapTransferWorker(store, new FakeTransferClientFactory(client), journal, _ => { }, r => report = r).ExecuteAsync(job, plan);
        Assert.True(report!.IsFiltered);
        Assert.Equal(preview.AdvancedFilterFingerprint, report.ImapTransfer!.AdvancedFilterFingerprint);
        Assert.Equal("completed", job.Status);
        Assert.Single(client.TargetAppendedMessages);
        Assert.Equal("chosen", client.TargetAppendedMessages[0].Message.Subject);
        Assert.Equal(preview.AdvancedFilterFingerprint, job.AdvancedFilterFingerprint);
    }

    [Fact]
    public async Task UnknownMimeDateIsCountedWithoutUsingServerInternalDate()
    {
        var dir = CreateTestDir(); var (store, source, target) = SetupTestAccounts(dir);
        var journal = new ImapTransferJournal(dir); var client = new FakeImapTransferClient();
        var mime = CreateMime("undated", null);
        client.AddSourceMessage("INBOX", 1, ImapSerializationAssumptions.Serialize(mime), mime, null, DateTimeOffset.UtcNow, MessageFlags.None, []);
        var service = new ImapTransferPreviewService(store, new FakeTransferClientFactory(client), journal,
            new ImapCredentialResolver(store, new WindowsImapCredentialProtector(), new MsalMicrosoftAuthProvider()));
        var preview = await service.CreatePreviewAsync(new()
        {
            CompanyId = "comp_test", ProjectId = "proj_test", SourceAccountId = source, TargetAccountId = target,
            SelectedFolders = [new() { SourceFolderPath = "INBOX", TargetFolderPath = "Filtered" }],
            AdvancedFilter = new(1, new("condition", "date", "gte", Date: DateTimeOffset.Parse("2020-01-01T00:00:00Z")))
        });
        Assert.Equal(1, preview.AdvancedFilterUnknownCount);
        Assert.Equal(0, preview.EligibleItemsCount);
        Assert.Equal(1, journal.GetPlan(preview.PreviewId)!.AdvancedFilterUnknownCount);
    }

    [Fact]
    public async Task AlteredFrozenFilterFailsBeforeAnyAppend()
    {
        var dir = CreateTestDir(); var (store, source, target) = SetupTestAccounts(dir);
        var compiled = AdvancedMailFilter.Compile(new(1, new("condition", "subject", "contains", Text: "original")));
        var plan = new ImapTransferPlan
        {
            PlanId = "frozen", PreviewId = "frozen", CompanyId = "comp_test", ProjectId = "proj_test",
            SourceAccountId = source, TargetAccountId = target, SourceAccountVersion = 1, TargetAccountVersion = 1,
            AdvancedFilterCanonicalJson = compiled.CanonicalJson.Replace("original", "changed"), AdvancedFilterFingerprint = compiled.Fingerprint
        };
        var journal = new ImapTransferJournal(dir);
        Assert.Throws<InvalidDataException>(() => journal.SavePlan(plan));
        var client = new FakeImapTransferClient();
        var job = new LocalJobRecord { JobId = "tamper-job", JobKind = "imap-transfer" };
        await new ImapTransferWorker(store, new FakeTransferClientFactory(client), journal, _ => { }, _ => { }).ExecuteAsync(job, plan);
        Assert.Equal("failed", job.Status); Assert.Empty(client.TargetAppendedMessages);
    }
}
