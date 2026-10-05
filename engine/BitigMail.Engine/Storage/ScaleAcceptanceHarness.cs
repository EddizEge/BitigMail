using System.Security.Cryptography;
using System.Text.Json;
using MimeKit;

namespace BitigMail.Engine.Storage;

public sealed record ScaleHarnessPlan(string TargetDirectory, long SourceBytes, long RequiredBytes, long? AvailableBytes, bool CanExecute, string Status, IReadOnlyList<string> SourceSha256);
public sealed record ScaleFixtureExpected(string FileName,string Folder,string? DateUtc,string Flags,IReadOnlyList<string> AttachmentSha256);

public sealed class ScaleAcceptanceHarness
{
    public ScaleHarnessPlan Plan(string targetDirectory, IEnumerable<string> sourceFiles, IDiskCapacityProbe probe)
    {
        string target = Path.GetFullPath(targetDirectory); if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Ölçek hedef dizini bulunamadı.");
        var files = sourceFiles.Select(Path.GetFullPath).ToArray(); if (files.Length == 0 || files.Any(path => !File.Exists(path))) throw new FileNotFoundException("Ölçek kaynak kümesi eksik.");
        long bytes = files.Sum(path => new FileInfo(path).Length), required = checked(bytes * 3 + 256L * 1024 * 1024); var capacity = probe.Probe(target);
        string? blocker = DiskCapacityPlanning.CapacityBlocker(required, capacity);
        return new(target, bytes, required, capacity.IsAvailable ? capacity.AvailableBytes : null, blocker is null, blocker is null ? "PLANNED_NOT_EXECUTED" : blocker, files.Select(Hash).ToArray());
    }
    public (int Messages, int Attachments, int Dated) SelfCheckEml(IEnumerable<string> files)
    {
        int messages=0,attachments=0,dated=0; foreach(string file in files){using var stream=File.OpenRead(file);using var message=MimeMessage.Load(stream);messages++;attachments+=message.Attachments.Count();if(message.Date!=DateTimeOffset.MinValue)dated++;} return(messages,attachments,dated);
    }
    public void VerifySourcesUnchanged(ScaleHarnessPlan plan,IEnumerable<string> files){var current=files.Select(Path.GetFullPath).Select(Hash).ToArray();if(!current.SequenceEqual(plan.SourceSha256,StringComparer.Ordinal))throw new InvalidDataException("Ölçek kaynak kümesi planlamadan sonra değişti.");}
    public void VerifyFixture(IEnumerable<(string Path,string Folder)> files,IReadOnlyList<ScaleFixtureExpected> expected)
    {var actual=new List<ScaleFixtureExpected>();foreach(var source in files){using var stream=File.OpenRead(source.Path);using var message=MimeMessage.Load(stream);var hashes=message.Attachments.Select(entity=>{using var ms=new MemoryStream();entity.WriteTo(ms);return Convert.ToHexString(SHA256.HashData(ms.ToArray()));}).OrderBy(x=>x,StringComparer.Ordinal).ToArray();actual.Add(new(Path.GetFileName(source.Path),source.Folder,message.Date==DateTimeOffset.MinValue?null:message.Date.UtcDateTime.ToString("O"),"UNKNOWN_NOT_IN_EML",hashes));}if(!JsonSerializer.Serialize(actual.OrderBy(x=>x.FileName)).Equals(JsonSerializer.Serialize(expected.OrderBy(x=>x.FileName)),StringComparison.Ordinal))throw new InvalidDataException("Küçük ölçek fixture oracle değerleri uyuşmuyor.");}
    private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
}
