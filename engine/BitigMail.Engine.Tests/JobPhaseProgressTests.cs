using BitigMail.LocalHost.Dialogs;
using System.Text.Json;
using BitigMail.Engine.Models;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Bridge.Transfer;
using Xunit;
namespace BitigMail.Engine.Tests;
public class JobPhaseProgressTests
{
    [Fact]
    public void LegacyJsonHasUnknownPhase()
    {
        var job = JsonSerializer.Deserialize<LocalJobRecord>("{\"JobId\":\"old\",\"Status\":\"interrupted\"}")!;
        Assert.Null(job.ProgressPhase); Assert.Null(job.PhaseCompleted); Assert.Null(job.PhaseTotal);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalVerificationIsDistinctAndCannotHideFailure(bool damageAtVerification)
    {
        var dir = BridgeTestHelpers.CreateTestDir("phase-progress");
        var (accounts, _, target, resolver) = BridgeTestHelpers.SetupTestAccounts(dir);
        var handles = new FileHandleRegistry();
        var journal = new BridgeTransferJournal(dir);
        var fake = new BridgeFakeTransferClient();
        var factory = new BridgeFakeClientFactory(fake);
        var source = Path.Combine(dir, "input"); Directory.CreateDirectory(Path.Combine(source,"INBOX"));
        BridgeTestHelpers.CreateEmlFile(Path.Combine(source,"INBOX"), "one.eml", "One", "Fri, 05 Jan 2024 10:00:00 +0300", "<one@example.test>", "Body\r\n");
        var previewService = new BridgeImportPreviewService(handles, accounts, factory, journal, resolver);
        var handle = handles.RegisterMimeSource(new MimeSourceInspector().BuildEmlDirectoryManifest(source), "eml-tree");
        var preview = await previewService.CreatePreviewAsync(new BridgeImportPreviewRequest { CompanyId="company-1", ProjectId="project-1", SourceHandle=handle, TargetAccountId=target, SelectedFolders=new List<string>{"INBOX"} });
        Assert.True(preview.CanTransfer);
        var plan = journal.GetImportPlan(preview.PreviewId)!;
        var events = new List<(string Status, int? Done, int? Total, int Written)>();
        var reportSaved = false;
        var job = new LocalJobRecord { JobId="phase-"+Guid.NewGuid().ToString("N"), JobKind="bridge-import", ClientContext=new(){CompanyId="company-1",ProjectId="project-1"} };
        var worker = new BridgeImportWorker(handles, accounts, factory, journal, record => {
            events.Add((record.Status,record.PhaseCompleted,record.PhaseTotal,record.ItemsWritten));
            if(record.Status=="verifying" && record.PhaseCompleted==0 && damageAtVerification) fake.TargetMessages.Clear();
            if(record.Status=="completed") Assert.True(reportSaved);
        }, _ => reportSaved=true, resolver);
        await worker.ExecuteAsync(job,plan);
        Assert.Contains(events, e=>e.Status=="verifying" && e.Done==0 && e.Total==1 && e.Written==1);
        if(damageAtVerification) { Assert.Equal("failed",job.Status); Assert.DoesNotContain(events,e=>e.Status=="completed"); Assert.Equal(0,job.PhaseCompleted); }
        else { Assert.Equal("completed",job.Status); Assert.Equal(1,job.PhaseCompleted); Assert.True(reportSaved); }
    }
}