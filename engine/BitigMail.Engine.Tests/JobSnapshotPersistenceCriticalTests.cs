using System.Text.Json;
using BitigMail.LocalHost.Jobs;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class JobSnapshotPersistenceCriticalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bitigmail-persist-" + Guid.NewGuid().ToString("N"));
    public JobSnapshotPersistenceCriticalTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void NewAndReplacedSnapshotsContainCompleteUnicodeJson()
    {
        string path = Path.Combine(_directory, "job.json");
        JobSnapshotPersistence.Write(path, "{\"stage\":\"İstanbul arşivi\",\"count\":1}");
        using (var first = JsonDocument.Parse(File.ReadAllText(path)))
            Assert.Equal("İstanbul arşivi", first.RootElement.GetProperty("stage").GetString());
        JobSnapshotPersistence.Write(path, "{\"stage\":\"Tamamlandı\",\"count\":1024}");
        using var second = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1024, second.RootElement.GetProperty("count").GetInt32());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ActualRenameSharingLockCanReleaseWithoutLosingPreviousSnapshot()
    {
        string path = Path.Combine(_directory, "job.json");
        const string before = "{\"count\":929}";
        const string after = "{\"count\":1024}";
        File.WriteAllText(path, before);
        var retried = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Task save = Task.Run(() => JobSnapshotPersistence.Write(path, after, n => retried.TrySetResult(n)));
        try
        {
            Assert.Equal(1, await retried.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(before, File.ReadAllText(path));
        }
        finally
        {
            lease.Dispose();
            await save.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(after, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task PersistentSharingLockFailsWithinBudgetAndPreservesOldBytes()
    {
        string path = Path.Combine(_directory, "job.json");
        const string original = "{\"count\":929,\"status\":\"converting\"}";
        File.WriteAllText(path, original);
        using var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        int retries = 0;
        Exception? failure = await Task.Run(() => Record.Exception(() =>
            JobSnapshotPersistence.Write(path, "{\"status\":\"completed\"}", _ => retries++)))
            .WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(5, retries);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void MissingParentIsNotRetriedOrInventedAsSuccess()
    {
        int retries = 0;
        Assert.Throws<DirectoryNotFoundException>(() => JobSnapshotPersistence.Write(
            Path.Combine(_directory, "missing-parent", "job.json"), "{}", _ => retries++));
        Assert.Equal(0, retries);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void CreateOnlyPublicationCannotOverwriteAnExistingPlan()
    {
        string destination = Path.Combine(_directory, "immutable.json");
        string temporary = Path.Combine(_directory, "candidate.tmp");
        File.WriteAllText(destination, "original plan");
        File.WriteAllText(temporary, "replacement plan");
        Assert.ThrowsAny<IOException>(() => JobSnapshotPersistence.Publish(temporary, destination, overwrite: false));
        Assert.Equal("original plan", File.ReadAllText(destination));
        Assert.Equal("replacement plan", File.ReadAllText(temporary));
    }

    public void Dispose()
    {
        // Only this fixture's direct files; no recursive deletion or shared directory cleanup.
        foreach (string file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
