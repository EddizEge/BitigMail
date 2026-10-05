using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class LocalOutputDiskCapacityTests
{
    private sealed class FixedProbe : IDiskCapacityProbe
    {
        private readonly DiskCapacityResult _result;
        public FixedProbe(long bytes) => _result = new(true, bytes, null);
        public FixedProbe(string error) => _result = new(false, null, error);
        public DiskCapacityResult Probe(string directoryPath) => _result;
    }

    [Fact]
    public void Planning_Formulas_AreCheckedNonnegativeAndIncludeSplitOverhead()
    {
        long pst = DiskCapacityPlanning.EstimatePst(1_000, 2);
        Assert.Equal((4 * 1_000) + (2 * 65_536L) + 268_435_456L, pst);
        Assert.Equal(pst + (3 * 1_048_576L), DiskCapacityPlanning.EstimateSplit(1_000, 2, 3));
        Assert.Equal(pst, DiskCapacityPlanning.EstimateArchive(1_000, 2));
        Assert.Equal(3, DiskCapacityPlanning.EstimateSizeSplitParts(1_000, 800));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiskCapacityPlanning.EstimatePst(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiskCapacityPlanning.EstimateSplit(1, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiskCapacityPlanning.EstimateSizeSplitParts(1, 0));
        Assert.Throws<OverflowException>(() => DiskCapacityPlanning.EstimatePst(long.MaxValue, 1));
    }

    [Fact]
    public void OstToPst_InsufficientCapacity_PreventsOutput()
    {
        string source = ResolveRepoPath("lab", "ost-spike", "input", "bitigmail-lab-full.ost");
        string dir = BridgeTestHelpers.CreateTestDir("ost-capacity-block");
        string target = Path.Combine(dir, "blocked.pst");
        var converter = new OstToPstConverter(new FixedProbe(0));

        var ex = Assert.Throws<InvalidOperationException>(() => converter.Convert(
            source, target, "capacity-ost", new ClientProjectContext()));

        Assert.Contains("DİSK KAPASİTESİ ENGELİ", ex.Message);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(dir, "*.partial"));
    }

    [Fact]
    public void MimeToPst_UnavailableCapacity_PreventsOutput()
    {
        string dir = BridgeTestHelpers.CreateTestDir("mime-capacity-block");
        string eml = BridgeTestHelpers.CreateEmlFile(dir, "one.eml", "one");
        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { eml });
        string target = Path.Combine(dir, "blocked.pst");
        var converter = new MimeToPstConverter(new FixedProbe("probe unavailable"));

        var ex = Assert.Throws<InvalidOperationException>(() => converter.Convert(
            manifest, target, "capacity-mime", new ClientProjectContext()));

        Assert.Contains("DİSK KAPASİTESİ ENGELİ", ex.Message);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".capacity-mime.partial"));
    }

    [Fact]
    public void Split_InsufficientCapacity_PreventsPartialAndFinalDirectories()
    {
        string dir = BridgeTestHelpers.CreateTestDir("split-capacity-block");
        string output = Path.Combine(dir, "output");
        Directory.CreateDirectory(output);
        var fixture = SplitFixtureBuilder.BuildNineMessageFixture(dir);
        var analysis = new OstAnalyzer().AnalyzeSplitSource(fixture.PstPath);
        var plan = new RegisteredSplitPlan
        {
            PlanId = "capacity-split-plan", SourceHandle = "src", SourceSha256 = analysis.SourceSha256,
            SplitMode = SplitOptions.ModeYear, CanSplit = true
        };
        var splitter = new PstSplitter(new FixedProbe(0));

        var ex = Assert.Throws<InvalidOperationException>(() => splitter.Split(
            fixture.PstPath, output, "capacity-split", new ClientProjectContext(), plan));

        Assert.Contains("DİSK KAPASİTESİ ENGELİ", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(output, "arsiv-capacity-split.partial")));
        Assert.False(Directory.Exists(Path.Combine(output, "arsiv-capacity-split")));
    }

    [Fact]
    public async Task Archive_CapacityDrop_PreventsArchiveMutation()
    {
        string runtime = BridgeTestHelpers.CreateTestDir("archive-capacity-block");
        var storage = new ArchiveStorageManager(runtime);
        using var index = new ArchiveSearchIndex(runtime);
        var plans = new ArchivePlanStore(runtime);
        var registry = new FileHandleRegistry();
        var manager = new JobManager(runtime, new FixedProbe(0));
        var plan = new ArchiveIngestPlan
        {
            PlanId = "archive-cap-plan", PreviewId = "archive-cap-plan", ArchiveId = "arc_capacity",
            ArchiveName = "Capacity", CompanyId = "c", ProjectId = "p", SourceKind = "eml-tree",
            Dialect = "rfc822", SourceFingerprint = "fingerprint", TotalItems = 1, TotalSizeBytes = 100,
            EstimatedRequiredBytes = DiskCapacityPlanning.EstimateArchive(100, 1),
            Items = { new ArchiveIngestPlannedItem { Ordinal = 1, ItemId = "i", SourcePath = "unused.eml",
                MappedFolder = "INBOX", SizeBytes = 100, SourceSha256 = new string('0', 64) } }
        };
        var record = new LocalJobRecord { JobId = "archive-cap-job", JobKind = "archive-ingest",
            ClientContext = new() { CompanyId = "c", ProjectId = "p" } };
        var worker = new ArchiveIngestWorker(storage, index, plans, registry, manager, _ => { }, new FixedProbe(0));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteIngestAsync(record, plan, CancellationToken.None));

        Assert.Contains("DİSK KAPASİTESİ ENGELİ", ex.Message);
        Assert.Equal("failed", record.Status);
        Assert.Null(storage.GetArchiveManifest(plan.ArchiveId));
        Assert.False(Directory.Exists(Path.Combine(storage.StagingBaseDirectory, plan.ArchiveId)));
    }

    [Fact]
    public void ArchivePreview_UnavailableCapacity_IsPersistedFailClosed()
    {
        string runtime = BridgeTestHelpers.CreateTestDir("archive-preview-capacity");
        string eml = BridgeTestHelpers.CreateEmlFile(runtime, "one.eml", "one");
        var manifest = new MimeSourceInspector().BuildEmlFilesManifest(new[] { eml });
        var storage = new ArchiveStorageManager(runtime);
        using var index = new ArchiveSearchIndex(runtime);
        var plans = new ArchivePlanStore(runtime);
        var registry = new FileHandleRegistry();
        string handle = registry.RegisterMimeSource(manifest, eml);
        var manager = new JobManager(runtime, new FixedProbe("unavailable"));
        var catalog = new ArchiveCatalogService(storage, index, plans, registry, manager, new FixedProbe("unavailable"));

        var preview = catalog.CreatePreview(new ArchiveIngestPreviewRequest
        {
            SourceHandle = handle, ArchiveName = "Capacity", CompanyId = "c", ProjectId = "p"
        });
        var reloaded = catalog.GetPreview(preview.PreviewId);

        Assert.False(preview.CanIngest);
        Assert.False(reloaded.CanIngest);
        Assert.Contains("DİSK KAPASİTESİ ENGELİ", reloaded.BlockerReason);
        Assert.NotNull(reloaded.EstimatedRequiredBytes);
        Assert.Null(reloaded.AvailableFreeBytes);
    }

    private static string ResolveRepoPath(params string[] parts)
    {
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            string candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == null) break;
            current = parent;
        }
        throw new FileNotFoundException("Approved fixture not found.");
    }
}
