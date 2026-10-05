using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class Task038RouteInventoryTests
{
    [Fact]
    public void NewApiMethodAndTemplateFailsClosedUntilInventoryIsReviewed()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.MapGet("/api/archive/future-unclassified", () => "nope");

        var error = Assert.Throws<InvalidOperationException>(() => ApiRoutePolicy.AssertComplete(app, development: false));

        Assert.Contains("API method/template envanteri", error.Message, StringComparison.Ordinal);
    }
}
