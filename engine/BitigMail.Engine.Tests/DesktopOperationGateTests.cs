using System;
using System.IO;
using System.Threading.Tasks;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopOperationGateTests
{
    [Fact]
    public void DrainWaitsForAllOwnedLeasesAndRejectsNewOperations()
    {
        var gate = new DesktopOperationGate();
        var first = gate.TryEnter()!;
        var second = gate.TryEnter()!;
        Assert.False(gate.IsDrained);
        gate.BeginDrain();
        Assert.Null(gate.TryEnter());
        first.Dispose();
        first.Dispose();
        Assert.Equal(1, gate.ActiveCount);
        Assert.False(gate.IsDrained);
        second.Dispose();
        Assert.True(gate.IsDrained);
    }

    [Fact]
    public async Task MiddlewareReleasesFailedOperationAndRejectsFollowingRequest()
    {
        var gate = new DesktopOperationGate();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/admin/archive/restore";
        context.Response.Body = new MemoryStream();
        var middleware = new DesktopOperationGateMiddleware(_ => throw new IOException("Injected failure"));
        await Assert.ThrowsAsync<IOException>(() => middleware.InvokeAsync(context, gate));
        Assert.Equal(0, gate.ActiveCount);
        gate.BeginDrain();
        await middleware.InvokeAsync(context, gate);
        Assert.Equal(503, context.Response.StatusCode);
        Assert.True(gate.IsDrained);
    }
}
