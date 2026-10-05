using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BitigMail.LocalHost;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Security;
using BitigMail.TestingHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace BitigMail.Engine.Tests;

public class SecurityTests
{
    private static (LocalSecurityMiddleware middleware, SessionManager sessionManager) CreateMiddleware(string host = "127.0.0.1:6174")
    {
        var config = new SecurityConfig { ExpectedHost = host };
        var sessionManager = new SessionManager();
        var middleware = new LocalSecurityMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await Task.CompletedTask;
        }, config);
        return (middleware, sessionManager);
    }

    private static DefaultHttpContext CreateValidContext(SessionManager sessionManager, string method = "GET", string path = "/api/jobs", string? token = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sessionManager);
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers["Host"] = "127.0.0.1:6174";
        context.Request.Headers["Origin"] = "http://127.0.0.1:5173";

        if (token != null)
        {
            context.Request.Headers[SecurityConfig.SessionHeaderName] = token;
        }

        if (method == "POST" || method == "PUT" || method == "PATCH")
        {
            context.Request.Headers["Content-Type"] = "application/json";
        }

        return context;
    }

    [Fact]
    public void SessionManager_SupportsMultipleValidSessionsWithoutInvalidatingOthers()
    {
        var manager = new SessionManager();
        string token1 = manager.CreateSession();
        string token2 = manager.CreateSession();

        Assert.NotNull(token1);
        Assert.NotNull(token2);
        Assert.NotEqual(token1, token2);

        // Both sessions must remain valid simultaneously (e.g. multi-tab / page reload)
        Assert.True(manager.ValidateSession(token1));
        Assert.True(manager.ValidateSession(token2));
        Assert.False(manager.ValidateSession(token1 + "_invalid"));
        Assert.False(manager.ValidateSession(null));
        Assert.False(manager.ValidateSession(""));
    }

    [Fact]
    public async Task Middleware_ValidRequest_PassesWithNoStoreHeaders()
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        string token = sessionMgr.CreateSession();
        var context = CreateValidContext(sessionMgr, "GET", "/api/jobs", token);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("no-store", context.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("http://127.0.0.1:5173", context.Response.Headers["Access-Control-Allow-Origin"].ToString());
    }

    [Theory]
    [InlineData("localhost:6174")]
    [InlineData("192.168.1.1:6174")]
    [InlineData("malicious-site.com")]
    [InlineData("")]
    public async Task Middleware_InvalidOrForeignHost_Rejected400(string host)
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        var context = CreateValidContext(sessionMgr, "GET", "/api/session/status");
        context.Request.Headers["Host"] = host;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_MultipleHostHeaders_Rejected400()
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        var context = CreateValidContext(sessionMgr, "GET", "/api/session/status");
        context.Request.Headers["Host"] = new StringValues(new[] { "127.0.0.1:6174", "127.0.0.1:6174" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:5173")]
    [InlineData("http://attacker.com")]
    [InlineData("null")]
    public async Task Middleware_InvalidOrForeignOrigin_Rejected403(string? origin)
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        var context = CreateValidContext(sessionMgr, "GET", "/api/session/status");
        if (origin == null)
        {
            context.Request.Headers.Remove("Origin");
        }
        else
        {
            context.Request.Headers["Origin"] = origin;
        }

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_MultipleOriginHeaders_Rejected403()
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        var context = CreateValidContext(sessionMgr, "GET", "/api/session/status");
        context.Request.Headers["Origin"] = new StringValues(new[] { "http://127.0.0.1:5173", "http://127.0.0.1:5173" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_OptionsPreflight_RequiresValidHostAndOriginBeforeCors()
    {
        var (middleware, sessionMgr) = CreateMiddleware();

        // 1. Valid OPTIONS -> 204 No Content
        var validContext = CreateValidContext(sessionMgr, "OPTIONS", "/api/jobs/start");
        await middleware.InvokeAsync(validContext);
        Assert.Equal(StatusCodes.Status204NoContent, validContext.Response.StatusCode);

        // 2. Foreign origin on OPTIONS -> 403 Forbidden (CORS is not security bypass)
        var invalidOriginContext = CreateValidContext(sessionMgr, "OPTIONS", "/api/jobs/start");
        invalidOriginContext.Request.Headers["Origin"] = "http://attacker.com";
        await middleware.InvokeAsync(invalidOriginContext);
        Assert.Equal(StatusCodes.Status403Forbidden, invalidOriginContext.Response.StatusCode);

        // 3. Foreign host on OPTIONS -> 400 Bad Request
        var invalidHostContext = CreateValidContext(sessionMgr, "OPTIONS", "/api/jobs/start");
        invalidHostContext.Request.Headers["Host"] = "localhost:6174";
        await middleware.InvokeAsync(invalidHostContext);
        Assert.Equal(StatusCodes.Status400BadRequest, invalidHostContext.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_MultipleSessionTokens_Rejected400()
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        string token = sessionMgr.CreateSession();
        var context = CreateValidContext(sessionMgr, "GET", "/api/jobs", token);
        context.Request.Headers[SecurityConfig.SessionHeaderName] = new StringValues(new[] { token, token });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/jobs")]
    [InlineData("/api/session/status")]
    public async Task Middleware_ProtectedEndpoint_MissingToken_Rejected401(string endpoint)
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        var context = CreateValidContext(sessionMgr, "GET", endpoint, token: null);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data")]
    public async Task Middleware_MutatingRequest_MisleadingContentType_Rejected415(string contentType)
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        string token = sessionMgr.CreateSession();
        var context = CreateValidContext(sessionMgr, "POST", "/api/jobs/start", token);
        context.Request.Headers["Content-Type"] = contentType;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, context.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_MutatingRequest_MissingContentType_Rejected415()
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        string token = sessionMgr.CreateSession();
        var context = CreateValidContext(sessionMgr, "POST", "/api/jobs/start", token);
        context.Request.Headers.Remove("Content-Type");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, context.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_MutatingRequest_MultipleContentType_Rejected400()
    {
        var (middleware, sessionMgr) = CreateMiddleware();
        string token = sessionMgr.CreateSession();
        var context = CreateValidContext(sessionMgr, "POST", "/api/jobs/start", token);
        context.Request.Headers["Content-Type"] = new StringValues(new[] { "application/json", "application/json" });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("/etc/shadow")]
    [InlineData("/var/log/syslog")]
    [InlineData("..\\..\\sensitive.pst")]
    [InlineData("../../secret.ost")]
    [InlineData("arbitrary-file.pst")]
    [InlineData("arbitrary-file.ost")]
    [InlineData("malicious_fixture")]
    [InlineData("unknown-token")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TestingHost_SetSplitSource_PathShapedOrUnknownFixture_RejectedAndCannotSelectFiles(string input)
    {
        var registry = new FileHandleRegistry();
        var picker = new TestingFilePickerService(registry);

        bool result = picker.TrySetSplitSourceFixture(input, out string error);

        Assert.False(result);
        Assert.NotEmpty(error);
        Assert.Null(picker.ActiveSplitFixtureId);
        Assert.Null(picker.ActiveSplitFixturePath);

        // PickSplitSourceAsync must never select the rejected input
        var pickResult = await picker.PickSplitSourceAsync();
        Assert.False(pickResult.Cancelled);
        Assert.NotEqual(input, pickResult.DisplayPath);
        Assert.DoesNotContain("calc.exe", pickResult.DisplayPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shadow", pickResult.DisplayPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arbitrary-file", pickResult.DisplayPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("genuine-pst")]
    [InlineData("genuine-ost")]
    [InlineData("synthetic-size")]
    public async Task TestingHost_SetSplitSource_AllowlistedFixtureId_SucceedsAndSelectsApprovedFile(string fixtureId)
    {
        var registry = new FileHandleRegistry();
        var picker = new TestingFilePickerService(registry);

        bool result = picker.TrySetSplitSourceFixture(fixtureId, out string error);

        Assert.True(result, $"Expected fixture '{fixtureId}' to succeed but got: {error}");
        Assert.Empty(error);
        Assert.NotNull(picker.ActiveSplitFixtureId);
        Assert.NotNull(picker.ActiveSplitFixturePath);
        Assert.True(File.Exists(picker.ActiveSplitFixturePath));

        var pickResult = await picker.PickSplitSourceAsync();
        Assert.False(pickResult.Cancelled);
        Assert.NotNull(pickResult.Handle);
        Assert.Equal(picker.ActiveSplitFixturePath, pickResult.DisplayPath);
    }

    [Theory]
    [InlineData("/api/testing/set-split-source")]
    [InlineData("/api/testing/simulate-cancel")]
    [InlineData("/api/testing/anything")]
    public void ProductionHost_NeverMapsTestingEndpoints(string testingPath)
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        app.MapLocalEngineEndpoints();

        var endpointDataSources = app.Services.GetServices<EndpointDataSource>();
        var mappedRoutes = endpointDataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Where(r => r != null)
            .ToList();

        // Production endpoint map must NEVER contain any /api/testing route
        Assert.DoesNotContain(mappedRoutes, r => r!.StartsWith("/api/testing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(mappedRoutes, r => string.Equals(r, testingPath, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/api/testing/set-split-source")]
    [InlineData("/api/testing/simulate-cancel")]
    public async Task TestingHost_Endpoints_ProtectedUnderSecurityMiddleware(string endpoint)
    {
        var (middleware, sessionMgr) = CreateMiddleware(host: "127.0.0.1:6175");

        // 1. Missing session token -> 401 Unauthorized
        var noTokenCtx = CreateValidContext(sessionMgr, "POST", endpoint, token: null);
        noTokenCtx.Request.Headers["Host"] = "127.0.0.1:6175";
        await middleware.InvokeAsync(noTokenCtx);
        Assert.Equal(StatusCodes.Status401Unauthorized, noTokenCtx.Response.StatusCode);

        // 2. Foreign host -> 400 Bad Request
        string token = sessionMgr.CreateSession();
        var badHostCtx = CreateValidContext(sessionMgr, "POST", endpoint, token);
        badHostCtx.Request.Headers["Host"] = "attacker.com:6175";
        await middleware.InvokeAsync(badHostCtx);
        Assert.Equal(StatusCodes.Status400BadRequest, badHostCtx.Response.StatusCode);

        // 3. Foreign origin -> 403 Forbidden
        var badOriginCtx = CreateValidContext(sessionMgr, "POST", endpoint, token);
        badOriginCtx.Request.Headers["Host"] = "127.0.0.1:6175";
        badOriginCtx.Request.Headers["Origin"] = "http://malicious.origin";
        await middleware.InvokeAsync(badOriginCtx);
        Assert.Equal(StatusCodes.Status403Forbidden, badOriginCtx.Response.StatusCode);
    }
}
