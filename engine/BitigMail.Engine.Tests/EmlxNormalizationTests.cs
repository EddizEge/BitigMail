using BitigMail.Engine.Storage;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class EmlxNormalizationTests
{
    private static string Corpus => Path.Combine(AppContext.BaseDirectory, "fixtures", "emlx-corpus-v1");

    [Fact]
    public void NormalizesAllPhysicalMessagesWithExactMimeAndOpaqueMetadata()
    {
        var service = new EmlxNormalizationService();
        var manifest = service.BuildDirectoryManifest(Path.Combine(Corpus, "complete"));
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-emlx-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var report = service.Normalize(manifest, output, "job-test", new Capacity(long.MaxValue));
        Assert.Equal(12, report.ItemsWritten);
        Assert.Equal(12, report.Entries.Select(e => e.OutputRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(11, report.Entries.Select(e => e.OutputSha256).Distinct().Count());
        foreach (var item in report.Entries)
        {
            var source = EmlxRecordReader.ReadFile(manifest.Entries.Single(e => e.PhysicalOrdinal == item.PhysicalOrdinal).CanonicalPath);
            Assert.Equal(source.RawMimeBytes, File.ReadAllBytes(Path.Combine(report.OutputPath, item.OutputRelativePath)));
            Assert.Equal(source.AppleMetadataBytes, File.ReadAllBytes(Path.Combine(report.OutputPath, item.MetadataRelativePath)));
        }
        Assert.True(File.Exists(Path.Combine(report.OutputPath, "bitigmail-emlx-manifest.json")));
    }

    [Fact]
    public void PartialAndTruncatedSourcesAreExplicitBlockers()
    {
        var service = new EmlxNormalizationService();
        Assert.Throws<InvalidDataException>(() => service.BuildDirectoryManifest(Path.Combine(Corpus, "negative")));
        Assert.Throws<InvalidDataException>(() => service.BuildFilesManifest([Path.Combine(Corpus, "negative", "truncated.emlx")]));
    }

    [Fact]
    public void ChangedSourceFailsWithoutPublishingFinalTree()
    {
        string source = Path.Combine(Path.GetTempPath(), "bitigmail-emlx-source", Guid.NewGuid().ToString("N"));
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-emlx-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source); Directory.CreateDirectory(output);
        string file = Path.Combine(source, "message.emlx");
        File.Copy(Path.Combine(Corpus, "complete", "Gelen Kutusu", "0001.emlx"), file);
        var service = new EmlxNormalizationService(); var manifest = service.BuildDirectoryManifest(source);
        File.AppendAllText(file, "changed");
        Assert.Throws<InvalidDataException>(() => service.Normalize(manifest, output, "job-changed", new Capacity(long.MaxValue)));
        Assert.Empty(Directory.GetDirectories(output));
    }

    [Fact]
    public void InsufficientDiskFailsBeforeOutputPublication()
    {
        var service = new EmlxNormalizationService();
        var manifest = service.BuildDirectoryManifest(Path.Combine(Corpus, "complete"));
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-emlx-output", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var ex = Assert.Throws<InvalidOperationException>(() => service.Normalize(manifest, output, "job-disk", new Capacity(1)));
        Assert.Contains("DİSK KAPASİTESİ ENGELİ", ex.Message); Assert.Empty(Directory.GetDirectories(output));
    }

    [Fact]
    public void OversizePhysicalFileIsRejectedBeforeContentAllocation()
    {
        string source = Path.Combine(Path.GetTempPath(), "bitigmail-emlx-oversize", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(source);
        string path = Path.Combine(source, "oversize.emlx");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(EmlxRecordReader.DefaultMaxMessageBytes + EmlxRecordReader.DefaultMaxMetadataBytes + 33L);
        var ex = Assert.Throws<InvalidDataException>(() => new EmlxNormalizationService().BuildDirectoryManifest(source));
        Assert.Contains("fiziksel dosyası", ex.Message);
    }

    [Fact]
    public async Task JobPublishesVerifiedReportWithFrozenOwner()
    {
        string runtime = Path.Combine(Path.GetTempPath(), "bitigmail-emlx-runtime", Guid.NewGuid().ToString("N"));
        string output = Path.Combine(Path.GetTempPath(), "bitigmail-emlx-output", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var manifest = new EmlxNormalizationService().BuildDirectoryManifest(Path.Combine(Corpus, "complete"));
        var owner = new ClientProjectContext { CompanyId = "company-original", CompanyName = "Şirket", ProjectId = "project-original", ProjectName = "Proje" };
        var manager = new JobManager(runtime, new Capacity(long.MaxValue));
        var started = manager.StartEmlxNormalizationJob(manifest, output, "emlx-idempotent", owner);
        owner.CompanyId = "mutated"; owner.ProjectId = "mutated";
        LocalJobRecord? done = null;
        for (int i = 0; i < 100; i++) { done = manager.GetJob(started.JobId); if (done?.Status is "completed" or "failed") break; await Task.Delay(25); }
        Assert.Equal("completed", done?.Status); Assert.Equal("company-original", done?.ClientContext.CompanyId); Assert.Equal("project-original", done?.ClientContext.ProjectId);
        var report = manager.GetReport(started.JobId); Assert.NotNull(report); Assert.True(report!.SourceHashMatch); Assert.Equal(12, report.ItemsWritten);
        var originalOwner = new ClientProjectContext { CompanyId = "company-original", CompanyName = "Şirket", ProjectId = "project-original", ProjectName = "Proje" };
        Assert.Equal(started.JobId, manager.StartEmlxNormalizationJob(manifest, output, "emlx-idempotent", originalOwner).JobId);
    }

    private sealed class Capacity(long bytes) : IDiskCapacityProbe
    { public DiskCapacityResult Probe(string directoryPath) => new(true, bytes, null); }
}
