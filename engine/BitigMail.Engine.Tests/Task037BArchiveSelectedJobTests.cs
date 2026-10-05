using System.Text;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.Engine.Planning;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class Task037BArchiveSelectedJobTests
{
    [Fact]
    public async Task SelectedArchiveJobRevalidatesAndWritesMappedDeduplicatedVerifiedOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-037b-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var storage = new ArchiveStorageManager(root); string staging = storage.CreateStagingDirectory("seed");
            byte[] raw = Encoding.UTF8.GetBytes("Date: Mon, 01 Jan 2024 00:00:00 +0000\r\nFrom: a@example.test\r\nTo: b@example.test\r\nSubject: same\r\nMessage-Id: <same@example.test>\r\n\r\nbody");
            var one = await storage.StoreMimeItemAsync(staging, 1, "msg_00000001", "Inbox", new MemoryStream(raw), null, null, CancellationToken.None);
            var two = await storage.StoreMimeItemAsync(staging, 2, "msg_00000002", "Archive", new MemoryStream(raw), null, null, CancellationToken.None);
            var manifest = new ArchiveManifest { ArchiveId="arc_test", ArchiveName="Test", CompanyId="c", ProjectId="p", SourceKind="eml", Dialect="eml", SourceFingerprint=new string('a',64), TotalItems=2, TotalSizeBytes=raw.Length*2, Items=[one,two], Folders=[new(){FolderName="Inbox",ItemCount=1,TotalSizeBytes=raw.Length},new(){FolderName="Archive",ItemCount=1,TotalSizeBytes=raw.Length}], DateFilterBlocked=true, QualificationIsPartial=true, QualificationFingerprint=new string('b',64), QualificationWarnings=["Kaynak tarihleri doğrulanmadı."] };
            await storage.WriteManifestAsync(staging, manifest, CancellationToken.None); storage.PublishStagingToManagedArchive(staging, "arc_test");
            using var index = new ArchiveSearchIndex(root); await index.RebuildDatabaseAsync(storage, CancellationToken.None);
            var jobs = new JobManager(Path.Combine(root,"jobs-runtime")); var catalog = new ArchiveCatalogService(storage,index,new ArchivePlanStore(root),new FileHandleRegistry(),jobs); var service = new ArchiveSelectedJobService(catalog,jobs);
            var search = new ArchiveSearchRequest { SelectedScopes=[new(){ArchiveId="arc_test",CompanyId="c",ProjectId="p"}], Page=1, PageSize=20 };
            var found = catalog.Search(search); Assert.Equal(2, found.TotalCount);
            var plan = service.Preview(search, found.Items.Select(x=>x.MessageId).ToArray(), [new("arc_test:Inbox","Mapped/Inbox"),new("arc_test:Archive","Mapped/Archive")], DuplicatePolicy.ContentOnly, "c", "p");
            string output=Path.Combine(root,"out"); Directory.CreateDirectory(output); var job=service.Start(plan.PlanId,output,new(){CompanyId="c",ProjectId="p"});
            for(int i=0;i<100;i++){job=jobs.GetJob(job.JobId)!;if(job.Status is "completed" or "failed")break;await Task.Delay(20);}
            Assert.Equal("completed",job.Status); Assert.Equal(2,job.ItemsRead); Assert.Equal(1,job.ItemsWritten); Assert.Single(job.SkippedDuplicateItemIds);
            string file=Assert.Single(Directory.GetFiles(job.OutputDirectoryPath!,"*.eml",SearchOption.AllDirectories)); Assert.Equal(one.StoredSha256,BitigMail.Engine.Recovery.DamagedStoreResultValidator.HashFile(file)); Assert.Equal(plan.MappingFingerprint,job.FolderMappingFingerprint); Assert.Equal(plan.Frozen.Fingerprint,job.FrozenSelectionFingerprint);
            var qualification=new NormalizedSourceQualificationReader().Read(job.OutputDirectoryPath!)!; Assert.True(qualification.DateFilterBlocked); Assert.True(qualification.IsPartial); Assert.Equal(1,qualification.VerifiedOutputCount); Assert.Contains("Kaynak tarihleri doğrulanmadı.",qualification.Warnings);
            var source=new MimeSourceInspector().BuildEmlDirectoryManifest(job.OutputDirectoryPath!); var blocked=MimeSelectionEngine.EvaluateSelection(source,"selected-export",null,"2024-01-01",null,new PreflightCheckResult{CanConvert=true},CancellationToken.None); Assert.False(blocked.Preview.CanConvert); Assert.True(blocked.Preview.DateFilterBlocked);
        }
        finally { try { Directory.Delete(root,true); } catch { } }
    }
}
