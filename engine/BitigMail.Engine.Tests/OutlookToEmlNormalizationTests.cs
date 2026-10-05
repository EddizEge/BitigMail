using BitigMail.Engine.Storage;
using Xunit;
using System.Reflection;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Jobs;
using BitigMail.Engine.Archive;
using MimeKit;

namespace BitigMail.Engine.Tests;

public sealed class OutlookToEmlNormalizationTests
{
    [Fact]
    public async Task EmptyOutlookStoreDoesNotPublishCompletedJob()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-empty-outlook", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "empty.pst");
        using (Aspose.Email.Storage.Pst.PersonalStorage.Create(source, Aspose.Email.Storage.Pst.FileFormatVersion.Unicode)) { }
        string output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        string sourceHash;
        using (var input = File.OpenRead(source)) sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).ToLowerInvariant();
        var jobs = new JobManager(Path.Combine(root, "runtime"), new Capacity());
        var started = jobs.StartOutlookEmlJob(source, output, sourceHash, Guid.NewGuid().ToString("N"),
            new ClientProjectContext { CompanyId = "test-company", ProjectId = "test-project" }, false);
        LocalJobRecord? completed = null;
        for (int i = 0; i < 200; i++)
        {
            completed = jobs.GetJob(started.JobId);
            if (completed?.Status is "completed" or "failed") break;
            await Task.Delay(25);
        }
        Assert.Equal("failed", completed?.Status);
        var report = jobs.GetReport(started.JobId);
        Assert.NotNull(report);
        Assert.False(report!.ConversionSuccess);
        Assert.Equal(0, report.ItemsWritten);
        Assert.Equal("failed", report.OverallStatus);
    }

    [Theory]
    [InlineData("lab/ost-spike/input/bitigmail-lab-full.ost")]
    [InlineData("lab/ost-spike/output/genuine-full-converted-09.pst")]
    public void HealthyPstAndOstProduceQualifiedEmlTree(string relative)
    {
        string root = AppContext.BaseDirectory; while (!Directory.Exists(Path.Combine(root, "engine"))) root = Directory.GetParent(root)!.FullName;
        string source = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)); string output = Path.Combine(Path.GetTempPath(), "bitigmail-outlook-eml", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var report = new OutlookToEmlNormalizationService().Normalize(source, output, Guid.NewGuid().ToString("N"), new Capacity());
        Assert.Equal(13, report.MailItems); Assert.Equal(0, report.FailedItems); Assert.Equal(report.SourceSha256Before, report.SourceSha256After);
        // Exact original names from task011-astra-source-oracle.json; do not translate or case-fold them.
        Assert.Equal(6, report.Items.Count(item => item.FolderPath.EndsWith("/Gelen Kutusu", StringComparison.Ordinal)));
        Assert.Equal(3, report.Items.Count(item => item.FolderPath.EndsWith("/Gönderilenler", StringComparison.Ordinal)));
        Assert.Equal(4, report.Items.Count(item => item.FolderPath.EndsWith("/Projeler/İstanbul", StringComparison.Ordinal)));
        Assert.Equal(4, report.Items.Sum(item => item.AttachmentCount));
        Assert.All(report.Items, item =>
        {
            Assert.NotNull(item.Fidelity);
            Assert.Equal(item.Fidelity!.SourceAttachments, item.Fidelity.ExactAttachments);
            Assert.True(item.Fidelity.ExactFields.Contains("subject") || item.Fidelity.QualifiedDifferences.Contains("subject:representation-difference"));
            Assert.Contains("attachment-payloads", item.Fidelity.ExactFields);
        });
        using var oracle = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "lab", "ost-spike", "output", "task011-astra-source-oracle.json")));
        var expectedAttachments = oracle.RootElement.GetProperty("messages").EnumerateArray()
            .SelectMany(m => m.GetProperty("attachments").EnumerateArray())
            .Select(a => $"{a.GetProperty("name").GetString()}\0{a.GetProperty("bytes").GetInt64()}\0{a.GetProperty("sha256").GetString()}")
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var actualAttachments = new List<string>();
        foreach (var item in report.Items)
        {
            using var emitted = MimeMessage.Load(Path.Combine(report.OutputPath, item.OutputRelativePath));
            foreach (var part in emitted.BodyParts.OfType<MimePart>().Where(p => !string.IsNullOrEmpty(p.FileName) || p.IsAttachment || !string.IsNullOrEmpty(p.ContentId)))
            {
                using var decoded = new MemoryStream();
                (part.Content ?? throw new InvalidDataException("Fixture attachment content missing")).DecodeTo(decoded);
                actualAttachments.Add($"{part.FileName}\0{decoded.Length}\0{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(decoded.ToArray()))}");
            }
        }
        Assert.Equal(expectedAttachments, actualAttachments.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.All(report.Items, item => Assert.True(File.Exists(Path.Combine(report.OutputPath, item.OutputRelativePath))));
    }
    [Fact]
    public void VendorOlmRequiresExactOneToOneIdentityAndPreservesSource()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "fixtures", "vendor-olm", "SampleOLM.olm");
        string before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-outlook-eml", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var report = new OutlookToEmlNormalizationService().Normalize(source, output, "olm-test", new Capacity());
        Assert.Equal(22, report.MailItems); Assert.Equal(3, report.ExcludedNonMailItems);
        Assert.Equal(38, report.Items.Sum(i => i.RawAttachmentsRestored));
        Assert.All(report.Items, item => Assert.Contains("DATE_FIDELITY_UNRESOLVED", item.Qualification));
        Assert.All(report.Items, item =>
        {
            Assert.NotNull(item.Fidelity);
            Assert.Contains("date:semantic-fidelity-unresolved-source-literals-preserved", item.Fidelity!.QualifiedDifferences);
            Assert.False(string.IsNullOrWhiteSpace(item.Fidelity.OutputDateValue));
            Assert.True(!string.IsNullOrWhiteSpace(item.Fidelity.SourceSentTimeLiteral) || !string.IsNullOrWhiteSpace(item.Fidelity.SourceReceivedTimeLiteral));
        });
        int diagnosticNoonDiscrepancies = report.Items.Count(item =>
        {
            if (!DateTimeOffset.TryParse(item.Fidelity!.OutputDateValue, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var outputDate) ||
                !DateTime.TryParse(item.Fidelity.SourceSentTimeLiteral, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var sourceLiteral)) return false;
            var hypotheticalUtc = DateTime.SpecifyKind(sourceLiteral, DateTimeKind.Utc);
            return Math.Abs((outputDate.UtcDateTime - hypotheticalUtc).TotalHours + 12) < 0.001;
        });
        Assert.Equal(4, diagnosticNoonDiscrepancies); // Diagnostic only: source literal has no offset; no timezone correction is authorized.
        Assert.Equal(before, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        var sidecars = Directory.GetFiles(report.OutputPath, "*.olm-source.xml", SearchOption.AllDirectories); Assert.Equal(22, sidecars.Length);
        Assert.All(sidecars, path => Assert.True(new FileInfo(path).Length > 0));
    }
    private sealed class Capacity : IDiskCapacityProbe { public DiskCapacityResult Probe(string directoryPath) => new(true, long.MaxValue, null); }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void MaliciousFolderComponentsAreRejectedBeforeWrite(string component)
    {
        var method = typeof(OutlookToEmlNormalizationService).GetMethod("EncodeComponent", BindingFlags.NonPublic | BindingFlags.Static)!;
        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [component]));
        Assert.IsType<InvalidDataException>(ex.InnerException);
    }

    [Fact]
    public async Task QueuedSourceMutationFailsBeforePublishingAndOwnerIsFrozen()
    {
        string original = Path.Combine(AppContext.BaseDirectory, "fixtures", "vendor-olm", "SampleOLM.olm");
        string sourceDir = Path.Combine(Path.GetTempPath(), "bitigmail-queued-source", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sourceDir);
        string source = Path.Combine(sourceDir, "source.olm"); File.Copy(original, source);
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-queued-output", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        string runtime = Path.Combine(Path.GetTempPath(), "bitigmail-queued-runtime", Guid.NewGuid().ToString("N"));
        var manager = new JobManager(runtime, new Capacity());
        typeof(JobManager).GetField("_activeRunningJobId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(manager, "blocker");
        string hash; using (var fs = File.OpenRead(source)) hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)).ToLowerInvariant();
        var owner = new ClientProjectContext { CompanyId = "company", ProjectId = "project", CompanyName = "Şirket", ProjectName = "Proje" };
        var queued = manager.StartOutlookEmlJob(source, output, hash, "queued-source-change", owner, true); owner.CompanyId = "mutated";
        File.AppendAllText(source, "changed");
        typeof(JobManager).GetField("_activeRunningJobId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(manager, null);
        typeof(JobManager).GetMethod("CompleteAndDispatch", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, ["blocker"]);
        LocalJobRecord? done = null; for (int i = 0; i < 100; i++) { done = manager.GetJob(queued.JobId); if (done?.Status == "failed") break; await Task.Delay(25); }
        Assert.Equal("failed", done?.Status); Assert.Equal("company", done?.ClientContext.CompanyId); Assert.Empty(Directory.GetDirectories(output));
    }

    [Fact]
    public void AttachedMessageInlineAndOrdinaryHaveCountParityAndUnknownEmbeddedSize()
    {
        var nested = new MimeMessage(); nested.Subject = "attached"; nested.Body = new TextPart("plain") { Text = "inside" };
        var attached = new MessagePart { Message = nested, ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "attached.eml" } };
        var inline = new MimePart("image", "png") { FileName = "logo.png", ContentId = "logo", ContentDisposition = new ContentDisposition(ContentDisposition.Inline), Content = new MimeContent(new MemoryStream([1,2,3])) };
        var ordinary = new MimePart("application", "pdf") { FileName = "doc.pdf", ContentDisposition = new ContentDisposition(ContentDisposition.Attachment), Content = new MimeContent(new MemoryStream([4,5])) };
        var message = new MimeMessage(); message.Subject = "outer"; message.Body = new Multipart("mixed") { new TextPart("plain") { Text = "body" }, attached, inline, ordinary };
        Assert.Equal(3, MimeAttachmentInventory.CountTopLevelAttachments(message));
        using var bytes = new MemoryStream(); message.WriteTo(bytes); bytes.Position = 0; var parsed = ArchiveMimeParser.Parse(bytes, bytes.Length);
        Assert.Equal(3, parsed.Attachments.Count); Assert.Null(parsed.Attachments.Single(a => a.FileName == "attached.eml").SizeBytes);
    }

    [Fact]
    public void OlmIdentityCardinalityRejectsAmbiguousMissingAndCaseChangedJoins()
    {
        var method = typeof(OutlookToEmlNormalizationService).GetMethod("ValidateOlmIdentityCardinality", BindingFlags.NonPublic | BindingFlags.Static)!;
        static TargetInvocationException Reject(MethodInfo method, Dictionary<string, int> raw, Dictionary<string, int> sdk) =>
            Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [raw, sdk]));
        Assert.IsType<InvalidDataException>(Reject(method, new() { ["/A\0id"] = 2 }, new() { ["/A\0id"] = 1 }).InnerException);
        Assert.IsType<InvalidDataException>(Reject(method, new() { ["/A\0id"] = 1 }, new()).InnerException);
        Assert.IsType<InvalidDataException>(Reject(method, new() { ["/A\0id"] = 1 }, new() { ["/a\0id"] = 1 }).InnerException);
    }

    [Fact]
    public void PerItemFailurePublishesOnlyVerifiedSuccessesWithExplicitFailureIdentity()
    {
        string root = AppContext.BaseDirectory; while (!Directory.Exists(Path.Combine(root, "engine"))) root = Directory.GetParent(root)!.FullName;
        string source = Path.Combine(root, "lab", "ost-spike", "output", "genuine-full-converted-09.pst");
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-outlook-partial", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var service = new OutlookToEmlNormalizationService { BeforeItemWriteHook = (ordinal, _) => ordinal == 2 ? new IOException("deterministic item failure") : null };
        var report = service.Normalize(source, output, "partial-test", new Capacity());
        Assert.Equal("partially_completed_with_qualification", report.Status);
        Assert.Equal(12, report.MailItems); Assert.Equal(1, report.FailedItems); Assert.Single(report.Failures);
        Assert.Equal(2, report.Failures[0].PhysicalOrdinal); Assert.False(string.IsNullOrWhiteSpace(report.Failures[0].SourceIdentity));
        Assert.Equal(12, Directory.GetFiles(report.OutputPath, "*.eml", SearchOption.AllDirectories).Length);
        Assert.DoesNotContain(report.Items, item => item.PhysicalOrdinal == 2);
        Assert.All(report.Items, item => { Assert.NotNull(item.Fidelity); Assert.Equal(item.Fidelity!.SourceAttachments, item.Fidelity.ExactAttachments); });
    }

    [Fact]
    public async Task PartialItemFailureIsExposedAsPartiallyCompletedJobAndReport()
    {
        string root = AppContext.BaseDirectory; while (!Directory.Exists(Path.Combine(root, "engine"))) root = Directory.GetParent(root)!.FullName;
        string source = Path.Combine(root, "lab", "ost-spike", "output", "genuine-full-converted-09.pst");
        string work = Path.Combine(Path.GetTempPath(), "bitigmail-outlook-partial-job", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        string output = Path.Combine(work, "output"); Directory.CreateDirectory(output);
        string hash; using (var input = File.OpenRead(source)) hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).ToLowerInvariant();
        var manager = new JobManager(Path.Combine(work, "runtime"), new Capacity());
        manager.OutlookEmlServiceFactory = () => new OutlookToEmlNormalizationService { BeforeItemWriteHook = (ordinal, _) => ordinal == 3 ? new IOException("deterministic item failure") : null };
        var started = manager.StartOutlookEmlJob(source, output, hash, Guid.NewGuid().ToString("N"), new ClientProjectContext { CompanyId = "company", ProjectId = "project" }, false);
        LocalJobRecord? done = null; for (int i = 0; i < 200; i++) { done = manager.GetJob(started.JobId); if (done?.Status is "partially_completed" or "failed") break; await Task.Delay(25); }
        Assert.Equal("partially_completed", done?.Status); Assert.Equal(1, done?.FailedItems); Assert.Equal(12, done?.ItemsWritten);
        var report = manager.GetReport(started.JobId); Assert.NotNull(report); Assert.False(report!.ConversionSuccess);
        Assert.Equal("partially_completed_with_qualification", report.OverallStatus); Assert.Single(report.Errors);
        Assert.Equal(12, report.ReopenedPstVerification.TotalPhysicalItemsFound); Assert.False(report.ReopenedPstVerification.ItemCountMatch);
    }

    [Fact]
    public void OlmFailureAfterEmlWriteRemovesBothOrphanEmlAndProvenance()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "fixtures", "vendor-olm", "SampleOLM.olm");
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-olm-provenance-failure", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var service = new OutlookToEmlNormalizationService { BeforeProvenanceWriteHook = (ordinal, _) => ordinal == 2 ? new IOException("deterministic provenance failure") : null };
        var report = service.Normalize(source, output, "provenance-failure", new Capacity());
        Assert.Equal("partially_completed_with_qualification", report.Status); Assert.Equal(21, report.MailItems); Assert.Equal(1, report.FailedItems);
        Assert.Equal(21, Directory.GetFiles(report.OutputPath, "*.eml", SearchOption.AllDirectories).Length);
        Assert.Equal(21, Directory.GetFiles(report.OutputPath, "*.olm-source.xml", SearchOption.AllDirectories).Length);
        Assert.DoesNotContain(report.Items, item => item.PhysicalOrdinal == 2);
        Assert.Equal(2, Assert.Single(report.Failures).PhysicalOrdinal);
    }

    [Fact]
    public void EmptyOrUnknownSourceAttachmentPayloadCannotMatchUnexpectedOutput()
    {
        Assert.False(OutlookToEmlNormalizationService.AttachmentPayloadsMatch([], [[1, 2, 3]], false));
        Assert.False(OutlookToEmlNormalizationService.AttachmentPayloadsMatch([], [], true));
        Assert.True(OutlookToEmlNormalizationService.AttachmentPayloadsMatch([], [], false));
    }
}
