namespace BitigMail.LocalHost.Security;

public sealed partial class IdentityCatalog
{
    public async Task<LocalProject> CreateProjectAsync(AuthenticatedSessionPrincipal actor, string companyId,
        string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw new ArgumentException("Proje adı 1–128 karakter olmalıdır.");
        await using var lease = await ProfileWriteLease.AcquireAsync(_profileDirectory, cancellationToken);
        lock (_gate)
        {
            var document = Load();
            RequireAdmin(document, actor);
            var company = document.Companies.SingleOrDefault(item => item.Active && item.CompanyId == companyId)
                ?? throw new KeyNotFoundException("Müşteri bulunamadı.");
            if (company.Projects.Any(item => item.Active && string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Bu müşteride aynı adlı bir proje zaten var.");
            var project = new LocalProject("prj_" + Guid.NewGuid().ToString("N"), name.Trim(), true);
            var updated = company with { Projects = company.Projects.Append(project).ToArray() };
            Save(document with { Companies = document.Companies.Select(item => item.CompanyId == companyId ? updated : item).ToArray() });
            return project;
        }
    }
}
