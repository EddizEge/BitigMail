using System.Security.Cryptography;
using BitigMail.Engine.Distribution;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopSetupProofRefreshTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }
    private static IdentityCatalog Catalog() => new(Path.Combine(Path.GetTempPath(), "bitigmail-refresh-" + Guid.NewGuid().ToString("N")));
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    [Fact]
    public async Task ExpiredStartupProofRefreshesAndCreatesExactlyOneAdministrator()
    {
        var clock = new Clock(); using var gate = new SetupProofGate();
        var catalog = Catalog(); var operations = new DesktopOperationGate();
        byte[] old = RandomNumberGenerator.GetBytes(32), fresh = RandomNumberGenerator.GetBytes(32);
        gate.InstallFromNativeChannel(old, clock);
        clock.Ticks = TimeSpan.FromMinutes(6).Ticks;
        Assert.False(gate.TryConsume(Hex(old)));
        string response = DesktopSetupProofRefresh.Handle("SETUP_PROOF " + Hex(fresh), 19876, gate, catalog, operations);
        Assert.True(DesktopReadyProof.VerifyLine(fresh, 19876, response));
        Assert.DoesNotContain(Hex(fresh), response);
        Assert.False(gate.TryConsume(Hex(old)));
        var admin = await catalog.BootstrapAdministratorAsync("native-admin", "synthetic password only", () => gate.TryConsume(Hex(fresh)));
        Assert.Equal(LocalUserRole.Admin, admin.Role);
        Assert.True(catalog.IsInitialized);
        Assert.False(gate.TryConsume(Hex(fresh)));
        Assert.Equal(0, operations.ActiveCount);
    }

    [Theory]
    [InlineData("SETUP_PROOF bad")]
    [InlineData("SETUP_PROOF ")]
    [InlineData("UNKNOWN")]
    public void MalformedCommandCannotReplaceValidProof(string command)
    {
        using var gate = new SetupProofGate(); byte[] original = RandomNumberGenerator.GetBytes(32);
        gate.InstallFromNativeChannel(original);
        Assert.Equal(DesktopSetupProofRefresh.Rejected, DesktopSetupProofRefresh.Handle(command, 19876, gate, Catalog(), new()));
        Assert.True(gate.TryConsume(Hex(original)));
    }

    [Fact]
    public async Task InitializedProfileCannotRefresh()
    {
        using var gate = new SetupProofGate(); byte[] original = RandomNumberGenerator.GetBytes(32), fresh = RandomNumberGenerator.GetBytes(32);
        gate.InstallFromNativeChannel(original); var catalog = Catalog();
        await catalog.BootstrapAdministratorAsync("native-admin", "synthetic password only", () => gate.TryConsume(Hex(original)));
        Assert.Equal(DesktopSetupProofRefresh.Rejected, DesktopSetupProofRefresh.Handle("SETUP_PROOF " + Hex(fresh), 19876, gate, catalog, new()));
        Assert.False(gate.TryConsume(Hex(fresh)));
    }

    [Fact]
    public void ShutdownRefusesRefreshWithoutReplacingOriginalProof()
    {
        using var gate = new SetupProofGate(); byte[] original = RandomNumberGenerator.GetBytes(32), fresh = RandomNumberGenerator.GetBytes(32);
        gate.InstallFromNativeChannel(original); var operations = new DesktopOperationGate(); operations.BeginDrain();
        Assert.Equal(DesktopSetupProofRefresh.Rejected, DesktopSetupProofRefresh.Handle("SETUP_PROOF " + Hex(fresh), 19876, gate, Catalog(), operations));
        Assert.True(gate.TryConsume(Hex(original)));
        Assert.True(operations.IsDrained);
    }
}
