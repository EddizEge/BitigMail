using System.Text;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class Task037CArchiveAdvancedFilterTests
{
    [Fact]
    public async Task FullRawBodyFilterRunsBeforePaginationAndSelectedPreviewUsesSameAst()
    {
        string root=Path.Combine(Path.GetTempPath(),"bitigmail-037c-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var storage=new ArchiveStorageManager(root); string staging=storage.CreateStagingDirectory("seed");
            byte[] ordinary=Encoding.UTF8.GetBytes("Date: Mon, 01 Jan 2024 00:00:00 +0000\r\nSubject: ordinary\r\n\r\nno match");
            byte[] late=Encoding.UTF8.GetBytes("Date: Tue, 02 Jan 2024 00:00:00 +0000\r\nSubject: late\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n"+new string('x',600_000)+" UNIQUE-LATE-MARKER");
            var one=await storage.StoreMimeItemAsync(staging,1,"one","Inbox",new MemoryStream(ordinary),null,null,CancellationToken.None);
            var two=await storage.StoreMimeItemAsync(staging,2,"two","Inbox",new MemoryStream(late),null,null,CancellationToken.None);
            var manifest=new ArchiveManifest{ArchiveId="arc",ArchiveName="A",CompanyId="c",ProjectId="p",SourceKind="eml",Dialect="eml",SourceFingerprint=new string('a',64),TotalItems=2,TotalSizeBytes=ordinary.Length+late.Length,TruncatedItemsCount=1,Items=[one,two],Folders=[new(){FolderName="Inbox",ItemCount=2,TotalSizeBytes=ordinary.Length+late.Length}]};
            await storage.WriteManifestAsync(staging,manifest,CancellationToken.None);storage.PublishStagingToManagedArchive(staging,"arc");
            using var index=new ArchiveSearchIndex(root);await index.RebuildDatabaseAsync(storage,CancellationToken.None);
            var catalog=new ArchiveCatalogService(storage,index,new ArchivePlanStore(root),new FileHandleRegistry(),new JobManager(Path.Combine(root,"jobs")));
            var filter=new MailFilterDefinition(1,new("condition","body","contains","unique-late-marker"));
            var request=new ArchiveSearchRequest{SelectedScopes=[new(){ArchiveId="arc",CompanyId="c",ProjectId="p"}],Page=1,PageSize=1,AdvancedFilter=filter};
            var result=catalog.Search(request);Assert.Equal(1,result.TotalCount);Assert.Single(result.Items);Assert.Equal("arc:two",result.Items[0].MessageId);Assert.NotNull(result.AdvancedFilterFingerprint);Assert.Equal(0,result.AdvancedFilterUnknownCount);
            var preview=catalog.GetMessagePreview(new(){MessageId="arc:two",SearchRequest=request});Assert.Equal(two.StoredSha256,preview.Sha256);
            Assert.Throws<ArchiveSearchPolicyException>(()=>catalog.GetMessagePreview(new(){MessageId="arc:one",SearchRequest=request}));
        }
        finally{try{Directory.Delete(root,true);}catch{}}
    }
}
