using System.Text;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public class MimeCriticalBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PreviewThenStart_NullOrEmptyMeansAll_ExplicitFolderRemainsExact(int mode)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bitigmail-task013-root-tests", Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(directory, "source");
        foreach (string folder in new[] { "Inbox", "Sent" })
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, folder));
            File.WriteAllText(Path.Combine(sourceRoot, folder, "mail.eml"),
                $"From: a@test.local\r\nTo: b@test.local\r\nSubject: {folder}\r\nDate: Mon, 01 Jan 2024 12:00:00 +0300\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nOriginal {folder}", new UTF8Encoding(false));
        }
        var inspector = new MimeSourceInspector();
        var manifest = inspector.BuildEmlDirectoryManifest(sourceRoot);
        var analysis = inspector.Analyze(manifest);
        string inboxId = analysis.Folders.Single(f => f.FolderPath == "Inbox").FolderId;
        List<string>? requested = mode == 0 ? null : mode == 1 ? new() : new() { inboxId };
        var (preview, selection) = MimeSelectionEngine.EvaluateSelection(manifest, "test", requested, null, null, analysis.Preflight, CancellationToken.None);
        int expected = mode == 2 ? 1 : 2;
        Assert.Equal(expected, preview.SelectedMessagesCount);
        Assert.NotNull(preview.Filters.FolderIds);
        Assert.Equal("OriginalMimeDate_ExplicitZone_UtcPlus3_Inclusive", preview.Filters.DatePolicy);
        Assert.Equal(expected, preview.Filters.SelectedFolders.Count);
        Assert.Equal(expected, selection.SelectedFolderIds.Count);
        var manager = new JobManager(Path.Combine(directory, "runtime"));
        var job = manager.StartMimeJob(manifest, Path.Combine(directory, "out.pst"), "key", new ClientProjectContext(), selection, runInBackground: false);
        Assert.Equal("completed", job.Status);
        Assert.Equal(mode == 2, job.IsFiltered);
        Assert.Equal(mode == 2, manager.GetReport(job.JobId)!.IsFiltered);
        Assert.Equal(expected, job.ItemsWritten);
        Assert.Equal(expected, manager.GetReport(job.JobId)!.ReopenedPstVerification.TotalPhysicalItemsFound);
        Assert.NotNull(manager.GetJob(job.JobId)!.SelectionFilter!.FolderIds);
        if (mode != 2)
        {
            // An older persisted record can still contain the historical null list.
            job.SelectionFilter!.FolderIds = null!;
            string json = System.Text.Json.JsonSerializer.Serialize(job, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            File.WriteAllText(Path.Combine(manager.RuntimeDirectory, "jobs", job.JobId + ".json"), json);
            var restarted = new JobManager(manager.RuntimeDirectory);
            Assert.Empty(restarted.GetJob(job.JobId)!.SelectionFilter!.FolderIds);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MimeTerminalPersistenceFailure_NeverExposesCompleted_AndRetainsPublishedPath(bool failJobRecord)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bitigmail-task013-root-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.eml");
        File.WriteAllText(source, "From: a@test.local\r\nTo: b@test.local\r\nSubject: Persistence probe\r\nDate: Mon, 01 Jan 2024 12:00:00 +0300\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nOriginal body", new UTF8Encoding(false));
        var inspector = new MimeSourceInspector();
        var manifest = inspector.BuildEmlFilesManifest(new[] { source }, "Corpus");
        var manager = new JobManager(Path.Combine(directory, "runtime"));
        bool injected = false;
        void Fail(string id)
        {
            injected = true;
            Assert.NotEqual("completed", manager.GetJob(id)!.Status);
            Assert.Equal(id, manager.ActiveRunningJobId);
            Assert.Throws<InvalidOperationException>(() => manager.StartMimeJob(manifest, Path.Combine(directory, "other.pst"), "other", new ClientProjectContext(), runInBackground: false));
            throw new IOException("intentional terminal persistence failure");
        }
        if (failJobRecord) manager.OnBeforeSaveJobRecord = record => { if (record.Status == "completed") Fail(record.JobId); };
        else manager.OnBeforeSaveReport = report => Fail(report.JobId);
        string target = Path.Combine(directory, "output.pst");
        var job = manager.StartMimeJob(manifest, target, "first", new ClientProjectContext(), runInBackground: false);
        Assert.True(injected);
        Assert.Equal("failed", manager.GetJob(job.JobId)!.Status);
        Assert.Equal(target, manager.GetJob(job.JobId)!.OutputPath);
        Assert.True(File.Exists(target));
        Assert.Null(manager.ActiveRunningJobId);
    }

    [Fact]
    public void VendorQualification_PreservesSourceMarker_AndRejectsUnmeasuredHtmlAdditions()
    {
        var profile = MimeVendorCalibrator.GetCalibration();
        const string plain = "User Evaluation Only. Created with Aspose.Email. text\n";
        Assert.Equal(plain, MimeFidelityPolicy.QualifyPlain(plain, plain[..^1], profile, out bool restored));
        Assert.True(restored);
        const string html = "<p>Original</p>";
        Assert.Equal(profile.HtmlPrefix + html + profile.HtmlSuffix,
            MimeFidelityPolicy.QualifyHtml(html, profile.HtmlPrefix + html + profile.HtmlSuffix, profile, out _));
        Assert.Throws<InvalidOperationException>(() => MimeFidelityPolicy.QualifyHtml(html,
            profile.HtmlPrefix + "<p>unmeasured inserted text</p>" + html + profile.HtmlSuffix, profile, out _));
        Assert.Throws<InvalidOperationException>(() => MimeFidelityPolicy.QualifyHtml(html,
            "<html><body>arbitrary prefix" + html + "</body></html>", profile, out _));
    }

    [Fact]
    public void ReopenedFolderCounts_SeparateActiveAndEmptySystemFolders()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bitigmail-task013-root-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.eml");
        File.WriteAllText(source, "From: a@test.local\r\nTo: b@test.local\r\nSubject: Folder count\r\nDate: Mon, 01 Jan 2024 12:00:00 +0300\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nOriginal", new UTF8Encoding(false));
        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { source }, "Corpus");
        var report = new MimeToPstConverter().Convert(manifest, Path.Combine(directory, "out.pst"), "count", new ClientProjectContext());
        var counts = report.ReopenedPstVerification;
        Assert.Equal(1, counts.ActiveFoldersFound);
        Assert.Equal(0, counts.EmptyFoldersFound);
        Assert.Equal(1, counts.SystemFoldersFound);
        Assert.Equal(counts.ActiveFoldersFound + counts.EmptyFoldersFound + counts.SystemFoldersFound, counts.TotalFoldersFound);
    }
}
