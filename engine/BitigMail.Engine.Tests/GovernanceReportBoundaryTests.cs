using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class GovernanceReportBoundaryTests
{
    private static string NewProfile() => Path.Combine(Path.GetTempPath(), "bitigmail-report-boundary-" + Guid.NewGuid().ToString("N"));
    private static ArchiveGovernanceService Open(string root)
    {
        var jobs = new JobManager(root);
        var catalog = new ArchiveCatalogService(new ArchiveStorageManager(root), new ArchiveSearchIndex(root), new ArchivePlanStore(root), new FileHandleRegistry(), jobs);
        return new(catalog, jobs);
    }

    [Fact]
    public void ReportHidesPrivateFieldsAndHashesVisibleChanges()
    {
        string root = NewProfile();
        Directory.CreateDirectory(Path.Combine(root, "jobs"));
        var job = new LocalJobRecord
        {
            JobId = "report-job", JobKind = "convert", Status = "completed", SourceKind = "eml-files",
            SourceFileName = "C:/private/person.eml", TargetFileName = "C:/private/archive.pst",
            ErrorMessage = "PRIVATE_EXCEPTION", QualificationWarnings = ["PRIVATE_SUBJECT C:/private/secret token=PRIVATE_TOKEN"],
            SelectedAttachmentsCount = 3, ItemsRead = 4, ItemsWritten = 4,
            ClientContext = new ClientProjectContext { CompanyId = "company", ProjectId = "project", CompanyName = "PRIVATE_COMPANY" }
        };
        string file = Path.Combine(root, "jobs", "report-job.json");
        void Save() => File.WriteAllText(file, JsonSerializer.Serialize(job, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Save();
        var first = Open(root).GetDeliveryReport(job.JobId);
        string serialized = JsonSerializer.Serialize(first);
        Assert.DoesNotContain("PRIVATE_", serialized);
        Assert.DoesNotContain("C:/private", serialized);
        Assert.Equal(3, first.AttachmentCount);
        Assert.NotEmpty(first.QualificationWarnings);
        job.DateFilterBlocked = true;
        Save();
        var second = Open(root).GetDeliveryReport(job.JobId);
        Assert.NotEqual(first.EvidenceSha256, second.EvidenceSha256);
        job.SelectedAttachmentsCount = 5;
        Save();
        var third = Open(root).GetDeliveryReport(job.JobId);
        Assert.NotEqual(second.EvidenceSha256, third.EvidenceSha256);
        Assert.Equal(5, third.AttachmentCount);
    }

    [Fact]
    public async Task RetentionPreviewPreservesManifestAndRawBytes()
    {
        string root = NewProfile();
        var storage = new ArchiveStorageManager(root);
        string staging = storage.CreateStagingDirectory("seed");
        byte[] raw = "original mail"u8.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(raw));
        await File.WriteAllBytesAsync(Path.Combine(staging, "messages", "one.eml"), raw);
        var manifest = new ArchiveManifest
        {
            ArchiveId = "original", ArchiveName = "Original", CompanyId = "company", ProjectId = "project",
            SourceKind = "eml-files", Dialect = "eml", SourceFingerprint = hash, TotalItems = 1, TotalSizeBytes = raw.Length,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddYears(-2),
            Items = [new() { Ordinal = 1, ItemId = "one", RelativeEmlPath = "messages/one.eml", SourceSha256 = hash, StoredSha256 = hash, ByteLength = raw.Length, OriginalFolder = "Inbox" }]
        };
        await storage.WriteManifestAsync(staging, manifest, CancellationToken.None);
        storage.PublishFreshStagingToManagedArchive(staging, "original");
        string archiveRoot = Path.Combine(storage.ArchivesBaseDirectory, "original");
        byte[] originalManifest = await File.ReadAllBytesAsync(Path.Combine(archiveRoot, "manifest.json"));
        var result = Open(root).PreviewRetention("company", "project", 365, null);
        Assert.Equal(1, result.CandidateCount);
        Assert.Contains("LEGAL_HOLD_NOT_SUPPORTED", result.Policy);
        Assert.Equal(raw, await File.ReadAllBytesAsync(Path.Combine(archiveRoot, "messages", "one.eml")));
        Assert.Equal(originalManifest, await File.ReadAllBytesAsync(Path.Combine(archiveRoot, "manifest.json")));
    }
}
