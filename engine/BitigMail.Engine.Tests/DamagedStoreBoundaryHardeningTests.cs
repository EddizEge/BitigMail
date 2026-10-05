using System.Text.Json;
using BitigMail.Engine.Recovery;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DamagedStoreBoundaryHardeningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bitigmail-recovery-boundary-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source.pst");
    private string Output => Path.Combine(_root, "output");
    private string Result => Path.Combine(Output, "result.json");
    private DamagedStoreWorkerRequest Request => new(1, "invocation", "job", Source,
        DamagedStoreResultValidator.HashFile(Source), Output, 100, 100, 16, DateTime.UtcNow.AddMinutes(5).Ticks);
    public DamagedStoreBoundaryHardeningTests()
    {
        Directory.CreateDirectory(Output); File.WriteAllText(Source, "source"); Write();
    }
    private void Write(string outcome = "healthy_extraction", IReadOnlyList<RecoveredMimeFile>? files = null,
        bool complete = true, IReadOnlyList<RecoveryFailure>? failures = null) =>
        File.WriteAllText(Result, JsonSerializer.Serialize(new DamagedStoreWorkerResult(1, "invocation", "job",
            Request.SourceSha256, outcome, null, files ?? [], failures ?? [], complete)));
    private void Validate(CancellationToken token = default) => new DamagedStoreResultValidator().Validate(Request, Result, Source, token);

    [Theory] [InlineData("duplicate")] [InlineData("unknown")] [InlineData("null")]
    public void InvalidJsonContractsFailClosed(string kind)
    {
        string json = File.ReadAllText(Result);
        json = kind switch { "duplicate" => "{\"SchemaVersion\":1," + json[1..],
            "unknown" => "{\"Unexpected\":true," + json[1..], _ => json.Replace("\"Messages\":[]", "\"Messages\":null") };
        File.WriteAllText(Result, json); Assert.Throws<InvalidDataException>(() => Validate());
    }
    [Fact] public void SourcePathCannotBeSubstitutedEvenWithIdenticalBytes()
    {
        string other = Path.Combine(_root, "other.pst"); File.Copy(Source, other);
        Assert.Throws<InvalidDataException>(() => new DamagedStoreResultValidator().Validate(Request, Result, other));
    }
    [Fact] public void EmptyPartialAndContradictoryUnreadableResultsFail()
    {
        Write("partial_recovered", complete: false); Assert.Throws<InvalidDataException>(() => Validate());
        Write("unreadable_source", complete: true); Assert.Throws<InvalidDataException>(() => Validate());
        Write("unreadable_source", complete: false, failures: [new("root", null, "unreadable")]); Validate();
    }
    [Fact] public void CancellationStopsSourceHashAndValidation()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Validate(source.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => DamagedStoreResultValidator.HashFile(Source, source.Token));
    }
    [Fact] public async Task EmptyUnlistedJunctionFailsBeforeDescent()
    {
        string outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        string link = Path.Combine(Output, "junction");
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"New-Item -ItemType Junction -Path '{link.Replace("'", "''")}' -Target '{outside.Replace("'", "''")}' -ErrorAction Stop | Out-Null");
        using var process = System.Diagnostics.Process.Start(start)!; await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try { Assert.Throws<InvalidDataException>(() => Validate()); Assert.Empty(Directory.GetFiles(outside)); }
        finally { Directory.Delete(link); }
    }
    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(Output)) File.Delete(file);
        Directory.Delete(Output);
        string outside = Path.Combine(_root, "outside"); if (Directory.Exists(outside)) Directory.Delete(outside);
        foreach (string file in Directory.GetFiles(_root)) File.Delete(file);
        Directory.Delete(_root);
    }
}
