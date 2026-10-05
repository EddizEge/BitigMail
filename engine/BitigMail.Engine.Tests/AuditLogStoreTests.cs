using BitigMail.Engine.Security;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;
public sealed class AuditLogStoreTests:IDisposable
{
 private readonly string _dir=Path.Combine(Path.GetTempPath(),"bitigmail-audit-store-"+Guid.NewGuid().ToString("N"));
 public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
 [Fact] public void PersistsAndDetectsMissingSuffixOrTamper()
 {
  var store=new AuditLogStore(_dir);store.Append("user-1","archive.backup","cmp-1","prj-1",null,AuditOutcome.Succeeded);store.Append("user-1","archive.restore","cmp-1","prj-1",null,AuditOutcome.Failed);
  var valid=store.Read();Assert.True(valid.IntegrityValid);Assert.Equal(2,valid.Entries.Count);
  string log=Path.Combine(_dir,"audit-v1.jsonl");File.WriteAllText(log,File.ReadLines(log).First()+Environment.NewLine);
  var invalid=new AuditLogStore(_dir).Read();Assert.False(invalid.IntegrityValid);Assert.NotNull(invalid.Warning);Assert.Empty(invalid.Entries);
 }
}
