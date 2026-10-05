using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class RestoreCrashBoundaryTests
{
    [Theory]
    [InlineData("before-publish")]
    [InlineData("after-publish")]
    [InlineData("before-index")]
    [InlineData("after-index")]
    public async Task RestartRepairsSameArchiveTwiceWithoutDuplicate(string boundary)
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-restore-crash-" + Guid.NewGuid().ToString("N"));
        var storage = new ArchiveStorageManager(root);
        var catalog = new ArchiveCatalogService(storage, new ArchiveSearchIndex(root), new ArchivePlanStore(root), new FileHandleRegistry(), new JobManager(root));
        byte[] raw = "From: source@example.test\r\nTo: target@example.test\r\nSubject: fixture\r\n\r\noriginal body"u8.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(raw));
        string staging = storage.CreateStagingDirectory("seed");
        await File.WriteAllBytesAsync(Path.Combine(staging, "messages", "one.eml"), raw);
        var manifest = new ArchiveManifest
        {
            ArchiveId = "original", ArchiveName = "Original", CompanyId = "company_a", ProjectId = "project_a",
            SourceKind = "eml-files", Dialect = "eml", SourceFingerprint = hash, TotalItems = 1, TotalSizeBytes = raw.Length,
            Items = [new() { Ordinal = 1, ItemId = "one", RelativeEmlPath = "messages/one.eml", SourceSha256 = hash, StoredSha256 = hash, ByteLength = raw.Length, OriginalFolder = "Inbox" }]
        };
        await storage.WriteManifestAsync(staging, manifest, CancellationToken.None);
        storage.PublishFreshStagingToManagedArchive(staging, "original");
        string output = Path.Combine(root, "output");
        var backup = await new ArchiveBackupService(catalog).CreateAsync([new ArchiveScopeSelection("company_a", "project_a", "original")], output, CancellationToken.None);
        var actor = new AuthenticatedSessionPrincipal("admin", "session", 1);
        var interrupted = new ArchiveRestoreService(catalog);
        Func<string, CancellationToken, Task> fault = (_, _) => throw new IOException("Injected interruption");
        if (boundary == "before-publish") interrupted.BeforePublishAsync = fault;
        if (boundary == "after-publish") interrupted.AfterPublishAsync = fault;
        if (boundary == "before-index") interrupted.BeforeIndexAsync = fault;
        if (boundary == "after-index") interrupted.AfterIndexAsync = fault;
        await Assert.ThrowsAsync<IOException>(() => interrupted.RestoreAsync(Path.Combine(output, backup.FileName), "company_b", "project_b", actor, CancellationToken.None));
        string receiptPath = Assert.Single(Directory.GetFiles(Path.Combine(root, "restore-receipts"), "*.json"));
        string operationId = Path.GetFileNameWithoutExtension(receiptPath);
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
        string freshId = receipt.RootElement.GetProperty("entries")[0].GetProperty("freshArchiveId").GetString()!;
        for (int retry = 0; retry < 2; retry++)
        {
            var reopenedCatalog = new ArchiveCatalogService(new ArchiveStorageManager(root), new ArchiveSearchIndex(root), new ArchivePlanStore(root), new FileHandleRegistry(), new JobManager(root));
            var repaired = await new ArchiveRestoreService(reopenedCatalog).RepairAsync(operationId, actor, CancellationToken.None);
            Assert.Equal(RestoreReceiptState.Indexed, repaired.State);
            Assert.Equal(freshId, Assert.Single(repaired.FreshArchiveIds));
            Assert.Equal(1, repaired.MessageCount);
        }
        Assert.Equal(2, storage.GetAllArchiveManifests().Count);
        Assert.Equal(raw, await File.ReadAllBytesAsync(Path.Combine(storage.ArchivesBaseDirectory, "original", "messages", "one.eml")));
        Assert.Equal(raw, await File.ReadAllBytesAsync(Path.Combine(storage.ArchivesBaseDirectory, freshId, "messages", "one.eml")));
    }
}
