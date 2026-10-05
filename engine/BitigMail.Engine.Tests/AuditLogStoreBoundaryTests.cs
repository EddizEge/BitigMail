using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BitigMail.Engine.Security;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class AuditLogStoreBoundaryTests
{
    private static string NewProfile() => Path.Combine(Path.GetTempPath(), "bitigmail-audit-boundary-" + Guid.NewGuid().ToString("N"));
    private static void Append(AuditLogStore store) => store.Append("admin", "archive.restore", "company", "project", null, AuditOutcome.Succeeded);

    [Fact]
    public void RecoversFlushedTailWithoutDuplicatingEntries()
    {
        string profile = NewProfile();
        var store = new AuditLogStore(profile);
        Append(store);
        string head = Path.Combine(profile, "audit-v1.head.json");
        byte[] previous = File.ReadAllBytes(head);
        Append(store);
        File.WriteAllBytes(head, previous);
        var recovered = new AuditLogStore(profile).Read();
        Assert.True(recovered.IntegrityValid);
        Assert.Equal(2, recovered.Entries.Count);
        var reopened = new AuditLogStore(profile);
        Append(reopened);
        Assert.Equal(3, reopened.Read().Entries.Count);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("oversized-log")]
    [InlineData("oversized-head")]
    [InlineData("duplicate")]
    public void CorruptStorageProducesSafeWarning(string mode)
    {
        string profile = NewProfile();
        var store = new AuditLogStore(profile);
        Append(store);
        string log = Path.Combine(profile, "audit-v1.jsonl");
        string head = Path.Combine(profile, "audit-v1.head.json");
        if (mode == "invalid") File.WriteAllText(log, "{invalid\n");
        if (mode == "oversized-log") File.WriteAllText(log, new string('x', 1_048_578));
        if (mode == "oversized-head") File.WriteAllText(head, new string('x', 4097));
        if (mode == "duplicate") File.WriteAllText(head, File.ReadAllText(head).Replace("{", "{\"sequence\":0,"));
        var result = new AuditLogStore(profile).Read();
        Assert.False(result.IntegrityValid);
        Assert.NotNull(result.Warning);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task IndependentWritersKeepEveryEvent()
    {
        string profile = NewProfile();
        var first = new AuditLogStore(profile);
        var second = new AuditLogStore(profile);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => Append(i % 2 == 0 ? first : second))));
        var result = new AuditLogStore(profile).Read();
        Assert.True(result.IntegrityValid);
        Assert.Equal(12, result.Entries.Count);
    }
}
