using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost.Security;

internal sealed class EphemeralOperationAuthorization(IHttpContextAccessor? http, IdentityCatalog? identities,
    AuthenticatedSessionRegistry? sessions, SecurityConfig? config)
{
    public bool Enabled => identities is not null && config?.DevelopmentAnonymousSession != true;

    public AuthenticatedSessionPrincipal? Capture(string companyId, string projectId)
    {
        if (!Enabled) return null;
        var actor = http?.HttpContext?.Items[typeof(AuthenticatedSessionPrincipal)] as AuthenticatedSessionPrincipal;
        if (!IsCurrent(actor, companyId, projectId)) throw new KeyNotFoundException("İşlem kapsamı bulunamadı.");
        return actor;
    }

    public bool IsCaller(AuthenticatedSessionPrincipal? owner, string companyId, string projectId)
    {
        if (!Enabled) return true;
        var caller = http?.HttpContext?.Items[typeof(AuthenticatedSessionPrincipal)] as AuthenticatedSessionPrincipal;
        return owner is not null && caller == owner && IsCurrent(owner, companyId, projectId);
    }

    public void DemandCurrent(AuthenticatedSessionPrincipal? owner, string companyId, string projectId)
    {
        if (Enabled && !IsCurrent(owner, companyId, projectId)) throw new UnauthorizedAccessException("İşlem oturumu veya kapsam yetkisi sona erdi.");
    }

    private bool IsCurrent(AuthenticatedSessionPrincipal? owner, string companyId, string projectId)
    {
        if (owner is null || sessions is null || identities is null || !sessions.IsActive(owner)) return false;
        try { return identities.CanAccess(owner, companyId, projectId) && sessions.IsActive(owner); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        { return false; }
    }
}
