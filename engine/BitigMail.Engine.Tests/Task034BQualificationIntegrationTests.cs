using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Bridge.Transfer;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class Task034BQualificationIntegrationTests
{
    [Fact]
    public void QualifiedEmlToPstPreviewBlocksDatesAndStartRejectsMutatedProvenance()
    {
        string root = Temp(); string eml = Path.Combine(root, "Inbox", "one.eml"); Directory.CreateDirectory(Path.GetDirectoryName(eml)!);
        File.WriteAllText(eml, "Date: Mon, 1 Jan 2024 00:00:00 +0000\r\nSubject: one\r\n\r\nbody\r\n", new UTF8Encoding(false));
        string xml = Path.Combine(root, "Inbox", "one.xml"); File.WriteAllText(xml, "<original/>");
        var item = new { OutputRelativePath = "Inbox/one.eml", OutputSha256 = Hash(eml), ProvenanceRelativePath = "Inbox/one.xml", OriginalXmlSha256 = Hash(xml) };
        File.WriteAllText(Path.Combine(root, "bitigmail-outlook-eml-manifest.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, Status = "completed_with_qualification", SourceFormat = "olm", MailItems = 1, Items = new[] { item } }));
        var source = new MimeSourceManifest { SourceKind = "eml-tree", Dialect = "rfc822", RootPath = root, AggregateFingerprint = "source-fingerprint", TotalFiles = 1, Entries = [new MimeSourceEntry { CanonicalPath = eml, RelativePath = "Inbox/one.eml", MappedFolder = "Inbox", SizeBytes = new FileInfo(eml).Length, Sha256 = Hash(eml), PhysicalOrdinal = 1 }] };
        var preflight = new PreflightCheckResult { CanConvert = true };
        var blocked = MimeSelectionEngine.EvaluateSelection(source, "msrc_test", null, "2024-01-01", null, preflight, CancellationToken.None);
        Assert.False(blocked.Preview.CanConvert); Assert.True(blocked.Preview.DateFilterBlocked); Assert.Contains("TARİH FİLTRESİ", blocked.Preview.BlockerReason);
        var allowed = MimeSelectionEngine.EvaluateSelection(source, "msrc_test", null, null, null, preflight, CancellationToken.None);
        Assert.True(allowed.Preview.CanConvert); Assert.NotEmpty(allowed.Preview.QualificationWarnings); Assert.NotNull(allowed.Registered.QualificationFingerprint);
        File.AppendAllText(xml, "changed");
        var jobs = new JobManager(Path.Combine(root, "runtime"));
        Assert.Throws<InvalidDataException>(() => jobs.StartMimeJob(source, Path.Combine(root, "out.pst"), "qualified", new ClientProjectContext(), allowed.Registered, runInBackground: false));
    }

    [Fact]
    public async Task QualifiedArchiveScopeRejectsDateSearchAndPreservesWarnings()
    {
        string root = Temp(); var storage = new ArchiveStorageManager(root); string staging = storage.CreateStagingDirectory("stage034b");
        var manifest = new ArchiveManifest { ArchiveId = "arc034b", ArchiveName = "Qualified", CompanyId = "company", ProjectId = "project", SourceKind = "eml-tree", Dialect = "rfc822", SourceFingerprint = "source", TotalItems = 0, TotalSizeBytes = 0, TruncatedItemsCount = 0, DateFilterBlocked = true, QualificationFingerprint = "qualification", QualificationWarnings = ["OLM tarih anlamı doğrulanmadı."] };
        await storage.WriteManifestAsync(staging, manifest, CancellationToken.None); storage.PublishStagingToManagedArchive(staging, manifest.ArchiveId);
        using var index = new ArchiveSearchIndex(root); var catalog = new ArchiveCatalogService(storage, index, new ArchivePlanStore(root), new FileHandleRegistry(), new JobManager(root));
        var scope = new ArchiveScopeTriple { CompanyId = "company", ProjectId = "project", ArchiveId = manifest.ArchiveId };
        Assert.Throws<ArchiveSearchPolicyException>(() => catalog.Search(new ArchiveSearchRequest { SelectedScopes = [scope], StartDate = "2024-01-01" }));
        var response = catalog.Search(new ArchiveSearchRequest { SelectedScopes = [scope] }); Assert.Contains("OLM tarih anlamı doğrulanmadı.", response.QualificationWarnings);
    }

    [Fact]
    public async Task BridgeHandleFreeResumeRejectsChangedNormalizationManifestBeforeAppend()
    {
        string root = Temp(); var (accounts, _, targetId, resolver) = BridgeTestHelpers.SetupTestAccounts(root); var handles = new FileHandleRegistry(); var journal = new BridgeTransferJournal(root);
        string sourceRoot = Path.Combine(root, "normalized"); string inbox = Path.Combine(sourceRoot, "Inbox"); Directory.CreateDirectory(inbox);
        string eml = BridgeTestHelpers.CreateEmlFile(inbox, "one.eml", "one", "Mon, 01 Jan 2024 10:00:00 +0300"); string xml = Path.Combine(inbox, "one.xml"); File.WriteAllText(xml, "<original/>");
        var entry = new { OutputRelativePath = "Inbox/one.eml", OutputSha256 = Hash(eml), ProvenanceRelativePath = "Inbox/one.xml", OriginalXmlSha256 = Hash(xml) };
        string normalizationManifest = Path.Combine(sourceRoot, "bitigmail-outlook-eml-manifest.json"); File.WriteAllText(normalizationManifest, JsonSerializer.Serialize(new { SchemaVersion = 1, Status = "completed_with_qualification", SourceFormat = "olm", MailItems = 1, Items = new[] { entry } }));
        var source = new MimeSourceInspector().BuildEmlDirectoryManifest(sourceRoot); string handle = handles.RegisterMimeSource(source, "qualified"); var fake = new BridgeFakeTransferClient(); var factory = new BridgeFakeClientFactory(fake);
        var preview = await new BridgeImportPreviewService(handles, accounts, factory, journal, resolver).CreatePreviewAsync(new() { CompanyId = "company-1", ProjectId = "project-1", SourceHandle = handle, TargetAccountId = targetId, SelectedFolders = ["Inbox"] });
        var plan = journal.GetImportPlan(preview.PreviewId)!; Assert.NotNull(plan.QualificationFingerprint); File.AppendAllText(xml, "changed");
        var job = new LocalJobRecord { JobId = "bridge-qualified-resume", ClientContext = new ClientProjectContext { CompanyId = "company-1", ProjectId = "project-1" } };
        var worker = new BridgeImportWorker(new FileHandleRegistry(), accounts, factory, journal, _ => { }, _ => { }, resolver); await worker.ExecuteAsync(job, plan);
        Assert.Equal("failed", job.Status); Assert.Contains("qualification", job.ErrorMessage, StringComparison.OrdinalIgnoreCase); Assert.Empty(fake.AppendedMessages);
    }

    [Fact]
    public void PopSnapshotReusePreservesWarningsAllowsRfcDatesAndRejectsHashTamper()
    {
        string root = Temp(); string popDir = Path.Combine(root, "POP"); Directory.CreateDirectory(popDir); string eml = Path.Combine(popDir, "000001.eml");
        File.WriteAllText(eml, "Date: Mon, 1 Jan 2024 00:00:00 +0000\r\nSubject: pop\r\n\r\nbody\r\n", new UTF8Encoding(false));
        var item = new { Sequence = 1, Uidl = "uid-1", RelativePath = "POP/000001.eml", SizeBytes = new FileInfo(eml).Length, Sha256 = Hash(eml), RfcMessageDate = "2024-01-01T00:00:00+00:00", MetadataQualification = "RFC_MESSAGE_DATE_ONLY; POP_INTERNALDATE_AND_READFLAG_UNAVAILABLE; RETR_STREAM_BYTES" };
        File.WriteAllText(Path.Combine(root, "bitigmail-pop-snapshot-manifest.json"), JsonSerializer.Serialize(new { Status = "completed_with_qualification", OutputPath = root, SnapshotSha256 = new string('a', 64), PlannedItems = 1, WrittenItems = 1, Items = new[] { item }, Warnings = Array.Empty<string>() }));
        var qualification = new NormalizedSourceQualificationReader().Read(root)!; Assert.False(qualification.DateFilterBlocked); Assert.Equal(1, qualification.VerifiedOutputCount); Assert.Contains(qualification.Warnings, x => x.Contains("internal date", StringComparison.OrdinalIgnoreCase));
        File.AppendAllText(eml, "tamper"); Assert.Throws<InvalidDataException>(() => new NormalizedSourceQualificationReader().Read(root));
    }

    [Fact]
    public void ManifestAwareEmlFilesIgnoreUnselectedQualifiedSibling()
    {
        string root = Temp(); string ordinary = Path.Combine(root, "ordinary.eml"); File.WriteAllText(ordinary, "Subject: ordinary\r\n\r\nbody\r\n");
        string generated = Path.Combine(root, "generated"); Directory.CreateDirectory(generated); WriteQualified(generated, "qualified.eml", "qualified.xml");
        var manifest = new MimeSourceInspector().BuildEmlFilesManifest([ordinary], "Inbox");
        Assert.Null(new NormalizedSourceQualificationReader().Read(manifest));
    }

    [Fact]
    public void SingleUnlistedEmlInsideGeneratedRootIsRejected()
    {
        string root = Temp(); WriteQualified(root, "declared.eml", "declared.xml"); string renamed = Path.Combine(root, "renamed.eml"); File.WriteAllText(renamed, "Subject: renamed\r\n\r\nbody\r\n");
        var manifest = new MimeSourceInspector().BuildEmlFilesManifest([renamed], "Inbox");
        Assert.Throws<InvalidDataException>(() => new NormalizedSourceQualificationReader().Read(manifest));
    }

    [Fact]
    public void ManifestAwareEmlFilesAggregateSelectedQualificationsAcrossParents()
    {
        string first = Path.Combine(Temp(), "first"), second = Path.Combine(Temp(), "second"); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        string one = WriteQualified(first, "one.eml", "one.xml"), two = WriteQualified(second, "two.eml", "two.xml");
        var manifest = new MimeSourceInspector().BuildEmlFilesManifest([one, two], "Inbox"); var qualification = new NormalizedSourceQualificationReader().Read(manifest)!;
        Assert.Equal(2, qualification.VerifiedOutputCount); Assert.True(qualification.DateFilterBlocked); Assert.NotEmpty(qualification.Fingerprint);
    }

    private static string Temp() { string path = Path.Combine(Path.GetTempPath(), "bitigmail-034b-", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string WriteQualified(string root, string emlName, string xmlName)
    {
        string eml = Path.Combine(root, emlName); string xml = Path.Combine(root, xmlName); File.WriteAllText(eml, "Subject: qualified\r\n\r\nbody\r\n"); File.WriteAllText(xml, "<original/>");
        var item = new { OutputRelativePath = emlName, OutputSha256 = Hash(eml), ProvenanceRelativePath = xmlName, OriginalXmlSha256 = Hash(xml) };
        File.WriteAllText(Path.Combine(root, "bitigmail-outlook-eml-manifest.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, Status = "completed_with_qualification", SourceFormat = "olm", MailItems = 1, Items = new[] { item } })); return eml;
    }
}
