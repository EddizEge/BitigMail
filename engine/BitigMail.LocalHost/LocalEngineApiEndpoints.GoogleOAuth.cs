using BitigMail.Engine.Imap.OAuth;
using BitigMail.Engine.Imap;
using BitigMail.LocalHost.OAuth;

namespace BitigMail.LocalHost;

public static class LocalEngineApiEndpointsGoogleOAuth
{
    public static void MapGoogleOAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/providers/capabilities", () => Results.Ok(new ProviderCapabilityDto[]
        {
            new() { Id = "imap", DisplayName = "Genel IMAP", AuthKind = "password", Endpoint = "Yapılandırılmış TLS IMAP", Source = true, Target = true, OAuth = false, SupportLevel = "supported" },
            new() { Id = "exchange-online", DisplayName = "Exchange Online", AuthKind = "microsoft365", Endpoint = "outlook.office365.com:993", Source = true, Target = true, OAuth = true, SupportLevel = "local_ready" },
            new() { Id = "outlook-personal", DisplayName = "Outlook.com / Hotmail", AuthKind = "microsoft365", Endpoint = "outlook.office365.com:993", Source = true, Target = true, OAuth = true, SupportLevel = "local_ready" },
            new() { Id = "google", DisplayName = "Gmail / Google Workspace", AuthKind = "google", Endpoint = "imap.gmail.com:993", Source = true, Target = true, OAuth = true, SupportLevel = "local_ready_live_pending" },
            new() { Id = "exchange-onprem", DisplayName = "Şirket içi Exchange", AuthKind = "password", Endpoint = "Yapılandırılmış IMAP", Source = true, Target = true, OAuth = false, SupportLevel = "unverified_configured_imap_only", UnsupportedCapabilities = ["EWS", "MAPI", "NTLM/Kerberos", "Autodiscover", "Hybrid OAuth"] }
        }));
        app.MapPost("/api/oauth/google/start", (GoogleOAuthOperationManager manager, StartGoogleOAuthRequest request) =>
        {
            try { return Results.Ok(manager.StartOperation(request)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch { return Results.BadRequest(new { error = "Google oturum açma işlemi başlatılamadı." }); }
        });
        app.MapGet("/api/oauth/google/operations/{id}", (GoogleOAuthOperationManager manager, string id, string? companyId, string? projectId) =>
        {
            if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId)) return Results.BadRequest(new { error = "Müşteri ve proje kimliği zorunludur." });
            try { var result = manager.GetOperation(id, companyId, projectId); return result is null ? Results.NotFound(new { error = "İşlem bulunamadı." }) : Results.Ok(result); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
        app.MapPost("/api/oauth/google/operations/{id}/cancel", (GoogleOAuthOperationManager manager, string id, string? companyId, string? projectId) =>
        {
            if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId)) return Results.BadRequest(new { error = "Müşteri ve proje kimliği zorunludur." });
            try { var result = manager.Cancel(id, companyId, projectId); return result is null ? Results.NotFound(new { error = "İşlem bulunamadı." }) : Results.Ok(result); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        });
    }
}
