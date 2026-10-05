using System.Text.Json;
using System.Text.Json.Serialization;
using BitigMail.Engine.Security;

namespace BitigMail.LocalHost.Security;

public sealed record AuditReadResult(bool IntegrityValid,string? Warning,IReadOnlyList<AuditChainEntry> Entries,AuditChainHead Head);

public sealed class AuditLogStore
{
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    private readonly string _profileDirectory,_logPath,_headPath;private readonly object _gate=new();
    public AuditLogStore(string profileDirectory){_profileDirectory=Path.GetFullPath(profileDirectory);RejectLinks(_profileDirectory);Directory.CreateDirectory(_profileDirectory);_logPath=Path.Combine(_profileDirectory,"audit-v1.jsonl");_headPath=Path.Combine(_profileDirectory,"audit-v1.head.json");using var lease=ProfileWriteLease.AcquireAsync(_profileDirectory,timeout:TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();if(!File.Exists(_headPath)&&!File.Exists(_logPath))PublishHead(AuditChain.Empty);}

    public void Append(string actorId,string actionCode,string? companyId,string? projectId,string? jobId,AuditOutcome outcome)
    {
        using var lease=ProfileWriteLease.AcquireAsync(_profileDirectory,timeout:TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();lock(_gate)
        {
            var current=ReadLocked(throwOnIntegrityFailure:true);var entry=AuditChain.Append(current.Head,new(DateTimeOffset.UtcNow,actorId,actionCode,companyId,projectId,jobId,outcome));
            byte[] line=JsonSerializer.SerializeToUtf8Bytes(entry,Json);
            RejectLinks(_logPath);
            using(var stream=new FileStream(_logPath,FileMode.Append,FileAccess.Write,FileShare.Read)){stream.Write(line);stream.WriteByte((byte)'\n');stream.Flush(true);}
            PublishHead(new(entry.Sequence,entry.Hash));
        }
    }

    public AuditReadResult Read()
    {
        using var lease=ProfileWriteLease.AcquireAsync(_profileDirectory,timeout:TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();lock(_gate){try{return ReadLocked(true);}catch(Exception ex)when(ex is InvalidDataException or JsonException or IOException){return new(false,"Denetim zinciri eksik, kesilmiş veya değiştirilmiş.",Array.Empty<AuditChainEntry>(),AuditChain.Empty);}}
    }

    private AuditReadResult ReadLocked(bool throwOnIntegrityFailure)
    {
        var head=TryReadHead();var entries=ReadEntriesBounded();
        try{AuditChain.Verify(entries,head);return new(true,null,entries.AsReadOnly(),head);}catch(InvalidDataException)when(entries.Count>head.Sequence){var prefix=entries.Take(checked((int)head.Sequence)).ToArray();AuditChain.Verify(prefix,head);var recovered=new AuditChainHead(entries[^1].Sequence,entries[^1].Hash);AuditChain.Verify(entries,recovered);PublishHead(recovered);return new(true,"Denetim başlığı, diske yazılmış geçerli kuyruktan kurtarıldı.",entries.AsReadOnly(),recovered);}catch when(!throwOnIntegrityFailure){return new(false,"Denetim zinciri doğrulanamadı.",Array.Empty<AuditChainEntry>(),head);}
    }
    private List<AuditChainEntry> ReadEntriesBounded(){var result=new List<AuditChainEntry>();if(!File.Exists(_logPath))return result;RejectLinks(_logPath);using var stream=new FileStream(_logPath,FileMode.Open,FileAccess.Read,FileShare.Read);using var reader=new StreamReader(stream,System.Text.Encoding.UTF8,true,4096,false);while(!reader.EndOfStream){var buffer=new char[1_048_577];int count=0;while(count<buffer.Length){int value=reader.Read();if(value<0||value=='\n')break;buffer[count++]=(char)value;}if(count==0||count>1_048_576)throw new InvalidDataException();string line=new(buffer,0,count);RejectDuplicateProperties(line);result.Add(JsonSerializer.Deserialize<AuditChainEntry>(line,Json)??throw new InvalidDataException());if(result.Count>1_000_000)throw new InvalidDataException();}return result;}
    private AuditChainHead TryReadHead(){if(!File.Exists(_headPath))return File.Exists(_logPath)?throw new InvalidDataException():AuditChain.Empty;try{RejectLinks(_headPath);using var stream=new FileStream(_headPath,FileMode.Open,FileAccess.Read,FileShare.Read);if(stream.Length is <1 or >4096)throw new InvalidDataException();byte[] bytes=new byte[stream.Length];stream.ReadExactly(bytes);string text=System.Text.Encoding.UTF8.GetString(bytes);RejectDuplicateProperties(text);return JsonSerializer.Deserialize<AuditChainHead>(text,Json)??throw new InvalidDataException();}catch(Exception ex)when(ex is JsonException or IOException){throw new InvalidDataException("Denetim başlığı bozuk.",ex);}}
    private void PublishHead(AuditChainHead head){RejectLinks(_profileDirectory);string temp=_headPath+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,head,Json);stream.Flush(true);}File.Move(temp,_headPath,true);}finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}}
    private static void RejectDuplicateProperties(string json){using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=16});Walk(doc.RootElement);static void Walk(JsonElement value){if(value.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in value.EnumerateObject()){if(!names.Add(p.Name))throw new InvalidDataException();Walk(p.Value);}}else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Walk(item);}}
    private static void RejectLinks(string path){for(string? current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Denetim yolu bağlantı içeremez.");}
}
