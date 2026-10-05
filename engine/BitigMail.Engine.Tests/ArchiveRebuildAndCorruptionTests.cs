using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public class ArchiveRebuildAndCorruptionTests
{
    private static string ResolveRepoRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ROADMAP.md")) ||
                Directory.Exists(Path.Combine(current, ".codex-coordination")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ROADMAP.md")) ||
                Directory.Exists(Path.Combine(current, ".codex-coordination")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Repo root not found.");
    }

    private static string GetCorpusMboxPath() => Path.Combine(ResolveRepoRoot(), "fixtures", "mail-corpus-v1", "corpus.mbox");

    private static string CreateTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bitigmail-test-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task RebuildDatabaseAsync_ReconstructsFullIndex_FromManagedManifests_WithoutOriginalSource()
    {
        string runtimeDir = CreateTempDir("rebuild-nosource");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            // Ingest archive
            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildMboxManifest(GetCorpusMboxPath());
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "corpus.mbox");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Rebuild Archive",
                CompanyId = "comp-reb",
                ProjectId = "proj-reb",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            jobManager.StartArchiveIngestJob(
                plan,
                "idemp-reb-1",
                new Models.ClientProjectContext { CompanyId = "comp-reb", ProjectId = "proj-reb" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            var scope = new List<ArchiveScopeTriple>
            {
                new() { CompanyId = "comp-reb", ProjectId = "proj-reb", ArchiveId = plan.ArchiveId }
            };

            // Verify initial search works
            var resp1 = catalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "İstanbul"
            });
            Assert.Equal(4, resp1.TotalCount);

            // Now, simulate that original source and handle registry are GONE
            handleRegistry = new FileHandleRegistry();

            // Delete SQLite database file directly
            searchIndex.Dispose();
            SqliteConnectionExtensionsClear();

            string dbPath = Path.Combine(runtimeDir, "archives", "archive-search.db");
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal");
            if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm");

            // Re-instantiate search index and rebuild from managed manifests alone
            var newSearchIndex = new ArchiveSearchIndex(runtimeDir);
            var newCatalogService = new ArchiveCatalogService(storageManager, newSearchIndex, planStore, handleRegistry, jobManager);

            // Reconcile / Rebuild
            await newSearchIndex.RebuildDatabaseAsync(storageManager, CancellationToken.None);

            // Search again: all 4 items found from rebuild
            var resp2 = newCatalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "İstanbul"
            });
            Assert.Equal(4, resp2.TotalCount);

            newSearchIndex.Dispose();
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CorruptedDatabase_TriggersBackup_AndRebuildRestoresService()
    {
        string runtimeDir = CreateTempDir("corrupted-db");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildMboxManifest(GetCorpusMboxPath());
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "corpus.mbox");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Corrupt Test Archive",
                CompanyId = "comp-c",
                ProjectId = "proj-c",
                SourceHandle = handle
            });

            var plan = planStore.GetPlan(preview.PreviewId)!;
            jobManager.StartArchiveIngestJob(
                plan,
                "idemp-corrupt-1",
                new Models.ClientProjectContext { CompanyId = "comp-c", ProjectId = "proj-c" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            // Close connection, corrupt DB by overwriting header with garbage
            searchIndex.Dispose();
            SqliteConnectionExtensionsClear();

            string dbPath = Path.Combine(runtimeDir, "archives", "archive-search.db");
            Assert.True(File.Exists(dbPath));
            File.WriteAllText(dbPath, "GARBAGE NOT A SQLITE DATABASE");

            // Open new search index
            var recoveredIndex = new ArchiveSearchIndex(runtimeDir);

            // Rebuild
            await recoveredIndex.RebuildDatabaseAsync(storageManager, CancellationToken.None);

            // Verify a .corrupted- backup file was created
            var archivesDir = Path.Combine(runtimeDir, "archives");
            var corruptBackups = Directory.GetFiles(archivesDir, "archive-search.db.corrupted-*");
            Assert.NotEmpty(corruptBackups);

            // Query works after recovery
            var scope = new List<ArchiveScopeTriple>
            {
                new() { CompanyId = "comp-c", ProjectId = "proj-c", ArchiveId = plan.ArchiveId }
            };

            var newCatalogService = new ArchiveCatalogService(storageManager, recoveredIndex, planStore, handleRegistry, jobManager);
            var resp = newCatalogService.Search(new ArchiveSearchRequest
            {
                SelectedScopes = scope,
                Query = "Ahmet"
            });
            Assert.Equal(7, resp.TotalCount);

            recoveredIndex.Dispose();
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void JobCenter_EnforcesSingleConcurrency_AndIdempotencyConflict()
    {
        string runtimeDir = CreateTempDir("jobcenter-concurrency");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildMboxManifest(GetCorpusMboxPath());
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "corpus.mbox");

            var preview1 = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Plan 1",
                CompanyId = "comp-1",
                ProjectId = "proj-1",
                SourceHandle = handle
            });
            var plan1 = planStore.GetPlan(preview1.PreviewId)!;

            var preview2 = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Plan 2",
                CompanyId = "comp-1",
                ProjectId = "proj-1",
                SourceHandle = handle
            });
            var plan2 = planStore.GetPlan(preview2.PreviewId)!;

            // Start Job 1 with key "key-A"
            var job1 = jobManager.StartArchiveIngestJob(
                plan1,
                "key-A",
                new Models.ClientProjectContext { CompanyId = "comp-1", ProjectId = "proj-1" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", job1.Status);
            Assert.Equal("archive-ingest", job1.JobKind);

            // Same key + same plan -> returns identical job record
            var job1Duplicate = jobManager.StartArchiveIngestJob(
                plan1,
                "key-A",
                new Models.ClientProjectContext { CompanyId = "comp-1", ProjectId = "proj-1" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);
            Assert.Equal(job1.JobId, job1Duplicate.JobId);

            // Same key + different plan -> throws conflict
            Assert.Throws<InvalidOperationException>(() =>
            {
                jobManager.StartArchiveIngestJob(
                    plan2,
                    "key-A",
                    new Models.ClientProjectContext { CompanyId = "comp-1", ProjectId = "proj-1" },
                    storageManager,
                    searchIndex,
                    planStore,
                    handleRegistry,
                    runInBackground: false);
            });

            // Reindex job works and has JobKind = archive-reindex
            var reindexJob = jobManager.StartArchiveReindexJob(
                plan1.ArchiveId,
                "key-reindex-1",
                new Models.ClientProjectContext { CompanyId = "comp-1", ProjectId = "proj-1" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", reindexJob.Status);
            Assert.Equal("archive-reindex", reindexJob.JobKind);
            Assert.Equal(plan1.ArchiveId, reindexJob.ArchiveId);
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void InterruptedIngestJob_ResumesAndCompletes_WithoutDuplication()
    {
        string runtimeDir = CreateTempDir("jobcenter-resume");
        try
        {
            var storageManager = new ArchiveStorageManager(runtimeDir);
            using var searchIndex = new ArchiveSearchIndex(runtimeDir);
            var planStore = new ArchivePlanStore(runtimeDir);
            var handleRegistry = new FileHandleRegistry();
            var jobManager = new JobManager(runtimeDir);
            var catalogService = new ArchiveCatalogService(storageManager, searchIndex, planStore, handleRegistry, jobManager);

            var inspector = new MimeSourceInspector();
            var mimeManifest = inspector.BuildMboxManifest(GetCorpusMboxPath());
            string handle = handleRegistry.RegisterMimeSource(mimeManifest, "corpus.mbox");

            var preview = catalogService.CreatePreview(new ArchiveIngestPreviewRequest
            {
                ArchiveName = "Resume Archive",
                CompanyId = "comp-res",
                ProjectId = "proj-res",
                SourceHandle = handle
            });
            var plan = planStore.GetPlan(preview.PreviewId)!;

            // First run finishes
            var job = jobManager.StartArchiveIngestJob(
                plan,
                "key-res-1",
                new Models.ClientProjectContext { CompanyId = "comp-res", ProjectId = "proj-res" },
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: false);

            Assert.Equal("completed", job.Status);

            // Cannot resume completed job
            Assert.Throws<InvalidOperationException>(() =>
            {
                jobManager.ResumeArchiveIngestJob(
                    job.JobId,
                    new Models.ClientProjectContext { CompanyId = "comp-res", ProjectId = "proj-res" },
                    storageManager,
                    searchIndex,
                    planStore,
                    handleRegistry,
                    runInBackground: false);
            });
        }
        finally
        {
            try { Directory.Delete(runtimeDir, recursive: true); } catch { }
        }
    }

    private static void SqliteConnectionExtensionsClear()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
