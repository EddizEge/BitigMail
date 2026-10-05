using System.Security.Cryptography;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Bridge.Transfer;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using MailKit;
using MimeKit;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class BridgeAdvancedFilterTests
{
    private static MailFilterDefinition Filter => new(1, new("condition", "body", "contains", Text: "late needle"));
    private static ClientProjectContext Owner => new() { CompanyId = "company-1", ProjectId = "project-1" };

    [Fact]
    public async Task ImportFiltersFullBodyAndCopiesOnlyFrozenMatch()
    {
        string dir = BridgeTestHelpers.CreateTestDir("advanced-import");
        var (store, _, target, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handles = new FileHandleRegistry(); var journal = new BridgeTransferJournal(dir);
        var client = new BridgeFakeTransferClient(); var factory = new BridgeFakeClientFactory(client);
        string source = Path.Combine(dir, "source"); string inbox = Path.Combine(source, "Inbox"); Directory.CreateDirectory(inbox);
        string selected = BridgeTestHelpers.CreateEmlFile(inbox, "yes.eml", "chosen", body: new string('x', 300_000) + " late needle");
        BridgeTestHelpers.CreateEmlFile(inbox, "no.eml", "excluded", body: "different body");
        var manifest = new MimeSourceInspector().BuildEmlDirectoryManifest(source);
        string handle = handles.RegisterMimeSource(manifest, "fixture");
        var preview = await new BridgeImportPreviewService(handles, store, factory, journal, resolver).CreatePreviewAsync(new()
        {
            CompanyId = "company-1", ProjectId = "project-1", SourceHandle = handle, TargetAccountId = target,
            SelectedFolders = ["Inbox"], AdvancedFilter = Filter
        });
        Assert.True(preview.CanTransfer); Assert.Equal(1, preview.EligibleItemsCount); Assert.Equal(1, preview.ExcludedCount);
        var plan = journal.GetImportPlan(preview.PreviewId)!;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(selected))).ToLowerInvariant(), Assert.Single(plan.Items).SourceSha256);
        var job = new LocalJobRecord { JobId = "filtered-import", JobKind = "bridge-import", ClientContext = Owner };
        ConversionReport? report = null;
        await new BridgeImportWorker(handles, store, factory, journal, _ => { }, r => report = r, resolver).ExecuteAsync(job, plan);
        Assert.Equal("completed", job.Status); Assert.Equal("chosen", Assert.Single(client.AppendedMessages).Message.Subject);
        Assert.True(report!.IsFiltered); Assert.Equal(preview.AdvancedFilterFingerprint, report.BridgeTransfer!.AdvancedFilterFingerprint);
        // Qualification may forbid dates even if the generated EML has a parseable Date header.
        var date = AdvancedMailFilter.Compile(new(1, new("condition", "date", "gte", Date: DateTimeOffset.Parse("2020-01-01T00:00:00Z"))));
        plan.AdvancedFilterCanonicalJson = date.CanonicalJson; plan.AdvancedFilterFingerprint = date.Fingerprint; plan.DateFilterBlocked = true;
        Assert.Throws<InvalidDataException>(() => journal.SaveImportPlan(plan));
    }

    [Theory]
    [InlineData("eml-tree")]
    [InlineData("mboxrd")]
    public async Task ExportFiltersAndReopensActualOutput(string format)
    {
        string dir = BridgeTestHelpers.CreateTestDir("advanced-export");
        var (store, source, _, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handles = new FileHandleRegistry(); var journal = new BridgeTransferJournal(dir);
        var client = new BridgeFakeTransferClient(); var factory = new BridgeFakeClientFactory(client);
        string output = Path.Combine(dir, "output"); Directory.CreateDirectory(output);
        byte[]? expectedBytes = null;
        for (uint id = 1; id <= 2; id++)
        {
            var message = new MimeMessage(); message.From.Add(MailboxAddress.Parse("a@example.test")); message.To.Add(MailboxAddress.Parse("b@example.test"));
            message.Date = DateTimeOffset.Parse("2024-01-01T00:00:00Z"); message.Subject = id == 1 ? "chosen" : "excluded";
            message.Body = new TextPart("plain") { Text = id == 1 ? "late needle\r\n" : "other\r\n" };
            var bytes = ImapSerializationAssumptions.Serialize(message); if (id == 1) expectedBytes = bytes;
            client.AddSourceMessage("INBOX", id, bytes, message, message.Date, message.Date, MessageFlags.Seen);
        }
        var preview = await new BridgeExportPreviewService(handles, store, factory, journal, resolver).CreatePreviewAsync(new()
        {
            CompanyId = "company-1", ProjectId = "project-1", SourceAccountId = source, TargetDirHandle = handles.RegisterOutputDir(output),
            TargetFormat = format, SelectedFolders = ["INBOX"], AdvancedFilter = Filter
        });
        Assert.True(preview.CanTransfer); Assert.Equal(1, preview.EligibleItemsCount); Assert.Equal(1, preview.ExcludedCount);
        var plan = journal.GetExportPlan(preview.PreviewId)!;
        var job = new LocalJobRecord { JobId = "filtered-export", JobKind = "bridge-export", ClientContext = Owner };
        ConversionReport? report = null;
        await new BridgeExportWorker(handles, store, factory, journal, _ => { }, r => report = r, resolver).ExecuteAsync(job, plan);
        Assert.Equal("completed", job.Status); Assert.True(report!.IsFiltered);
        Assert.Equal(preview.AdvancedFilterFingerprint, report.BridgeTransfer!.AdvancedFilterFingerprint);
        byte[] actual;
        if (format == "eml-tree") actual = File.ReadAllBytes(Assert.Single(Directory.GetFiles(job.OutputDirectoryPath!, "*.eml", SearchOption.AllDirectories)));
        else
        {
            using var stream = File.OpenRead(Assert.Single(Directory.GetFiles(job.OutputDirectoryPath!, "*.mbox", SearchOption.AllDirectories)));
            actual = Assert.Single(MboxrdRecordReader.EnumerateRecords(stream)).RawMimeBytes;
        }
        Assert.Equal(expectedBytes, actual);
        using var reopened = MimeMessage.Load(new MemoryStream(actual)); Assert.Equal("chosen", reopened.Subject);
        plan.AdvancedFilterCanonicalJson = plan.AdvancedFilterCanonicalJson!.Replace("late needle", "different");
        Assert.Throws<InvalidDataException>(() => journal.SaveExportPlan(plan));
    }
}
