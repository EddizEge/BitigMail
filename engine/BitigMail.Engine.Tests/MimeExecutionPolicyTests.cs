using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;
using BitigMail.Engine.Planning;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Jobs;
using MimeKit;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class MimeExecutionPolicyTests
{
    private static (string Root, MimeSourceManifest Manifest, MimeAnalysisResult Analysis, FolderMappingRule[] Rules) Fixture()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-mime-policy-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"); Directory.CreateDirectory(Path.Combine(source, "Inbox")); Directory.CreateDirectory(Path.Combine(source, "Sent"));
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("a@example.test")); message.To.Add(MailboxAddress.Parse("b@example.test"));
        message.Subject = "same physical content"; message.Date = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
        var body = new BodyBuilder { TextBody = "original body" }; body.Attachments.Add("sample.bin", new byte[] { 1, 2, 3 }); message.Body = body.ToMessageBody();
        string first = Path.Combine(source, "Inbox", "a.eml"); message.WriteTo(first);
        File.Copy(first, Path.Combine(source, "Inbox", "b.eml")); File.Copy(first, Path.Combine(source, "Sent", "c.eml"));
        var inspector = new MimeSourceInspector(); var manifest = inspector.BuildEmlDirectoryManifest(source); var analysis = inspector.Analyze(manifest);
        var rules = analysis.Folders.Select(f => new FolderMappingRule(f.FolderId, f.FolderPath == "Inbox" ? "Müşteri/Gelen" : "Müşteri/Giden")).ToArray();
        return (root, manifest, analysis, rules);
    }

    [Theory]
    [InlineData(DuplicatePolicy.PreservePhysical, 3)]
    [InlineData(DuplicatePolicy.ContentOnly, 1)]
    [InlineData(DuplicatePolicy.ContentAndMetadata, 2)]
    public void PreviewPolicyProducesMappedReopenedPst(DuplicatePolicy policy, int expected)
    {
        var f = Fixture();
        var (preview, selected) = MimeSelectionEngine.EvaluateSelection(f.Manifest, "source", null, null, null,
            f.Analysis.Preflight, CancellationToken.None, folderMappings: f.Rules, duplicatePolicy: policy);
        Assert.Equal(expected, preview.SelectedMessagesCount); Assert.Equal(expected, preview.SelectedAttachmentsCount);
        Assert.Equal(3 - expected, preview.SkippedDuplicateItemIds.Count);
        var manager = new JobManager(Path.Combine(f.Root, "runtime"));
        string output = Path.Combine(f.Root, "mapped.pst");
        var job = manager.StartMimeJob(f.Manifest, output, "mapping-test", new(), selected, runInBackground: false);
        Assert.True(job.Status == "completed", job.ErrorMessage);
        var report = manager.GetReport(job.JobId)!;
        Assert.Equal(expected, report.ItemsWritten); Assert.Equal(expected, report.ReopenedPstVerification.TotalPhysicalItemsFound);
        Assert.Equal(preview.ExecutionPolicyFingerprint, report.MimeImport!.ExecutionPolicyFingerprint);
        Assert.Equal(3 - expected, report.MimeImport.SkippedDuplicateItemIds.Count);
        Assert.Equal(policy.ToString(), manager.GetJob(job.JobId)!.DuplicatePolicy);
        using var pst = PersonalStorage.FromFile(output);
        var customer = pst.RootFolder.GetSubFolder("Müşteri"); Assert.NotNull(customer);
        Assert.Equal(policy == DuplicatePolicy.PreservePhysical ? 2 : 1, customer.GetSubFolder("Gelen").ContentCount);
        if (policy != DuplicatePolicy.ContentOnly) Assert.Equal(1, customer.GetSubFolder("Giden").ContentCount);
        Assert.Null(pst.RootFolder.GetSubFolder("Inbox")); Assert.Null(pst.RootFolder.GetSubFolder("Sent"));
    }

    [Fact]
    public void AlteredMappingOrPhysicalSetCannotExecute()
    {
        var f = Fixture();
        var (_, selected) = MimeSelectionEngine.EvaluateSelection(f.Manifest, "source", null, null, null,
            f.Analysis.Preflight, CancellationToken.None, folderMappings: f.Rules, duplicatePolicy: DuplicatePolicy.ContentOnly);
        var frozen = selected.ExecutionPolicy!;
        selected.ExecutionPolicy = frozen with { Mappings = [new(f.Rules[0].SourceFolderId, "Different")] };
        string output = Path.Combine(f.Root, "refused.pst");
        Assert.Throws<InvalidDataException>(() => new MimeToPstConverter().Convert(f.Manifest, output, "tamper", new(), selection: selected));
        Assert.False(File.Exists(output));
        selected.ExecutionPolicy = frozen; selected.SelectedMessageKeys.Add("3");
        Assert.Throws<InvalidDataException>(() => MimeExecutionPolicy.Validate(f.Manifest, selected));
    }

    [Fact]
    public void MappingCollisionWithUnmappedFolderRejected()
    {
        var f = Fixture();
        var inbox = f.Analysis.Folders.Single(x => x.FolderPath == "Inbox");
        Assert.Throws<InvalidDataException>(() => MimeSelectionEngine.EvaluateSelection(f.Manifest, "source", null, null, null,
            f.Analysis.Preflight, CancellationToken.None, folderMappings: [new(inbox.FolderId, "Sent")]));
    }

    [Fact]
    public async Task QueuedJobOwnsItsSelectionAndManifestSnapshot()
    {
        var f = Fixture();
        var (_, selected) = MimeSelectionEngine.EvaluateSelection(f.Manifest, "source", null, null, null,
            f.Analysis.Preflight, CancellationToken.None, folderMappings: f.Rules, duplicatePolicy: DuplicatePolicy.ContentOnly);
        var manager = new JobManager(Path.Combine(f.Root, "runtime"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ScheduleStage7Job("test-hold", "fixture", "fixture", new(), async record =>
        {
            await release.Task;
            record.Status = "completed"; manager.UpdateRecovery(record);
        });
        var job = manager.StartMimeJob(f.Manifest, Path.Combine(f.Root, "queued.pst"), "queued", new(), selected, enqueueIfBusy: true);
        try
        {
            Assert.Equal("queued", job.Status);
            selected.SelectedMessageKeys.Clear(); selected.ExecutionPolicy = null;
            f.Manifest.Entries.Clear();
        }
        finally { release.TrySetResult(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        LocalJobRecord? current;
        do { await Task.Delay(25, timeout.Token); current = manager.GetJob(job.JobId); }
        while (current?.Status is "queued" or "converting" or "verifying");
        Assert.True(current?.Status == "completed", current?.ErrorMessage);
        Assert.Equal(1, current!.ItemsWritten);
    }
}
