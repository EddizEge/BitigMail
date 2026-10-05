using BitigMail.Engine.Archive;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BitigMail.LocalHost.Security;

internal static class ArchiveScopeAuthorization
{
    public static bool Allows(HttpContext context, IdentityCatalog identities, IEnumerable<ArchiveScopeTriple> scopes)
    {
        if (context.Items[typeof(AuthenticatedSessionPrincipal)] is not AuthenticatedSessionPrincipal actor) return true;
        var storage = context.RequestServices.GetRequiredService<ArchiveStorageManager>();
        foreach (var scope in scopes)
        {
            if (scope is null || !identities.CanAccess(actor, scope.CompanyId, scope.ProjectId)) return false;
            var actual = storage.GetArchiveManifest(scope.ArchiveId);
            if (actual is null || actual.CompanyId != scope.CompanyId || actual.ProjectId != scope.ProjectId ||
                !identities.CanAccess(actor, actual.CompanyId, actual.ProjectId)) return false;
        }
        return true;
    }
}
