using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class CatalogProjectCreationTests
{
    [Fact]
    public async Task NewProjectPersistsAndDoesNotExtendOperatorGrants()
    {
        string path = Path.Combine(Path.GetTempPath(), "bitigmail-project-test", Guid.NewGuid().ToString("N"));
        var catalog = new IdentityCatalog(path);
        var admin = await catalog.BootstrapAdministratorAsync("admin", "synthetic-password-only", () => true);
        var actor = new AuthenticatedSessionPrincipal(admin.UserId, "test", admin.SecurityVersion);
        var company = await catalog.CreateCompanyAsync(actor, "Synthetic company", "First project");
        var user = await catalog.CreateUserAsync(actor, "operator", "synthetic-password-only", LocalUserRole.Operator,
            new[] { new CompanyGrant(company.CompanyId, new[] { company.Projects[0].ProjectId }) });
        var limited = new AuthenticatedSessionPrincipal(user.UserId, "operator-session", user.SecurityVersion);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => catalog.CreateProjectAsync(limited, company.CompanyId, "Forbidden"));
        var project = await catalog.CreateProjectAsync(actor, company.CompanyId, "Second project");
        var reloaded = new IdentityCatalog(path);
        Assert.Equal(2, reloaded.ListCompanies(actor).Single().Projects.Count);
        Assert.True(reloaded.CanAccess(actor, company.CompanyId, project.ProjectId));
        Assert.False(reloaded.CanAccess(limited, company.CompanyId, project.ProjectId));
        Assert.Single(reloaded.ListCompanies(limited).Single().Projects);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reloaded.CreateProjectAsync(actor, company.CompanyId, "second project"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reloaded.CreateProjectAsync(actor, "missing", "Another"));
    }
}
