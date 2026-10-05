using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class TransientResourceOwnershipRegistryTests
{
    [Fact]
    public void PreviewAndPlanIdsAreBoundToTheExactCreatingSession()
    {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        var owner=new AuthenticatedSessionPrincipal("user-a","session-a",7);
        accessor.HttpContext.Items[typeof(AuthenticatedSessionPrincipal)]=owner;
        var registry=new TransientResourceOwnershipRegistry(accessor);
        registry.Bind("preview-1");

        Assert.True(registry.IsOwnedBy("preview-1",owner));
        Assert.False(registry.IsOwnedBy("preview-1",new("user-b","session-b",7)));
        Assert.False(registry.IsOwnedBy("preview-1",new("user-a","new-session",7)));
        Assert.False(registry.IsOwnedBy("preview-1",new("user-a","session-a",8)));
        Assert.False(registry.IsOwnedBy("unknown",owner));
    }
}
