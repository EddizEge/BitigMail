using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using BitigMail.LocalHost.Dialogs;

namespace BitigMail.LocalHost.Security;

public class LocalSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityConfig _config;

    public LocalSecurityMiddleware(RequestDelegate next, SecurityConfig config)
    {
        _next = next;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        bool developmentAnonymous = _config.DevelopmentAnonymousSession || context.RequestServices.GetService<AuthenticatedSessionRegistry>() is null;
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        // 1. Validate exact Host BEFORE any CORS or OPTIONS handling
        if (!context.Request.Headers.TryGetValue("Host", out var hostValues))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Bad Request: Missing Host header." });
            return;
        }

        if (hostValues.Count != 1)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Bad Request: Multiple Host headers are not permitted." });
            return;
        }

        string host = hostValues[0] ?? string.Empty;
        if (!string.Equals(host, _config.ExpectedHost, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = $"Bad Request: Host '{host}' is not permitted. Only exact loopback '{_config.ExpectedHost}' is authorized." });
            return;
        }

        // 2. Mutations require exact Origin. Authenticated safe GET may omit it for same-origin desktop navigation.
        bool mutating = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
        bool hasOrigin = context.Request.Headers.TryGetValue("Origin", out var originValues);
        if (((developmentAnonymous||mutating) && !hasOrigin) || (hasOrigin && originValues.Count != 1))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Forbidden: Mutations require one exact Origin header." });
            return;
        }
        string origin = hasOrigin ? originValues[0] ?? string.Empty : string.Empty;
        if (hasOrigin && !string.Equals(origin, _config.AllowedOrigin, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = $"Forbidden: Exact Origin '{_config.AllowedOrigin}' is required for all API requests." });
            return;
        }

        // 3. Apply security & CORS response headers now that Host and Origin are strictly validated
        if (hasOrigin) context.Response.Headers["Access-Control-Allow-Origin"] = _config.AllowedOrigin;
        context.Response.Headers["Access-Control-Allow-Headers"] = $"Content-Type, {SecurityConfig.SessionHeaderName}";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";

        // Handle preflight OPTIONS after Host and Origin are verified
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        // 4. Validate Session Token header multiplicity & validity on protected endpoints
        if (context.Request.Headers.TryGetValue(SecurityConfig.SessionHeaderName, out var tokenValues))
        {
            if (tokenValues.Count > 1)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = $"Bad Request: Multiple {SecurityConfig.SessionHeaderName} headers are not permitted." });
                return;
            }
        }

        string path = context.Request.Path.Value ?? "";
        bool legacySession = string.Equals(path, "/api/session", StringComparison.OrdinalIgnoreCase)||string.Equals(path,"/api/session/status",StringComparison.OrdinalIgnoreCase);
        bool publicDevelopmentSession = developmentAnonymous&&string.Equals(path,"/api/session",StringComparison.OrdinalIgnoreCase);
        bool publicIdentity = string.Equals(path,"/api/setup/status",StringComparison.OrdinalIgnoreCase)||string.Equals(path,"/api/setup/first-admin",StringComparison.OrdinalIgnoreCase)||string.Equals(path,"/api/auth/login",StringComparison.OrdinalIgnoreCase);

        if (legacySession && !developmentAnonymous)
        {
            context.Response.StatusCode=StatusCodes.Status404NotFound;return;
        }

        AuthenticatedSessionPrincipal? authenticatedPrincipal=null;
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !publicDevelopmentSession && !publicIdentity)
        {
            string? token = tokenValues.FirstOrDefault();
            if (developmentAnonymous)
            {
                var sessionManager=context.RequestServices.GetRequiredService<SessionManager>();
                if(!sessionManager.ValidateSession(token)){context.Response.StatusCode=StatusCodes.Status401Unauthorized;await context.Response.WriteAsJsonAsync(new{error=$"Unauthorized: Missing or invalid {SecurityConfig.SessionHeaderName} header."});return;}
            }
            else
            {
                var sessions=context.RequestServices.GetRequiredService<AuthenticatedSessionRegistry>();var catalog=context.RequestServices.GetRequiredService<IdentityCatalog>();
                var principal=sessions.Validate(token,catalog.CurrentSecurityVersion);
                if(principal is null){context.Response.StatusCode=StatusCodes.Status401Unauthorized;await context.Response.WriteAsJsonAsync(new{error="Unauthorized."});return;}
                context.Items[typeof(AuthenticatedSessionPrincipal)]=principal;
                authenticatedPrincipal=principal;
            }
        }

        // 5. Validate Content-Type on mutating requests (POST, PUT, PATCH)
        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            if (!context.Request.Headers.TryGetValue("Content-Type", out var ctValues) || ctValues.Count == 0)
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(new { error = "Unsupported Media Type: Missing Content-Type header. Exact 'application/json' is required." });
                return;
            }

            if (ctValues.Count != 1)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "Bad Request: Multiple Content-Type headers are not permitted." });
                return;
            }

            string rawCt = ctValues[0] ?? string.Empty;
            string mediaType = rawCt.Split(';')[0].Trim();
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsJsonAsync(new { error = $"Unsupported Media Type: '{rawCt}'. Content-Type must be 'application/json'." });
                return;
            }
        }

        if (!developmentAnonymous && authenticatedPrincipal is not null)
        {
            if(context.Request.ContentLength is > 1_048_576){context.Response.StatusCode=StatusCodes.Status413PayloadTooLarge;return;}
            try{if(!await AuthorizeResourceAsync(context,authenticatedPrincipal)){context.RequestServices.GetService<AuditLogStore>()?.Append(authenticatedPrincipal.UserId,"api.denied",null,null,null,BitigMail.Engine.Security.AuditOutcome.Denied);context.Response.StatusCode=StatusCodes.Status404NotFound;return;}}
            catch(JsonException){context.Response.StatusCode=StatusCodes.Status400BadRequest;await context.Response.WriteAsJsonAsync(new{error="Geçersiz JSON isteği."});return;}
            catch(BadHttpRequestException ex)when(ex.StatusCode==StatusCodes.Status413PayloadTooLarge){context.Response.StatusCode=StatusCodes.Status413PayloadTooLarge;return;}
        }

        await _next(context);
        if(authenticatedPrincipal is not null&&context.RequestServices.GetService<AuditLogStore>() is { } audit){var policy=ApiRoutePolicy.Classify(path);string action=policy switch{ApiPolicyKind.Admin=>"api.admin",ApiPolicyKind.Scoped=>"api.scoped",_=>"api.authenticated"};audit.Append(authenticatedPrincipal.UserId,action,null,null,null,context.Response.StatusCode<400?BitigMail.Engine.Security.AuditOutcome.Succeeded:BitigMail.Engine.Security.AuditOutcome.Failed);}
    }

    private static async Task<bool> AuthorizeResourceAsync(HttpContext context,AuthenticatedSessionPrincipal actor)
    {
        string path=context.Request.Path.Value??string.Empty;var catalog=context.RequestServices.GetRequiredService<IdentityCatalog>();
        var policy=ApiRoutePolicy.Classify(path);if(policy is null||policy==ApiPolicyKind.DevelopmentOnly)return false;if(policy==ApiPolicyKind.Admin&&!catalog.IsAdmin(actor))return false;
        if(path.StartsWith("/api/testing",StringComparison.OrdinalIgnoreCase))return false;
        if(path.Equals("/api/sdk/license/select",StringComparison.OrdinalIgnoreCase)||path.Equals("/api/archive/reindex-all",StringComparison.OrdinalIgnoreCase))return catalog.IsAdmin(actor);
        string? queryCompany=context.Request.Query["companyId"].FirstOrDefault(),queryProject=context.Request.Query["projectId"].FirstOrDefault();
        if(queryCompany is not null&&!catalog.CanAccess(actor,queryCompany,queryProject))return false;
        bool hasBody = context.Request.ContentLength is > 0 || context.Request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true ||
            (context.Request.ContentLength is null && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method));
        if(hasBody && context.Request.ContentType?.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)==true)
        {
            context.Request.EnableBuffering();using var document=await JsonDocument.ParseAsync(context.Request.Body,new JsonDocumentOptions{MaxDepth=32},context.RequestAborted);context.Request.Body.Position=0;
            var handles=new List<string>();var transientIds=new List<string>();var jobIds=new List<string>();var scopes=new List<(string Company,string? Project)>();Collect(document.RootElement,handles,transientIds,jobIds,scopes);
            var registry=context.RequestServices.GetRequiredService<FileHandleRegistry>();if(handles.Any(id=>!registry.IsOwnedBy(id,actor)))return false;
            if(context.RequestServices.GetService<TransientResourceOwnershipRegistry>() is { } transient&&transientIds.Any(id=>!registry.IsOwnedBy(id,actor)&&!transient.IsOwnedBy(id,actor)))return false;
            if(context.RequestServices.GetService<BitigMail.LocalHost.Jobs.JobManager>() is { } jobs&&jobIds.Any(id=>jobs.GetJob(id) is not { } job||!jobs.CanAccessJob(actor,job)))return false;
            if(scopes.Any(scope=>!catalog.CanAccess(actor,scope.Company,scope.Project)))return false;
        }
        if(context.RequestServices.GetService<TransientResourceOwnershipRegistry>() is { } routeOwners)
            foreach(string key in new[]{"previewId","planId"})if(context.Request.RouteValues.TryGetValue(key,out var value)&&value is not null&&!routeOwners.IsOwnedBy(value.ToString()!,actor))return false;
        if(context.Request.RouteValues.TryGetValue("jobId",out var routeJobId)&&routeJobId is not null&&context.RequestServices.GetService<BitigMail.LocalHost.Jobs.JobManager>() is { } routeJobs&&(routeJobs.GetJob(routeJobId.ToString()!) is not { } routeJob||!routeJobs.CanAccessJob(actor,routeJob)))return false;
        return true;
    }

    private static void Collect(JsonElement element,List<string> handles,List<string> transientIds,List<string> jobIds,List<(string Company,string? Project)> scopes)
    {
        if(element.ValueKind==JsonValueKind.Array){foreach(var child in element.EnumerateArray())Collect(child,handles,transientIds,jobIds,scopes);return;}if(element.ValueKind!=JsonValueKind.Object)return;
        string? company=null,project=null;
        foreach(var property in element.EnumerateObject())
        {
            if(property.Name.Equals("companyId",StringComparison.OrdinalIgnoreCase)&&property.Value.ValueKind==JsonValueKind.String)company=property.Value.GetString();
            else if(property.Name.Equals("projectId",StringComparison.OrdinalIgnoreCase)&&property.Value.ValueKind==JsonValueKind.String)project=property.Value.GetString();
            if((property.Name.EndsWith("Handle",StringComparison.OrdinalIgnoreCase)||property.Name.Equals("selectionId",StringComparison.OrdinalIgnoreCase))&&property.Value.ValueKind==JsonValueKind.String&&property.Value.GetString() is {Length:>0} id)handles.Add(id);
            if((property.Name.Equals("previewId",StringComparison.OrdinalIgnoreCase)||property.Name.Equals("planId",StringComparison.OrdinalIgnoreCase))&&property.Value.ValueKind==JsonValueKind.String&&property.Value.GetString() is {Length:>0} transientId)transientIds.Add(transientId);
            if((property.Name.Equals("jobId",StringComparison.OrdinalIgnoreCase)||property.Name.Equals("sourceJobId",StringComparison.OrdinalIgnoreCase))&&property.Value.ValueKind==JsonValueKind.String&&property.Value.GetString() is {Length:>0} jobId)jobIds.Add(jobId);
            Collect(property.Value,handles,transientIds,jobIds,scopes);
        }
        if(!string.IsNullOrWhiteSpace(company))scopes.Add((company,project));
    }
}
