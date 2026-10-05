using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.LocalHost.Recovery;
using Xunit;

namespace BitigMail.Engine.Tests;
public sealed class Task036RecoveryServiceTests
{
    [Fact] public async Task GlobalQueuePublishesOnlyValidatedPerJobDirectories()
    {
        string root=Path.Combine(Path.GetTempPath(),"bitigmail-036-service-"+Guid.NewGuid().ToString("N"));string runtime=Path.Combine(root,"runtime"),output=Path.Combine(root,"output"),source=Path.Combine(root,"known.pst");Directory.CreateDirectory(runtime);Directory.CreateDirectory(output);
        try{using(var pst=PersonalStorage.Create(source,FileFormatVersion.Unicode)){var folder=pst.CreatePredefinedFolder("Inbox",StandardIpmFolder.Inbox);folder.AddMessage(new MapiMessage("a@example.test","b@example.test","subject","body"));}var manager=new BitigMail.LocalHost.Jobs.JobManager(runtime);var service=new DamagedStoreRecoveryService(manager,new BitigMail.LocalHost.Security.RecoverySdkBootstrap(null));string hash=BitigMail.Engine.Recovery.DamagedStoreResultValidator.HashFile(source);var owner=new BitigMail.Engine.Models.ClientProjectContext{CompanyId="c",CompanyName="C",ProjectId="p",ProjectName="P"};var first=service.Start(source,output,hash,owner,TimeSpan.FromSeconds(30));var second=service.Start(source,output,hash,owner,TimeSpan.FromSeconds(30));Assert.Contains(service.Get(second.JobId)!.Status,new[]{"queued","running"});RecoveryJobView a=await Wait(service,first.JobId),b=await Wait(service,second.JobId);Assert.Equal("completed",a.Status);Assert.Equal("completed",b.Status);Assert.Equal(1,a.RecoveredCount);Assert.Equal(1,b.RecoveredCount);Assert.True(File.Exists(Path.Combine(output,a.JobId,"bitigmail-recovery-manifest.json")));Assert.True(File.Exists(Path.Combine(output,b.JobId,"bitigmail-recovery-manifest.json")));Assert.NotEmpty(service.GetReportBytes(a.JobId)!);var reopened=new BitigMail.LocalHost.Jobs.JobManager(runtime);var restored=new DamagedStoreRecoveryService(reopened,new BitigMail.LocalHost.Security.RecoverySdkBootstrap(null));Assert.Equal("completed",restored.Get(a.JobId)!.Status);Assert.NotEmpty(restored.GetReportBytes(a.JobId)!);File.AppendAllText(Path.Combine(output,a.JobId,"bitigmail-recovery-manifest.json")," ");Assert.Throws<InvalidDataException>(()=>restored.GetReportBytes(a.JobId));}
        finally{try{Directory.Delete(root,true);}catch{}}
    }
    private static async Task<RecoveryJobView> Wait(DamagedStoreRecoveryService service,string id){for(int i=0;i<200;i++){var job=service.Get(id)!;if(job.Status is not ("queued" or "running"))return job;await Task.Delay(50);}throw new TimeoutException();}
}
