using BitigMail.Engine.Bridge;
using BitigMail.LocalHost.Bridge.Transfer;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class BridgeJournalCriticalTests
{
    private static string DirectoryPath() => Path.Combine(Path.GetTempPath(), "bitigmail-bridge-critical", Guid.NewGuid().ToString("N"));
    private static BridgeImportPlan Import() => new()
    {
        PlanId = "plan-import", PreviewId = "plan-import", CompanyId = "company", ProjectId = "project",
        SourceHandle = "msrc-frozen", SourceFingerprint = new string('c', 64), SourceKind = "eml-files",
        TargetAccountId = "account-target", TargetAccountVersion = 1, CanTransfer = true,
        Items = new() { new() { ItemId = "item-one", SourceRelativePath = "one.eml", SourceCanonicalPath = "C:\\synthetic\\one.eml",
            PhysicalOrdinal = 1, SourceMappedFolder = "INBOX", TargetFolder = "Target", SourceSha256 = new string('a', 64),
            CanonicalSha256 = new string('b', 64), PlannedInternalDateUtc = DateTimeOffset.UnixEpoch } }
    };

    private static BridgeExportPlan Export() => new()
    {
        PlanId = "plan-export", PreviewId = "plan-export", CompanyId = "company", ProjectId = "project",
        SourceAccountId = "source-account", SourceAccountVersion = 1, TargetDirHandle = "dir-frozen", TargetFormat = "eml-tree",
        CanTransfer = true, FolderMappings = new() { new() { FolderKey = "folder-one", OriginalFolder = "INBOX" } },
        Items = new() { new() { ItemId = "item-one", SourceFolder = "INBOX", SourceUid = 1, SourceUidValidity = 10,
            SourceSha256 = new string('a', 64), InternalDateUtc = DateTimeOffset.UnixEpoch, FolderKey = "folder-one",
            RelativeOutputPath = "folder-one/item-one.eml" } }
    };

    [Fact]
    public void Import_FailedIntentDoesNotPublishOrPermitResetAfterRestart()
    {
        var directory = DirectoryPath(); var store = new BridgeTransferJournal(directory); var plan = Import();
        store.SaveImportPlan(plan); store.InitializeImportJournal("job-one", plan);
        var entry = store.GetImportJournal("job-one")!.Entries["item-one"];
        entry.Status = BridgeItemStatus.AppendIntent; entry.BitigMailKeyword = "bm_0123456789abcdef0123456789abcdef";
        entry.TargetUidValidity = 20;
        store.OnBeforeAtomicWrite = _ => throw new IOException("Synthetic disk failure");
        Assert.Throws<IOException>(() => store.UpdateImportEntry("job-one", entry));
        var restarted = new BridgeTransferJournal(directory);
        Assert.Equal(BridgeItemStatus.Planned, restarted.GetImportJournal("job-one")!.Entries["item-one"].Status);
        Assert.ThrowsAny<Exception>(() => restarted.InitializeImportJournal("job-one", plan));
    }

    [Fact]
    public void Export_FailedVerifiedWriteDoesNotPublishOrChangeSourceIdentity()
    {
        var directory = DirectoryPath(); var store = new BridgeTransferJournal(directory); var plan = Export();
        store.SaveExportPlan(plan); store.InitializeExportJournal("job-one", plan, Path.Combine(directory, "output"));
        var entry = store.GetExportJournal("job-one")!.Entries["item-one"];
        entry.Status = BridgeItemStatus.Verified; entry.VerifiedSha256 = entry.ExpectedSha256; entry.OutputLength = 100;
        store.OnBeforeAtomicWrite = _ => throw new IOException("Synthetic disk failure");
        Assert.Throws<IOException>(() => store.UpdateExportEntry("job-one", entry));
        Assert.Equal(BridgeItemStatus.Planned, new BridgeTransferJournal(directory).GetExportJournal("job-one")!.Entries["item-one"].Status);
        store.OnBeforeAtomicWrite = null;
        entry.SourceUid = 99;
        Assert.ThrowsAny<Exception>(() => store.UpdateExportEntry("job-one", entry));
        Assert.Equal(1u, store.GetExportJournal("job-one")!.Entries["item-one"].SourceUid);
    }

    [Fact]
    public void FrozenSnapshots_DoNotPublishCallerMutationsOrOverwritePlans()
    {
        var store = new BridgeTransferJournal(DirectoryPath()); var a = Import(); var b = Export();
        store.SaveImportPlan(a); store.SaveExportPlan(b);
        a.Items.Clear(); b.Items.Clear();
        Assert.Single(store.GetImportPlan(a.PlanId)!.Items); Assert.Single(store.GetExportPlan(b.PlanId)!.Items);
        Assert.ThrowsAny<Exception>(() => store.SaveImportPlan(a)); Assert.ThrowsAny<Exception>(() => store.SaveExportPlan(b));
        store.InitializeImportJournal("job-import", store.GetImportPlan(a.PlanId)!);
        var state = store.GetImportJournal("job-import")!; state.Entries.Clear();
        Assert.Single(store.GetImportJournal("job-import")!.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptExistingJournal_CannotBecomeAbsentOrBeOverwritten(bool export)
    {
        var directory = DirectoryPath(); var store = new BridgeTransferJournal(directory);
        if (export) { store.SaveExportPlan(Export()); store.InitializeExportJournal("job-one", Export(), Path.Combine(directory,"output")); }
        else { store.SaveImportPlan(Import()); store.InitializeImportJournal("job-one", Import()); }
        var path = Path.Combine(export ? store.ExportJournalsDirectory : store.ImportJournalsDirectory, "job-one.json");
        File.WriteAllText(path, "{broken");
        var restarted = new BridgeTransferJournal(directory);
        if (export)
        {
            Assert.ThrowsAny<Exception>(() => restarted.GetExportJournal("job-one"));
            Assert.ThrowsAny<Exception>(() => restarted.InitializeExportJournal("job-one", Export(), Path.Combine(directory,"output")));
        }
        else
        {
            Assert.ThrowsAny<Exception>(() => restarted.GetImportJournal("job-one"));
            Assert.ThrowsAny<Exception>(() => restarted.InitializeImportJournal("job-one", Import()));
        }
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData("file:stream")]
    public void AllJournalReads_RejectPathIdentifiers(string id)
    {
        var store = new BridgeTransferJournal(DirectoryPath());
        Assert.Throws<ArgumentException>(() => store.GetImportPlan(id));
        Assert.Throws<ArgumentException>(() => store.GetExportPlan(id));
        Assert.Throws<ArgumentException>(() => store.GetImportJournal(id));
        Assert.Throws<ArgumentException>(() => store.GetExportJournal(id));
    }

    [Fact]
    public async Task Export_ActualSharingLockReleasesAndPublishesCompleteVerifiedEntry()
    {
        string directory = DirectoryPath();
        var store = new BridgeTransferJournal(directory);
        var plan = Export();
        store.SaveExportPlan(plan);
        store.InitializeExportJournal("job-one", plan, Path.Combine(directory, "output"));
        string path = Path.Combine(store.ExportJournalsDirectory, "job-one.json");
        byte[] before = File.ReadAllBytes(path);
        var entry = store.GetExportJournal("job-one")!.Entries["item-one"];
        entry.Status = BridgeItemStatus.Verified;
        entry.VerifiedSha256 = entry.ExpectedSha256;
        entry.OutputLength = 100;
        var retried = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.OnAtomicPublicationRetry = _ => retried.TrySetResult(true);
        using var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var save = Task.Run(() => store.UpdateExportEntry("job-one", entry));
        try
        {
            await retried.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            lease.Dispose();
            await save.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var durable = new BridgeTransferJournal(directory).GetExportJournal("job-one")!.Entries["item-one"];
        Assert.Equal(BridgeItemStatus.Verified, durable.Status);
        Assert.Equal(entry.ExpectedSha256, durable.VerifiedSha256);
        Assert.Equal(100, durable.OutputLength);
        Assert.Empty(Directory.GetFiles(store.ExportJournalsDirectory, "*.tmp"));
    }

    [Fact]
    public async Task Export_PermanentSharingLockPreservesPlannedJournalAndFails()
    {
        string directory = DirectoryPath();
        var store = new BridgeTransferJournal(directory);
        var plan = Export();
        store.SaveExportPlan(plan);
        store.InitializeExportJournal("job-one", plan, Path.Combine(directory, "output"));
        string path = Path.Combine(store.ExportJournalsDirectory, "job-one.json");
        byte[] before = File.ReadAllBytes(path);
        var entry = store.GetExportJournal("job-one")!.Entries["item-one"];
        entry.Status = BridgeItemStatus.Verified;
        entry.VerifiedSha256 = entry.ExpectedSha256;
        int retries = 0;
        store.OnAtomicPublicationRetry = _ => retries++;
        using var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Exception? failure = await Task.Run(() => Record.Exception(() => store.UpdateExportEntry("job-one", entry)))
            .WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(5, retries);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(BridgeItemStatus.Planned, new BridgeTransferJournal(directory).GetExportJournal("job-one")!.Entries["item-one"].Status);
        Assert.Empty(Directory.GetFiles(store.ExportJournalsDirectory, "*.tmp"));
    }
}
