using System;
using System.IO;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.LocalHost.Imap.Transfer;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class ImapJournalCriticalTests
{
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "bitigmail-journal-critical", Guid.NewGuid().ToString("N"));

    private static ImapTransferPlan Plan() => new()
    {
        PlanId = "plan-critical", PreviewId = "plan-critical", CompanyId = "company", ProjectId = "project",
        SourceAccountId = "source", SourceAccountVersion = 1, TargetAccountId = "target", TargetAccountVersion = 1,
        Items = new() { new() { ItemId = "item-one", SourceFolder = "INBOX", SourceDelimiter = "/", SourceUid = 1,
            SourceUidValidity = 10, SourceSha256 = new string('a',64), InternalDateUtc = DateTimeOffset.Parse("2024-01-01T12:00:00Z"), TargetFolder = "Target" } }
    };

    [Fact]
    public void FrozenPlanAndJournal_DoNotShareMutableObjectsWithCallers()
    {
        var journal = new ImapTransferJournal(NewDirectory());
        var original = Plan();
        journal.SavePlan(original);
        original.Items[0].TargetFolder = "Caller mutation";
        Assert.Equal("Target", journal.GetPlan(original.PlanId)!.Items[0].TargetFolder);
        var loaded = journal.GetPlan(original.PlanId)!;
        loaded.Items.Clear();
        Assert.Single(journal.GetPlan(original.PlanId)!.Items);
        var state = journal.InitializeJournal("job-critical", journal.GetPlan(original.PlanId)!);
        state.Entries["item-one"].Status = ImapTransferItemStatus.Verified;
        Assert.Equal(ImapTransferItemStatus.Planned, journal.GetJournal("job-critical")!.Entries["item-one"].Status);
    }

    [Fact]
    public void FailedDurableIntent_DoesNotPublishTransitionInMemoryOrAfterRestart()
    {
        string directory = NewDirectory();
        var journal = new ImapTransferJournal(directory);
        journal.SavePlan(Plan());
        journal.InitializeJournal("job-critical", Plan());
        var entry = journal.GetJournal("job-critical")!.Entries["item-one"];
        entry.Status = ImapTransferItemStatus.AppendIntent;
        entry.BitigMailKeyword = "bm_0123456789abcdef0123456789abcdef";
        entry.TargetUidValidity = 20;
        journal.OnBeforeAtomicWrite = _ => throw new IOException("Synthetic disk failure");
        Assert.Throws<IOException>(() => journal.UpdateEntry("job-critical", entry));
        Assert.Equal(ImapTransferItemStatus.Planned, journal.GetJournal("job-critical")!.Entries["item-one"].Status);
        Assert.Equal(ImapTransferItemStatus.Planned, new ImapTransferJournal(directory).GetJournal("job-critical")!.Entries["item-one"].Status);
    }

    [Fact]
    public void CorruptExistingJournal_CannotBecomeMissingOrBeReinitialized()
    {
        string directory = NewDirectory();
        var journal = new ImapTransferJournal(directory);
        journal.SavePlan(Plan());
        journal.InitializeJournal("job-critical", Plan());
        File.WriteAllText(Path.Combine(journal.JournalsDirectory, "job-critical.json"), "{broken");
        Assert.ThrowsAny<Exception>(() => new ImapTransferJournal(directory).GetJournal("job-critical"));
        Assert.ThrowsAny<Exception>(() => journal.InitializeJournal("job-critical", Plan()));
        Assert.Equal("{broken", File.ReadAllText(Path.Combine(journal.JournalsDirectory, "job-critical.json")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData("entry:stream")]
    public void Identifiers_CannotEscapeOwnedDirectories(string invalidId)
    {
        var journal = new ImapTransferJournal(NewDirectory());
        Assert.Throws<ArgumentException>(() => journal.GetPlan(invalidId));
        Assert.Throws<ArgumentException>(() => journal.GetJournal(invalidId));
    }

    [Fact]
    public void ExistingPlanAndJournal_CannotBeOverwrittenToResetEvidence()
    {
        var journal = new ImapTransferJournal(NewDirectory());
        journal.SavePlan(Plan());
        var changed = Plan(); changed.Items.Clear();
        Assert.ThrowsAny<Exception>(() => journal.SavePlan(changed));
        journal.InitializeJournal("job-critical", Plan());
        var entry = journal.GetJournal("job-critical")!.Entries["item-one"];
        entry.Status = ImapTransferItemStatus.AppendIntent;
        entry.BitigMailKeyword = "bm_0123456789abcdef0123456789abcdef"; entry.TargetUidValidity = 20;
        journal.UpdateEntry("job-critical", entry);
        Assert.ThrowsAny<Exception>(() => journal.InitializeJournal("job-critical", Plan()));
        Assert.Equal(ImapTransferItemStatus.AppendIntent, journal.GetJournal("job-critical")!.Entries["item-one"].Status);
    }
}
