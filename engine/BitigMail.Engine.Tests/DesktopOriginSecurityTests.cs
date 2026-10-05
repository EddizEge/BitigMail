using System;
using System.IO;
using System.Threading.Tasks;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class DesktopOriginSecurityTests
{
    [Theory]
    [InlineData("http://127.0.0.1:6274", 200)]
    [InlineData("http://127.0.0.1:5173", 403)]
    [InlineData("http://localhost:6274", 403)]
    [InlineData("https://127.0.0.1:6274", 403)]
    [InlineData("http://127.0.0.1:6275", 403)]
    public async Task DesktopRequiresItsExactOrigin(string origin, int expected)
    {
        using var services = new ServiceCollection().AddSingleton<AuthenticatedSessionRegistry>().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.Path = "/api/setup/first-admin";
        context.Request.Host = new HostString("127.0.0.1", 6274);
        context.Request.Headers.Origin = origin;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream("{}"u8.ToArray());
        context.Request.ContentLength = 2;
        context.Response.Body = new MemoryStream();
        var middleware = new LocalSecurityMiddleware(_ => Task.CompletedTask, SecurityConfig.ForDesktop(6274));
        await middleware.InvokeAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
        if (expected == 200) Assert.Equal(origin, context.Response.Headers.AccessControlAllowOrigin.ToString());
        else Assert.False(context.Response.Headers.ContainsKey("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void InvalidPortCannotConfigureDesktop(int port) => Assert.Throws<ArgumentOutOfRangeException>(() => SecurityConfig.ForDesktop(port));
}
