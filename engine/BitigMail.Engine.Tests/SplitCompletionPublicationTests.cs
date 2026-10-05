using System.Text.Json;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public class SplitCompletionPublicationTests
{
    [Theory]
    [InlineData("lab/ost-spike/output/genuine-full-converted-04.pst")]
    [InlineData("lab/ost-spike/input/bitigmail-lab-full.ost")]
    public void Split_PreservesEachPhysicalMessageFolderWithoutRepeatingPathSegments(string relativeSource)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, relativeSource))) root = root.Parent;
        Assert.NotNull(root);
        string source = Path.Combine(root.FullName, relativeSource);
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-task012-folders", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var analyzer = new OstAnalyzer();
        var original = analyzer.AnalyzeSplitSource(source);
        var report = new PstSplitter().Split(source, output, "job-folder-proof", new ClientProjectContext(),
            new RegisteredSplitPlan { PlanId = "folder-proof", SourceSha256 = original.SourceSha256,
                SplitMode = SplitOptions.ModeYear, CanSplit = true });
        var expected = original.Folders.Where(f => f.ItemCount > 0)
            .ToDictionary(f => f.FolderPath, f => f.ItemCount, StringComparer.Ordinal);
        var actual = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var part in report.Parts)
            foreach (var folder in analyzer.AnalyzeSplitSource(part.PartFullPath).Folders.Where(f => f.ItemCount > 0))
                actual[folder.FolderPath] = actual.GetValueOrDefault(folder.FolderPath) + folder.ItemCount;
        Assert.Equal(expected.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(13, actual.Values.Sum());
        Assert.Equal("PST_OST_SPLIT (Local-Engine Reopen Verification)", report.EvidenceLabel);
        var summary = report.ReopenedPstVerification;
        Assert.True(summary.VerificationSuccess);
        Assert.True(summary.ItemCountMatch);
        Assert.Equal(13, summary.TotalPhysicalItemsFound);
        Assert.Equal(4, summary.TotalAttachmentsVerified);
        Assert.Equal(1, summary.TotalCidVerified);
        Assert.Equal(report.Parts.Sum(p => p.ReopenedPstVerification.TotalPhysicalItemsFound), summary.TotalPhysicalItemsFound);
        Assert.Equal(report.Parts.Sum(p => p.ReopenedPstVerification.TotalAttachmentsVerified), summary.TotalAttachmentsVerified);
        Assert.Equal(report.Parts.Sum(p => p.ReopenedPstVerification.TotalCidVerified), summary.TotalCidVerified);
    }

    [Theory]
    [InlineData("report")]
    [InlineData("final-job")]
    public void Split_RemainsActiveUntilReportAndFinalJobArePersisted(string faultBoundary)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,
            "lab", "ost-spike", "output", "genuine-full-converted-04.pst")))
            root = root.Parent;
        Assert.NotNull(root);
        string source = Path.Combine(root.FullName, "lab", "ost-spike", "output", "genuine-full-converted-04.pst");
        string runtime = Path.Combine(Path.GetTempPath(), "bitigmail-task012-completion", Guid.NewGuid().ToString("N"));
        string output = Path.Combine(runtime, "output");
        Directory.CreateDirectory(output);
        var manager = new JobManager(runtime);
        var analysis = new OstAnalyzer().AnalyzeSplitSource(source);
        var plan = new RegisteredSplitPlan
        {
            PlanId = "completion-proof", SourceSha256 = analysis.SourceSha256,
            SplitMode = SplitOptions.ModeYear, CanSplit = true
        };
        var client = new ClientProjectContext { CompanyId = "completion-qa", ProjectId = "persistence" };
        Exception? observationFailure = null;
        bool observed = false;

        void Observe(string jobId)
        {
            observed = true;
            try
            {
                var live = manager.GetJob(jobId);
                Assert.NotNull(live);
                Assert.Equal("verifying", live.Status);
                Assert.Null(live.CompletedAt);
                Assert.True(live.PercentComplete < 100);
                Assert.True(Directory.Exists(live.OutputDirectoryPath));
                Assert.Equal(5, live.Parts.Count);
                Assert.Throws<InvalidOperationException>(() => manager.StartSplitJob(
                    source, output, "second-split", client, plan));
                Assert.Throws<InvalidOperationException>(() => manager.StartJob(
                    source, Path.Combine(output, "second-conversion.pst"), "second-convert",
                    client, analysis.SourceSha256, 13));
                using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(runtime, "jobs", jobId + ".json")));
                Assert.NotEqual("completed", saved.RootElement.GetProperty("status").GetString());
            }
            catch (Exception ex)
            {
                observationFailure = ex;
                throw;
            }
        }

        if (faultBoundary == "report")
            manager.OnBeforeSaveReport = report => Observe(report.JobId);
        else
            manager.OnBeforeSaveJobRecord = record =>
            {
                if (record.Status == "completed") Observe(record.JobId);
            };

        var job = manager.StartSplitJob(source, output, "first", client, plan);
        Assert.True(SpinWait.SpinUntil(() =>
            manager.GetJob(job.JobId)?.Status is "completed" or "failed" or "interrupted"
            && manager.ActiveRunningJobId == null, TimeSpan.FromSeconds(20)), "Job did not finish within 20 seconds.");
        if (observationFailure != null) throw observationFailure;
        Assert.True(observed, "Persistence boundary was never exercised.");
        Assert.Equal("completed", manager.GetJob(job.JobId)!.Status);
        using var final = JsonDocument.Parse(File.ReadAllText(Path.Combine(runtime, "jobs", job.JobId + ".json")));
        Assert.Equal("completed", final.RootElement.GetProperty("status").GetString());
        Assert.True(File.Exists(Path.Combine(runtime, "reports", job.JobId + ".json")));
        // Keep the unique task-owned output as fault/publication evidence. Never clean shared temp directories.
    }
}
