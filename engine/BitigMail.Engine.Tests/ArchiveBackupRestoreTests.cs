using System.Security.Cryptography;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;
public sealed class ArchiveBackupRestoreTests:IDisposable
{
 private readonly string _dir=Path.Combine(Path.GetTempPath(),"bitigmail-backup-"+Guid.NewGuid().ToString("N"));
 public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
 [Fact] public async Task BackupAndRestoreCreatesFreshArchiveWithExactRawBytes()
 {
  var storage=new ArchiveStorageManager(_dir);var index=new ArchiveSearchIndex(_dir);var plans=new ArchivePlanStore(_dir);var handles=new FileHandleRegistry();var jobs=new JobManager(_dir);var catalog=new ArchiveCatalogService(storage,index,plans,handles,jobs);
  string staging=storage.CreateStagingDirectory("seed");Directory.CreateDirectory(Path.Combine(staging,"raw"));byte[] raw="From: a@example.test\r\nTo: b@example.test\r\nSubject: private\r\n\r\nbody"u8.ToArray();await File.WriteAllBytesAsync(Path.Combine(staging,"raw","one.eml"),raw);string hash=Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();var manifest=new ArchiveManifest{ArchiveId="arc_seed",ArchiveName="Seed",CompanyId="cmp_a",ProjectId="prj_a",SourceKind="eml-files",Dialect="eml",SourceFingerprint=hash,TotalItems=1,TotalSizeBytes=raw.Length,Items=[new(){Ordinal=1,ItemId="one",RelativeEmlPath="raw/one.eml",SourceSha256=hash,StoredSha256=hash,ByteLength=raw.Length,OriginalFolder="Inbox"}]};await storage.WriteManifestAsync(staging,manifest,CancellationToken.None);storage.PublishFreshStagingToManagedArchive(staging,manifest.ArchiveId);
  string output=Path.Combine(_dir,"out");Directory.CreateDirectory(output);var backup=await new ArchiveBackupService(catalog).CreateAsync([new ArchiveScopeSelection("cmp_a","prj_a","arc_seed")],output,CancellationToken.None);string zip=Path.Combine(output,backup.FileName);Assert.True(File.Exists(zip));
  var restored=await new ArchiveRestoreService(catalog).RestoreAsync(zip,"cmp_b","prj_b",new AuthenticatedSessionPrincipal("admin","session",1),CancellationToken.None);Assert.Equal(RestoreReceiptState.Indexed,restored.State);Assert.Single(restored.FreshArchiveIds);var restoredManifest=storage.GetArchiveManifest(restored.FreshArchiveIds[0]);Assert.NotNull(restoredManifest);Assert.Equal("cmp_b",restoredManifest!.CompanyId);await using var restoredRaw=storage.OpenRawEmlStream(restoredManifest.ArchiveId,"raw/one.eml");using var ms=new MemoryStream();await restoredRaw.CopyToAsync(ms);Assert.Equal(raw,ms.ToArray());Assert.NotEqual("arc_seed",restoredManifest.ArchiveId);
 }
}
