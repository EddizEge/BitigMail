using System.Text.Json;using BitigMail.Engine.Planning;
namespace BitigMail.LocalHost.Planning;
public sealed record SavedTransferTemplate(string TemplateId,TransferTemplate Template,DateTimeOffset CreatedAt,string? CreatorUserId=null);
public sealed class TransferTemplateStore
{
 private readonly string _dir;private readonly object _gate=new();public TransferTemplateStore(string runtime){_dir=Path.Combine(runtime,"templates");Directory.CreateDirectory(_dir);}
 public SavedTransferTemplate Save(TransferTemplate template,string? creatorUserId=null){string canonical=TransferTemplatePolicy.Serialize(template),id="tpl_"+Guid.NewGuid().ToString("N")[..16];var saved=new SavedTransferTemplate(id,JsonSerializer.Deserialize<TransferTemplate>(canonical)!,DateTimeOffset.UtcNow,creatorUserId);lock(_gate){string temp=Path.Combine(_dir,id+"."+Guid.NewGuid().ToString("N")+".tmp"),path=Path.Combine(_dir,id+".json");File.WriteAllText(temp,JsonSerializer.Serialize(saved));File.Move(temp,path);}return saved;}
 public IReadOnlyList<SavedTransferTemplate> List(string? actorUserId=null,bool admin=false){lock(_gate)return Directory.GetFiles(_dir,"tpl_*.json").Select(Read).Where(x=>actorUserId is null||admin||x.CreatorUserId==actorUserId).OrderBy(x=>x.CreatedAt).ToList();}
 public SavedTransferTemplate Get(string id,string? actorUserId=null,bool admin=false){ValidateId(id);lock(_gate){string path=Path.Combine(_dir,id+".json");if(!File.Exists(path))throw new KeyNotFoundException();var saved=Read(path);if(actorUserId is not null&&!admin&&saved.CreatorUserId!=actorUserId)throw new KeyNotFoundException();return saved;}}
 public void Delete(string id,string? actorUserId=null,bool admin=false){_ = Get(id,actorUserId,admin);lock(_gate){string path=Path.Combine(_dir,id+".json");File.Delete(path);}}
 private static void ValidateId(string id){if(!id.StartsWith("tpl_",StringComparison.Ordinal)||id.Any(c=>!(char.IsLetterOrDigit(c)||c=='_')))throw new KeyNotFoundException();}
 private static SavedTransferTemplate Read(string path){if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Şablon dosyası boyut sınırını aşıyor.");var value=JsonSerializer.Deserialize<SavedTransferTemplate>(File.ReadAllText(path))??throw new InvalidDataException();_ = TransferTemplatePolicy.Serialize(value.Template);return value;}
}
