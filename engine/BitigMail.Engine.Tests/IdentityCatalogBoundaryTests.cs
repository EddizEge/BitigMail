using System.Text.Json;
using System.Text.Json.Nodes;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;

public sealed class IdentityCatalogBoundaryTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bitigmail-root038-catalog-" + Guid.NewGuid().ToString("N"));
    private string CatalogPath => Path.Combine(_root, "identity-catalog.json");
    private async Task<(IdentityCatalog Catalog, AuthenticatedSessionPrincipal Admin)> Seed()
    {
        var catalog = new IdentityCatalog(_root);
        var admin = await catalog.BootstrapAdministratorAsync("admin", "correct horse battery staple", () => true);
        return (catalog, new(admin.UserId, "test-session", admin.SecurityVersion));
    }

    [Fact]
    public async Task SameCompanyDoesNotExposeUngrantedProject()
    {
        var (catalog, admin) = await Seed();
        var company = await catalog.CreateCompanyAsync(admin, "Company", "Visible");
        var json = JsonNode.Parse(File.ReadAllText(CatalogPath))!;
        json["companies"]![0]!["projects"]!.AsArray().Add(JsonSerializer.SerializeToNode(new LocalProject("prj_private", "Private", true), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        File.WriteAllText(CatalogPath, json.ToJsonString());
        var user = await catalog.CreateUserAsync(admin, "operator", "correct horse battery staple", LocalUserRole.Operator,
            new[] { new CompanyGrant(company.CompanyId, new[] { company.Projects[0].ProjectId }) });
        var actor = new AuthenticatedSessionPrincipal(user.UserId, "operator-session", user.SecurityVersion);
        Assert.Equal(company.Projects[0].ProjectId, Assert.Single(Assert.Single(catalog.ListCompanies(actor)).Projects).ProjectId);
        Assert.False(catalog.CanAccess(actor, company.CompanyId, "prj_private"));
    }

    [Theory]
    [InlineData("version", "99")]
    [InlineData("users", "null")]
    [InlineData("companies", "null")]
    public async Task InvalidSchemaFailsClosed(string key, string replacement)
    {
        var (catalog, _) = await Seed();
        var json = JsonNode.Parse(File.ReadAllText(CatalogPath))!;
        json[key] = JsonNode.Parse(replacement);
        File.WriteAllText(CatalogPath, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => _ = catalog.IsInitialized);
    }

    [Fact]
    public async Task DuplicateJsonFieldCannotRedefineCatalogVersion()
    {
        var (catalog, _) = await Seed();
        var json = File.ReadAllText(CatalogPath);
        File.WriteAllText(CatalogPath, "{\"version\":999," + json[1..]);
        Assert.Throws<InvalidDataException>(() => _ = catalog.IsInitialized);
    }

    [Fact]
    public async Task ConcurrentCatalogInstancesCannotLoseCompanyWrites()
    {
        var (_, admin) = await Seed();
        var instances = Enumerable.Range(0, 8).Select(_ => new IdentityCatalog(_root)).ToArray();
        await Task.WhenAll(instances.Select((c, i) => c.CreateCompanyAsync(admin, "Company " + i, "Project")));
        Assert.Equal(8, new IdentityCatalog(_root).ListCompanies(admin).Count);
    }

    [Fact]
    public async Task InvalidRoleDoesNotPersistUser()
    {
        var (catalog, admin) = await Seed();
        string before = File.ReadAllText(CatalogPath);
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateUserAsync(admin, "operator", "correct horse battery staple", (LocalUserRole)777, null));
        Assert.Equal(before, File.ReadAllText(CatalogPath));
    }

    [Fact]
    public async Task LastAdministratorCannotBeDisabledOrDemoted()
    {
        var (catalog, admin) = await Seed();
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.UpdateUserAsync(admin, admin.UserId, active: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.UpdateUserAsync(admin, admin.UserId, role: LocalUserRole.Operator));
        Assert.Equal(admin.SecurityVersion, catalog.CurrentSecurityVersion(admin.UserId));
    }

    [Fact]
    public async Task PasswordChangeInvalidatesSessionAndOldPassword()
    {
        var (catalog, admin) = await Seed();
        var sessions = new AuthenticatedSessionRegistry();
        var session = sessions.Issue(admin.UserId, admin.SecurityVersion);
        var changed = await catalog.UpdateUserAsync(admin, admin.UserId, newPassword: "another secure password value");
        Assert.Equal(admin.SecurityVersion + 1, changed.SecurityVersion);
        Assert.Null(sessions.Validate(session.Token, catalog.CurrentSecurityVersion));
        Assert.Null(await catalog.AuthenticateAsync("admin", "correct horse battery staple"));
        Assert.NotNull(await catalog.AuthenticateAsync("admin", "another secure password value"));
        Assert.Throws<UnauthorizedAccessException>(() => catalog.ListUsers(admin));
    }

    [Fact]
    public async Task ScopeRevocationAndDisableAreImmediatelyAuthoritativeAcrossInstances()
    {
        var (catalog, admin) = await Seed();
        var company = await catalog.CreateCompanyAsync(admin, "Company", "Project");
        var user = await catalog.CreateUserAsync(admin, "operator", "correct horse battery staple", LocalUserRole.Operator,
            new[] { new CompanyGrant(company.CompanyId, new[] { company.Projects[0].ProjectId }) });
        var actor = new AuthenticatedSessionPrincipal(user.UserId, "operator-session", user.SecurityVersion);
        var second = new IdentityCatalog(_root);
        Assert.True(second.CanAccess(actor, company.CompanyId, company.Projects[0].ProjectId));
        var updated = await catalog.UpdateUserAsync(admin, user.UserId, grants: Array.Empty<CompanyGrant>());
        Assert.Throws<UnauthorizedAccessException>(() => second.CanAccess(actor, company.CompanyId));
        Assert.False(second.CanAccess(new(user.UserId, "new-session", updated.SecurityVersion), company.CompanyId));
        await catalog.UpdateUserAsync(admin, user.UserId, active: false);
        Assert.Null(second.CurrentSecurityVersion(user.UserId));
        Assert.Null(await second.AuthenticateAsync("operator", "correct horse battery staple"));
    }

    [Fact]
    public async Task NoopUpdateDoesNotInvalidateSessionAndOperatorCannotEditAccounts()
    {
        var (catalog, admin) = await Seed();
        Assert.Equal(admin.SecurityVersion, (await catalog.UpdateUserAsync(admin, admin.UserId, role: LocalUserRole.Admin, active: true)).SecurityVersion);
        var user = await catalog.CreateUserAsync(admin, "operator", "correct horse battery staple", LocalUserRole.Operator, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => catalog.UpdateUserAsync(new(user.UserId, "session", user.SecurityVersion), admin.UserId, active: false));
    }

    [Fact]
    public async Task CorruptHashWithExcessiveWorkFactorIsRejectedBeforePasswordVerification()
    {
        var (catalog, _) = await Seed();
        var json = JsonNode.Parse(File.ReadAllText(CatalogPath))!;
        var hash = Convert.FromBase64String(json["users"]![0]!["passwordHash"]!.GetValue<string>());
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(hash.AsSpan(5, 4), uint.MaxValue);
        json["users"]![0]!["passwordHash"] = Convert.ToBase64String(hash);
        File.WriteAllText(CatalogPath, json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.AuthenticateAsync("admin", "correct horse battery staple"));
    }

    [Fact]
    public async Task ReturnedGrantCollectionsCannotMutateAuthoritativeState()
    {
        var (catalog, admin) = await Seed();
        var company = await catalog.CreateCompanyAsync(admin, "Company", "Project");
        var input = new[] { company.Projects[0].ProjectId };
        var user = await catalog.CreateUserAsync(admin, "operator", "correct horse battery staple", LocalUserRole.Operator,
            new[] { new CompanyGrant(company.CompanyId, input) });
        input[0] = "prj_other";
        Assert.Throws<NotSupportedException>(() => ((IList<string>)user.Grants[0].ProjectIds)[0] = "prj_other");
        Assert.True(catalog.CanAccess(new(user.UserId, "session", user.SecurityVersion), company.CompanyId, company.Projects[0].ProjectId));
    }

    [Fact]
    public async Task SimultaneousFirstRunCreatesExactlyOneAdminAndConsumesOneProof()
    {
        int consumed = 0;
        var attempts = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            try
            {
                await new IdentityCatalog(_root).BootstrapAdministratorAsync("admin" + i, "correct horse battery staple",
                    () => { Interlocked.Increment(ref consumed); return true; });
                return true;
            }
            catch (InvalidOperationException) { return false; }
        })).ToArray();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results, x => x); Assert.Equal(1, consumed);
    }

    [Fact]
    public async Task ConcurrentAdminSelfDisableCannotRemoveBothAdmins()
    {
        var (catalog, admin) = await Seed();
        var user = await catalog.CreateUserAsync(admin, "second-admin", "correct horse battery staple", LocalUserRole.Admin, null);
        var second = new AuthenticatedSessionPrincipal(user.UserId, "second-session", user.SecurityVersion);
        async Task<bool> Disable(AuthenticatedSessionPrincipal actor)
        {
            try { await new IdentityCatalog(_root).UpdateUserAsync(actor, actor.UserId, active: false); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Disable(admin)), Task.Run(() => Disable(second)));
        Assert.Single(results, x => x);
        Assert.True(new IdentityCatalog(_root).IsInitialized);
        Assert.Equal(1, new[] { catalog.CurrentSecurityVersion(admin.UserId), catalog.CurrentSecurityVersion(second.UserId) }.Count(x => x.HasValue));
    }
}
