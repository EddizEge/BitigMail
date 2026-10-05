using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class RestorePackageBoundaryTests
{
    private static async Task<(string Root, ArchiveStorageManager Storage, ArchiveCatalogService Catalog, string Package)> Seed()
    {
        string root = Path.Combine(Path.GetTempPath(), "bitigmail-package-boundary-" + Guid.NewGuid().ToString("N"));
        var storage = new ArchiveStorageManager(root);
        var catalog = new ArchiveCatalogService(storage, new ArchiveSearchIndex(root), new ArchivePlanStore(root), new FileHandleRegistry(), new JobManager(root));
        foreach (string id in new[] { "first", "second" })
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes($"From: {id}@example.test\r\nSubject: fixture\r\n\r\n{id}");
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            string staging = storage.CreateStagingDirectory(id);
            await File.WriteAllBytesAsync(Path.Combine(staging, "messages", "one.eml"), bytes);
            await storage.WriteManifestAsync(staging, new ArchiveManifest
            {
                ArchiveId = id, ArchiveName = id, CompanyId = "company_" + id, ProjectId = "project_" + id,
                SourceKind = "eml-files", Dialect = "eml", SourceFingerprint = hash, TotalItems = 1, TotalSizeBytes = bytes.Length,
                Items = [new() { Ordinal = 1, ItemId = "one", RelativeEmlPath = "messages/one.eml", SourceSha256 = hash, StoredSha256 = hash, ByteLength = bytes.Length, OriginalFolder = "Inbox" }]
            }, CancellationToken.None);
            storage.PublishFreshStagingToManagedArchive(staging, id);
        }
        string output = Path.Combine(root, "output");
        var backup = await new ArchiveBackupService(catalog).CreateAsync([
            new ArchiveScopeSelection("company_first", "project_first", "first"),
            new ArchiveScopeSelection("company_second", "project_second", "second")], output, CancellationToken.None);
        return (root, storage, catalog, Path.Combine(output, backup.FileName));
    }

    [Fact]
    public async Task TwoCompaniesRestoreIntoEmptyDestinationWithExactBytes()
    {
        var fixture = await Seed();
        var original = fixture.Storage.GetAllArchiveManifests().ToDictionary(x => x.ArchiveName, x => File.ReadAllBytes(Path.Combine(fixture.Storage.ArchivesBaseDirectory, x.ArchiveId, "messages", "one.eml")));
        var result = await new ArchiveRestoreService(fixture.Catalog).RestoreAsync(fixture.Package, "company_third", "project_third", new("admin", "session", 1), CancellationToken.None);
        Assert.Equal(RestoreReceiptState.Indexed, result.State);
        Assert.Equal(2, result.FreshArchiveIds.Count);
        Assert.Equal(2, result.MessageCount);
        Assert.Equal(4, fixture.Storage.GetAllArchiveManifests().Count);
        foreach (string id in result.FreshArchiveIds)
        {
            var manifest = fixture.Storage.GetArchiveManifest(id)!;
            Assert.Equal("company_third", manifest.CompanyId);
            Assert.Equal("project_third", manifest.ProjectId);
            Assert.Equal(original[manifest.ArchiveName], await File.ReadAllBytesAsync(Path.Combine(fixture.Storage.ArchivesBaseDirectory, id, "messages", "one.eml")));
        }
        foreach (string id in new[] { "first", "second" })
            Assert.Equal(original[id], await File.ReadAllBytesAsync(Path.Combine(fixture.Storage.ArchivesBaseDirectory, id, "messages", "one.eml")));
    }

    [Theory]
    [InlineData("tamper")]
    [InlineData("tamper-second")]
    [InlineData("extra")]
    [InlineData("duplicate-property")]
    [InlineData("null-archives")]
    public async Task InvalidPackageNeverPublishes(string mutation)
    {
        var fixture = await Seed();
        using (var zip = ZipFile.Open(fixture.Package, ZipArchiveMode.Update))
        {
            if (mutation == "extra")
            {
                using var writer = new StreamWriter(zip.CreateEntry("unlisted.txt").Open());
                writer.Write("unexpected");
            }
            else if (mutation is "tamper" or "tamper-second")
            {
                var rawEntries = zip.Entries.Where(x => x.FullName.EndsWith(".eml", StringComparison.Ordinal)).ToArray();
                var entry = mutation == "tamper-second" ? rawEntries.Last() : rawEntries.First();
                using var stream = entry.Open();
                int value = stream.ReadByte();
                stream.Position = 0;
                stream.WriteByte((byte)(value ^ 1));
            }
            else
            {
                var entry = zip.GetEntry("manifest.json")!;
                string text;
                using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
                if (mutation == "duplicate-property") text = text.Insert(text.IndexOf('{') + 1, "\"version\":1,");
                else { var node = JsonNode.Parse(text)!; node["archives"] = null; text = node.ToJsonString(); }
                entry.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
                writer.Write(text);
            }
        }
        Exception? error = await Record.ExceptionAsync(() => new ArchiveRestoreService(fixture.Catalog).RestoreAsync(fixture.Package, "company_third", "project_third", new("admin", "session", 1), CancellationToken.None));
        Assert.NotNull(error);
        Assert.IsNotType<NullReferenceException>(error);
        Assert.Equal(2, fixture.Storage.GetAllArchiveManifests().Count);
        string receipts = Path.Combine(fixture.Root, "restore-receipts");
        if (Directory.Exists(receipts))
            foreach (string receipt in Directory.GetFiles(receipts, "*.json"))
                await Assert.ThrowsAsync<InvalidDataException>(() => new ArchiveRestoreService(fixture.Catalog).RepairAsync(Path.GetFileNameWithoutExtension(receipt), new("admin", "session", 1), CancellationToken.None));
        Assert.Equal(2, fixture.Storage.GetAllArchiveManifests().Count);
    }
}
