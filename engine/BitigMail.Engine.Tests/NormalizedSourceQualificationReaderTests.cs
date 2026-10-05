using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BitigMail.Engine.Storage;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class NormalizedSourceQualificationReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bitigmail-qualification-" + Guid.NewGuid().ToString("N"));
    private readonly NormalizedSourceQualificationReader _reader = new();
    public NormalizedSourceQualificationReaderTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string Mail(string relative = "Inbox/one.eml")
    {
        string path = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Subject: İstanbul\r\n\r\nknown body\r\n", new UTF8Encoding(false)); return path;
    }
    private void Manifest(string relative = "Inbox/one.eml", string format = "olm", string status = "completed_with_qualification", int? schema = null, bool twice = false)
    {
        string path = Path.Combine(_root, "Inbox/one.eml");
        File.WriteAllText(Path.Combine(_root, "Inbox/one.xml"), "<original/>");
        var item = new { OutputRelativePath = relative, OutputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), ProvenanceRelativePath = "Inbox/one.xml", OriginalXmlSha256 = schema == 1 ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("<original/>"))) : null };
        var data = new Dictionary<string, object> { ["Status"] = status, ["SourceFormat"] = format, ["MailItems"] = twice ? 2 : 1, ["Items"] = twice ? new[] { item, item } : new[] { item } };
        if (schema.HasValue) data["SchemaVersion"] = schema.Value;
        File.WriteAllText(Path.Combine(_root, "bitigmail-outlook-eml-manifest.json"), JsonSerializer.Serialize(data));
    }
    [Fact] public void OrdinaryEmlDoesNotInventProvenance() { Mail(); Assert.Null(_reader.Read(_root)); }
    [Fact] public void SelectionInsideGeneratedTreeRetainsDateQualification()
    {
        string mail = Mail(); Manifest();
        var result = _reader.Read(mail)!;
        Assert.True(result.DateFilterBlocked); Assert.Equal(1, result.VerifiedOutputCount); Assert.False(result.IsPartial);
        Assert.Equal(result.Fingerprint, _reader.Read(mail)!.Fingerprint);
    }
    [Fact] public void ParentSelectionFindsNestedGeneratedTree()
    {
        Mail(); Manifest();
        string nested = Path.Combine(_root, "generated"); Directory.CreateDirectory(nested);
        Directory.Move(Path.Combine(_root, "Inbox"), Path.Combine(nested, "Inbox"));
        File.Move(Path.Combine(_root, "bitigmail-outlook-eml-manifest.json"), Path.Combine(nested, "bitigmail-outlook-eml-manifest.json"));
        var result = _reader.Read(_root)!; Assert.True(result.DateFilterBlocked); Assert.Single(result.Warnings);
    }
    [Fact] public void PartialPstRetainsPartialWarningWithoutOlmDateClaim()
    {
        Mail(); Manifest(format: "pst", status: "partially_completed_with_qualification");
        var result = _reader.Read(_root)!; Assert.True(result.IsPartial); Assert.False(result.DateFilterBlocked);
    }
    [Theory]
    [InlineData("../outside.eml")]
    [InlineData("Inbox/../one.eml")]
    [InlineData("Inbox/one.eml:stream")]
    [InlineData("C:\\outside.eml")]
    [InlineData("Inbox//one.eml")]
    public void RejectsUnsafeManifestPaths(string relative)
    { Mail(); Manifest(relative); Assert.Throws<InvalidDataException>(() => _reader.Read(_root)); }
    [Fact] public void ChangedOutputCannotRetainVerifiedQualification()
    { string path = Mail(); Manifest(); File.AppendAllText(path, "changed"); Assert.Throws<InvalidDataException>(() => _reader.Read(_root)); }
    [Fact] public void MissingOutputIsNotSilentlySkipped()
    { string path = Mail(); Manifest(); File.Delete(path); Assert.Throws<InvalidDataException>(() => _reader.Read(_root)); }
    [Fact] public void ExtraOutputIsNotSilentlyQualified()
    { Mail(); Manifest(); Mail("Inbox/orphan.eml"); Assert.Throws<InvalidDataException>(() => _reader.Read(_root)); }
    [Fact] public void DuplicatePhysicalOutputIsRejected()
    { Mail(); Manifest(twice: true); Assert.Throws<InvalidDataException>(() => _reader.Read(_root)); }
    [Fact] public void UnknownSchemaIsRejected()
    { Mail(); Manifest(schema: 2); Assert.Throws<InvalidDataException>(() => _reader.Read(_root)); }
    [Fact] public void DuplicateJsonPropertiesAreRejected()
    {
        Mail(); Manifest(); string path = Path.Combine(_root, "bitigmail-outlook-eml-manifest.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Status\":", "\"Status\":\"failed\",\"Status\":"));
        Assert.Throws<InvalidDataException>(() => _reader.Read(_root));
    }
    [Fact] public void EmlxMetadataQualificationAndHashesArePreserved()
    {
        string path = Mail();
        File.WriteAllText(Path.Combine(_root, "Inbox/one.plist"), "metadata");
        File.WriteAllText(Path.Combine(_root, "bitigmail-emlx-manifest.json"), JsonSerializer.Serialize(new {
            Status = "completed", ItemsWritten = 1, Entries = new[] { new { OutputRelativePath = "Inbox/one.eml", OutputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), MetadataRelativePath = "Inbox/one.plist", MetadataSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("metadata"))) } }
        }));
        Assert.True(_reader.Read(_root)!.DateFilterBlocked);
        File.AppendAllText(Path.Combine(_root, "Inbox/one.plist"), "changed");
        Assert.Throws<InvalidDataException>(() => _reader.Read(_root));
    }
    [Fact] public void MissingOlmProvenanceIsRejected()
    { Mail(); Manifest(); File.Delete(Path.Combine(_root, "Inbox/one.xml")); Assert.Throws<FileNotFoundException>(() => _reader.Read(_root)); }
    [Fact] public void ActualOlmOutputAndSingleMessageSelectionKeepQualification()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "fixtures", "vendor-olm", "SampleOLM.olm");
        var normalized = new OutlookToEmlNormalizationService().Normalize(source, _root, "reader-integration", new Capacity());
        var entire = _reader.Read(normalized.OutputPath)!;
        Assert.Equal(22, entire.VerifiedOutputCount); Assert.True(entire.DateFilterBlocked);
        var item = normalized.Items[0];
        var selected = _reader.Read(Path.Combine(normalized.OutputPath, item.OutputRelativePath))!;
        Assert.Equal(1, selected.VerifiedOutputCount); Assert.True(selected.DateFilterBlocked);
        File.AppendAllText(Path.Combine(normalized.OutputPath, item.ProvenanceRelativePath!), " ");
        Assert.Throws<InvalidDataException>(() => _reader.Read(Path.Combine(normalized.OutputPath, item.OutputRelativePath)));
    }
    private sealed class Capacity : IDiskCapacityProbe { public DiskCapacityResult Probe(string directoryPath) => new(true, long.MaxValue, null); }
    [Fact] public void CancellationIsObserved() { Mail(); Assert.Throws<OperationCanceledException>(() => _reader.Read(_root, new CancellationToken(true))); }
}
