using System;
using System.IO;
using System.Text.Json;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public class JobManagerTests
{
    [Fact]
    public void FileHandleRegistry_IssuesOpaqueHandlesWithoutLeakingPath()
    {
        var registry = new FileHandleRegistry();
        string tempSource = Path.GetTempFileName();
        string tempTarget = Path.Combine(Path.GetTempPath(), "test-target.pst");

        try
        {
            string srcHandle = registry.RegisterSource(tempSource);
            string tgtHandle = registry.RegisterTarget(tempTarget);

            Assert.StartsWith("src_", srcHandle);
            Assert.StartsWith("tgt_", tgtHandle);
            Assert.DoesNotContain(tempSource, srcHandle);
            Assert.DoesNotContain(tempTarget, tgtHandle);

            Assert.Equal(tempSource, registry.GetSourcePath(srcHandle));
            Assert.Equal(tempTarget, registry.GetTargetPath(tgtHandle));
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
        }
    }

    [Fact]
    public void FileHandleRegistry_AttachesServerBoundAnalysisResult()
    {
        var registry = new FileHandleRegistry();
        string tempSource = Path.GetTempFileName();
        try
        {
            string handle = registry.RegisterSource(tempSource);
            var entryBefore = registry.GetSourceEntry(handle);
            Assert.NotNull(entryBefore);
            Assert.Null(entryBefore.AnalysisResult);

            var analysis = new OstAnalysisResult
            {
                SourceSha256 = "abc123def456",
                TotalItems = 15,
                PhysicalTotalItems = 15,
                Preflight = new PreflightCheckResult { CanConvert = true }
            };

            registry.AttachAnalysis(handle, analysis);

            var entryAfter = registry.GetSourceEntry(handle);
            Assert.NotNull(entryAfter?.AnalysisResult);
            Assert.Equal("abc123def456", entryAfter.BoundSha256);
            Assert.Equal(15, entryAfter.BoundItemCount);
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
        }
    }

    [Fact]
    public void ClientProjectContext_IsFrozenOnJobRecord()
    {
        var context = new ClientProjectContext
        {
            CompanyId = "comp-1",
            CompanyName = "Şirket A",
            ProjectId = "proj-1",
            ProjectName = "Proje Alpha"
        };

        var job = new LocalJobRecord
        {
            JobId = "job-001",
            ClientContext = new ClientProjectContext
            {
                CompanyId = context.CompanyId,
                CompanyName = context.CompanyName,
                ProjectId = context.ProjectId,
                ProjectName = context.ProjectName
            }
        };

        // Mutate original context
        context.CompanyName = "Değiştirilmiş Şirket B";
        context.ProjectName = "Değiştirilmiş Proje Beta";

        // Job record context remains frozen
        Assert.Equal("Şirket A", job.ClientContext.CompanyName);
        Assert.Equal("Proje Alpha", job.ClientContext.ProjectName);
    }

    [Fact]
    public void JobManager_Idempotency_SameRequestReturnsSameJob()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bitigmail-test-runtime-{Guid.NewGuid():N}");
        try
        {
            var manager = new JobManager(tempDir);
            string sourceFile = Path.Combine(tempDir, "source.ost");
            string targetFile = Path.Combine(tempDir, "target.pst");
            File.WriteAllText(sourceFile, "dummy");

            var context = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1", CompanyName = "C1", ProjectName = "P1" };
            string key = "idemp-key-001";

            var job1 = manager.StartJob(sourceFile, targetFile, key, context, "hash1", 10);
            var job2 = manager.StartJob(sourceFile, targetFile, key, context, "hash1", 10);

            Assert.Equal(job1.JobId, job2.JobId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void JobManager_Idempotency_ConflictingParametersThrowsInvalidOperation()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bitigmail-test-runtime-{Guid.NewGuid():N}");
        try
        {
            var manager = new JobManager(tempDir);
            string sourceFile = Path.Combine(tempDir, "source.ost");
            string targetFile1 = Path.Combine(tempDir, "target1.pst");
            string targetFile2 = Path.Combine(tempDir, "target2.pst");
            File.WriteAllText(sourceFile, "dummy");

            var context1 = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1", CompanyName = "C1", ProjectName = "P1" };
            var context2 = new ClientProjectContext { CompanyId = "c2", ProjectId = "p2", CompanyName = "C2", ProjectName = "P2" };
            string key = "idemp-key-conflict";

            manager.StartJob(sourceFile, targetFile1, key, context1, "hash1", 10);

            // Same key with different target and context must throw InvalidOperationException (maps to 409 Conflict)
            var ex = Assert.Throws<InvalidOperationException>(() =>
                manager.StartJob(sourceFile, targetFile2, key, context2, "hash1", 10));

            Assert.Contains("Idempotency key", ex.Message);
            Assert.Contains("farklı parametrelerle", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void JobManager_IsolatedRuntime_DoesNotTouchDefaultRuntimeDirectory()
    {
        string isolatedDir = Path.Combine(Path.GetTempPath(), $"bitigmail-isolated-{Guid.NewGuid():N}");
        try
        {
            var manager = new JobManager(isolatedDir);
            Assert.Equal(isolatedDir, manager.RuntimeDirectory);
            Assert.True(Directory.Exists(Path.Combine(isolatedDir, "jobs")));
            Assert.True(Directory.Exists(Path.Combine(isolatedDir, "reports")));
        }
        finally
        {
            if (Directory.Exists(isolatedDir))
            {
                try { Directory.Delete(isolatedDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void JobManager_InterruptedJobRecovery_SetsSimplifiedUserText()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bitigmail-interrupted-{Guid.NewGuid():N}");
        try
        {
            string jobsDir = Path.Combine(tempDir, "jobs");
            Directory.CreateDirectory(jobsDir);

            // Write an uncompleted job simulating crash
            var unfinishedJob = new LocalJobRecord
            {
                JobId = "job-interrupted-01",
                Status = "converting",
                Stage = "Dönüştürülüyor",
                CreatedAt = DateTimeOffset.UtcNow
            };
            string jobJson = JsonSerializer.Serialize(unfinishedJob, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(Path.Combine(jobsDir, "job-interrupted-01.json"), jobJson);

            // Start JobManager over this directory
            var manager = new JobManager(tempDir);
            var recoveredJob = manager.GetJob("job-interrupted-01");

            Assert.NotNull(recoveredJob);
            Assert.Equal("interrupted", recoveredJob.Status);
            Assert.Equal("Kesintiye Uğradı", recoveredJob.Stage);
            // User-facing text without internal/developer jargon
            Assert.Contains("işlem kesintiye uğradı", recoveredJob.ErrorMessage);
            Assert.DoesNotContain("sahte", recoveredJob.ErrorMessage?.ToLowerInvariant() ?? "");
            Assert.DoesNotContain("taklidi", recoveredJob.ErrorMessage?.ToLowerInvariant() ?? "");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void JobManager_ExposesAndPersistsOutputPath_AcrossRecovery()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bitigmail-outputpath-{Guid.NewGuid():N}");
        try
        {
            var manager = new JobManager(tempDir);
            string sourceFile = Path.Combine(tempDir, "source.ost");
            string targetFile = Path.Combine(tempDir, "target.pst");
            File.WriteAllText(sourceFile, "dummy");

            var context = new ClientProjectContext { CompanyId = "c1", ProjectId = "p1", CompanyName = "C1", ProjectName = "P1" };
            var job = manager.StartJob(sourceFile, targetFile, "key-output-path", context, "dummy-hash", 5);

            // OutputPath is exposed on in-memory record
            Assert.Equal(Path.GetFullPath(targetFile), job.OutputPath);

            // Wait briefly for background execution / persistence to settle on disk
            string jobFilePath = Path.Combine(tempDir, "jobs", $"{job.JobId}.json");
            int waited = 0;
            while (!File.Exists(jobFilePath) && waited < 3000)
            {
                Thread.Sleep(50);
                waited += 50;
            }
            Thread.Sleep(100);

            // Recover from disk via a new JobManager instance
            var newManager = new JobManager(tempDir);
            var recoveredJob = newManager.GetJob(job.JobId);

            Assert.NotNull(recoveredJob);
            Assert.Equal(Path.GetFullPath(targetFile), recoveredJob.OutputPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
