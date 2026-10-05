using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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

public sealed class BridgeExportCapacityTests
{
    private sealed class FixedCapacityProbe : IDiskCapacityProbe
    {
        private readonly DiskCapacityResult _result;
        public FixedCapacityProbe(long available) => _result = new(true, available, null);
        public FixedCapacityProbe(string error) => _result = new(false, null, error);
        public DiskCapacityResult Probe(string directoryPath) => _result;
    }

    [Fact]
    public void Estimate_UsesDocumentedEmlAndMboxAllowances_AndCheckedArithmetic()
    {
        const long raw = 1_000;
        const int count = 2;
        long common = count * BridgeExportCapacityEstimator.PerItemAllowanceBytes + BridgeExportCapacityEstimator.FixedReserveBytes;

        Assert.Equal(raw + common, BridgeExportCapacityEstimator.Estimate("eml-tree", raw, count));
        Assert.Equal((3 * raw) + common, BridgeExportCapacityEstimator.Estimate("mboxrd", raw, count));
        Assert.Throws<OverflowException>(() => BridgeExportCapacityEstimator.Estimate("mboxrd", long.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeExportCapacityEstimator.Estimate("eml-tree", -1, 0));
    }

    [Fact]
    public async Task Preview_UsesEligibleRawBytes_AndExactCapacityBoundary()
    {
        var fixture = CreateFixture("capacity-filtered");
        byte[] eligibleRaw = AddMessage(fixture.Client, 1, "2024-01-10T07:00:00Z", "eligible");
        AddMessage(fixture.Client, 2, "2023-01-10T07:00:00Z", "excluded");
        long expected = BridgeExportCapacityEstimator.Estimate("eml-tree", eligibleRaw.LongLength, 1);
        var service = new BridgeExportPreviewService(fixture.Registry, fixture.Store, fixture.Factory, fixture.Journal, fixture.Resolver, new FixedCapacityProbe(expected));

        var preview = await service.CreatePreviewAsync(Request(fixture, "eml-tree", "2024-01-01", null));

        Assert.True(preview.CanTransfer);
        Assert.Equal(1, preview.EligibleItemsCount);
        Assert.Equal(1, preview.ExcludedCount);
        Assert.Equal(expected, preview.EstimatedRequiredBytes);
        Assert.Equal(expected, preview.AvailableFreeBytes);
        Assert.Equal(expected, fixture.Journal.GetExportPlan(preview.PreviewId)!.EstimatedRequiredBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_FailsClosed_WhenCapacityUnavailableOrInsufficient(bool unavailable)
    {
        var fixture = CreateFixture("capacity-block");
        AddMessage(fixture.Client, 1, "2024-01-10T07:00:00Z", "one");
        IDiskCapacityProbe probe = unavailable ? new FixedCapacityProbe("probe failed") : new FixedCapacityProbe(0);
        var service = new BridgeExportPreviewService(fixture.Registry, fixture.Store, fixture.Factory, fixture.Journal, fixture.Resolver, probe);

        var preview = await service.CreatePreviewAsync(Request(fixture, "mboxrd"));

        Assert.False(preview.CanTransfer);
        Assert.Contains("DİSK KAPASİTESİ ENGELİ", preview.BlockerReason);
        Assert.NotNull(preview.EstimatedRequiredBytes);
        Assert.Equal(unavailable ? null : 0, preview.AvailableFreeBytes);
    }

    [Fact]
    public async Task Worker_FreeSpaceDrop_FailsBeforeOutputCreationOrSourceFetch()
    {
        var fixture = CreateFixture("capacity-worker-drop");
        AddMessage(fixture.Client, 1, "2024-01-10T07:00:00Z", "one");
        var previewService = new BridgeExportPreviewService(fixture.Registry, fixture.Store, fixture.Factory, fixture.Journal, fixture.Resolver, new FixedCapacityProbe(long.MaxValue));
        var preview = await previewService.CreatePreviewAsync(Request(fixture, "eml-tree"));
        var plan = fixture.Journal.GetExportPlan(preview.PreviewId)!;
        int fetchesBeforeWorker = fixture.Client.FetchSingleSourceMessageCallCount;
        var worker = new BridgeExportWorker(fixture.Registry, fixture.Store, fixture.Factory, fixture.Journal, _ => { }, _ => { }, fixture.Resolver, new FixedCapacityProbe(0));
        var job = new LocalJobRecord { JobId = "capacity-drop-job", JobKind = "bridge-export", PlanId = plan.PlanId,
            ClientContext = new() { CompanyId = "company-1", ProjectId = "project-1" } };

        await worker.ExecuteAsync(job, plan);

        Assert.Equal("failed", job.Status);
        Assert.Contains("DİSK KAPASİTESİ ENGELİ", job.ErrorMessage);
        Assert.Equal(fetchesBeforeWorker, fixture.Client.FetchSingleSourceMessageCallCount);
        Assert.False(Directory.Exists(Path.Combine(fixture.OutputRoot, "bridge-export-capacity-drop-job")));
    }

    [Fact]
    public void LegacyPlanWithoutEstimate_RemainsDeserializable()
    {
        const string json = """{"planId":"legacy","previewId":"legacy","companyId":"c","projectId":"p","sourceAccountId":"acc_00000000000000000000000000000000","sourceAccountVersion":1,"targetDirHandle":"dir_x","targetFormat":"eml-tree"}""";
        var plan = JsonSerializer.Deserialize<BridgeExportPlan>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(plan);
        Assert.Null(plan.EstimatedRequiredBytes);
    }

    private static byte[] AddMessage(BridgeFakeTransferClient client, uint uid, string date, string body)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("S", "s@example.test"));
        msg.To.Add(new MailboxAddress("R", "r@example.test"));
        msg.Subject = body;
        msg.Body = new TextPart("plain") { Text = body };
        byte[] raw = ImapSerializationAssumptions.Serialize(msg);
        client.AddSourceMessage("INBOX", uid, raw, msg, DateTimeOffset.Parse(date), DateTimeOffset.Parse(date), MessageFlags.None);
        return raw;
    }

    private static BridgeExportPreviewRequest Request(Fixture f, string format, string? start = null, string? end = null) => new()
    {
        CompanyId = "company-1", ProjectId = "project-1", SourceAccountId = f.SourceId,
        TargetDirHandle = f.Handle, TargetFormat = format, SelectedFolders = new() { "INBOX" }, StartDate = start, EndDate = end
    };

    private static Fixture CreateFixture(string prefix)
    {
        string dir = BridgeTestHelpers.CreateTestDir(prefix);
        var accounts = BridgeTestHelpers.SetupTestAccounts(dir);
        var registry = new FileHandleRegistry();
        string output = Path.Combine(dir, "output");
        Directory.CreateDirectory(output);
        var client = new BridgeFakeTransferClient();
        return new(dir, output, registry.RegisterOutputDir(output), registry, accounts.store, accounts.sourceId,
            accounts.resolver, client, new BridgeFakeClientFactory(client), new BridgeTransferJournal(dir));
    }

    private sealed record Fixture(string Root, string OutputRoot, string Handle, FileHandleRegistry Registry,
        BitigMail.LocalHost.Imap.ImapAccountStore Store, string SourceId,
        BitigMail.LocalHost.Security.IImapCredentialResolver Resolver, BridgeFakeTransferClient Client,
        BridgeFakeClientFactory Factory, BridgeTransferJournal Journal);
}
