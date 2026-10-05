using System.Text.Json;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class JobHistoryPagingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bitigmail-job-paging", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TenThousandRecords_AreServerFiltered_StableAndBounded()
    {
        string jobs = Path.Combine(_directory, "jobs");
        Directory.CreateDirectory(jobs);
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var createdAt = DateTimeOffset.Parse("2026-09-15T12:00:00Z");
        for (int i = 0; i < 10_000; i++)
        {
            var record = new LocalJobRecord
            {
                JobId = $"job-{i:D5}",
                JobKind = i % 2 == 0 ? "convert" : "imap-transfer",
                Status = i % 4 == 0 ? "completed" : "failed",
                SourceFileName = $"source-{i:D5}.ost",
                TargetFileName = $"target-{i:D5}.pst",
                ClientContext = new ClientProjectContext
                {
                    CompanyId = "company",
                    CompanyName = i == 7777 ? "Needle Company" : "Company",
                    ProjectId = "project",
                    ProjectName = "Project"
                },
                CreatedAt = createdAt
            };
            File.WriteAllText(Path.Combine(jobs, $"{record.JobId}.json"), JsonSerializer.Serialize(record, jsonOptions));
        }

        var manager = new JobManager(_directory);
        var first = manager.GetJobsPage(page: 1, pageSize: 10_000);
        Assert.Equal(10_000, first.TotalCount);
        Assert.Equal(100, first.PageSize);
        Assert.Equal(100, first.Items.Count);
        Assert.Equal("job-09999", first.Items[0].JobId);
        Assert.Equal("job-09900", first.Items[^1].JobId);

        var second = manager.GetJobsPage(page: 2, pageSize: 100);
        Assert.Equal("job-09899", second.Items[0].JobId);
        Assert.Empty(manager.GetJobsPage(page: int.MaxValue, pageSize: 100).Items);
        var attention = manager.GetJobsPage(status: "attention");
        Assert.Equal(7_500, attention.TotalCount);
        Assert.All(attention.Items, item => Assert.Equal("failed", item.Status));
        var search = manager.GetJobsPage(search: "Needle Company");
        Assert.Equal(1, search.TotalCount);
        Assert.Equal("job-07777", Assert.Single(search.Items).JobId);
        var queuedConvert = manager.GetJobsPage(status: "completed", jobKind: "convert");
        Assert.Equal(2_500, queuedConvert.TotalCount);
        Assert.All(queuedConvert.Items, item => Assert.Equal("convert", item.JobKind));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
