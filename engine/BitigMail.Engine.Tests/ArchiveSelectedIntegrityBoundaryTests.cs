using System.Text;
using System.Text.Json.Nodes;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.Engine.Planning;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class ArchiveSelectedIntegrityBoundaryTests
{
    private sealed class Fixture : IDisposable
    {
        public required string Root;
        public required ArchiveSearchIndex Index;
        public required JobManager Jobs;
        public required ArchiveSelectedJobService Service;
        public required ArchiveSearchRequest Search;
        public string Output => Path.Combine(Root, "output");
        public ArchiveSelectedPlan Preview() => Service.Preview(Search, ["a:shared", "b:shared"],
            [new("a:Inbox", "Mapped/A"), new("b:Inbox", "Mapped/B")], DuplicatePolicy.PreservePhysical, "c", "p");
        public void Dispose() => Index.Dispose();
    }

    private static async Task<Fixture> Seed()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-selected-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "output"));
        var storage = new ArchiveStorageManager(root);
        foreach (string archive in new[] { "a", "b" })
        {
            string staging = storage.CreateStagingDirectory("seed-" + archive);
            byte[] raw = Encoding.UTF8.GetBytes($"From: a@example.test\r\nTo: b@example.test\r\nSubject: {archive}\r\n\r\nbody {archive}");
            var item = await storage.StoreMimeItemAsync(staging, 1, "shared", "Inbox", new MemoryStream(raw), null, null, CancellationToken.None);
            var manifest = new ArchiveManifest
            {
                ArchiveId = archive, ArchiveName = archive, CompanyId = "c", ProjectId = "p", SourceKind = "eml", Dialect = "eml",
                SourceFingerprint = new string('a', 64), TotalItems = 1, TotalSizeBytes = raw.Length, Items = [item],
                Folders = [new() { FolderName = "Inbox", ItemCount = 1, TotalSizeBytes = raw.Length }]
            };
            await storage.WriteManifestAsync(staging, manifest, CancellationToken.None); storage.PublishStagingToManagedArchive(staging, archive);
        }
        var index = new ArchiveSearchIndex(root); await index.RebuildDatabaseAsync(storage, CancellationToken.None);
        var jobs = new JobManager(Path.Combine(root, "jobs"));
        var catalog = new ArchiveCatalogService(storage, index, new ArchivePlanStore(root), new FileHandleRegistry(), jobs);
        return new() { Root = root, Index = index, Jobs = jobs, Service = new(catalog, jobs), Search = new()
        { SelectedScopes = [new() { ArchiveId = "a", CompanyId = "c", ProjectId = "p" }, new() { ArchiveId = "b", CompanyId = "c", ProjectId = "p" }] } };
    }

    private static async Task<LocalJobRecord> Wait(JobManager jobs, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var job = jobs.GetJob(id)!;
            if (job.Status is "completed" or "failed") return job;
            await Task.Delay(20, timeout.Token);
        }
    }

    [Fact]
    public async Task TwoArchivesWithSameItemIdRemainDistinctAndCannotImplicitlyMergeFolders()
    {
        using var f = await Seed();
        Assert.Throws<InvalidDataException>(() => f.Service.Preview(f.Search, ["a:shared", "b:shared"],
            [new("a:Inbox", "Same"), new("b:Inbox", "Same")], DuplicatePolicy.PreservePhysical, "c", "p"));
        var plan = f.Preview();
        var job = await Wait(f.Jobs, f.Service.Start(plan.PlanId, f.Output, new() { CompanyId = "c", ProjectId = "p" }).JobId);
        Assert.Equal("completed", job.Status); Assert.Equal(2, job.ItemsWritten);
        string[] files = Directory.GetFiles(job.OutputDirectoryPath!, "*.eml", SearchOption.AllDirectories);
        Assert.Equal(2, files.Length); Assert.Equal(2, files.Select(Path.GetFileName).Distinct().Count());
        Assert.Contains(files, p => File.ReadAllText(p).Contains("body a")); Assert.Contains(files, p => File.ReadAllText(p).Contains("body b"));
    }

    [Theory]
    [InlineData("query")]
    [InlineData("qualification")]
    [InlineData("raw")]
    public async Task MutationWhileQueuedCannotPublish(string mutation)
    {
        using var f = await Seed(); var plan = f.Preview();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Jobs.ScheduleStage7Job("test-hold", "fixture", "fixture", new(), async r => { await release.Task; r.Status = "completed"; f.Jobs.UpdateRecovery(r); });
        var job = f.Service.Start(plan.PlanId, f.Output, new() { CompanyId = "c", ProjectId = "p" });
        try
        {
            Assert.Equal("queued", job.Status);
            if (mutation == "query") plan.SearchRequest.SelectedScopes.Clear();
            else if (mutation == "qualification")
            {
                string path = Path.Combine(f.Root, "archives", "a", "manifest.json");
                var json = JsonNode.Parse(File.ReadAllText(path))!; json["dateFilterBlocked"] = true;
                File.WriteAllText(path, json.ToJsonString());
            }
            else
            {
                string path = Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "archives", "a"), "*.eml", SearchOption.AllDirectories));
                File.AppendAllText(path, " changed");
            }
        }
        finally { release.TrySetResult(); }
        job = await Wait(f.Jobs, job.JobId);
        Assert.Equal("failed", job.Status); Assert.False(Directory.Exists(Path.Combine(f.Output, job.JobId)));
    }
}
