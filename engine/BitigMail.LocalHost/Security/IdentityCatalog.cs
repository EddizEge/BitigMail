using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace BitigMail.LocalHost.Security;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LocalUserRole { Admin, Operator }
public sealed record CompanyGrant(string CompanyId, IReadOnlyList<string> ProjectIds);
public sealed record LocalUser(string UserId,string UserName,string NormalizedUserName,string PasswordHash,LocalUserRole Role,bool Active,long SecurityVersion,IReadOnlyList<CompanyGrant> Grants,DateTimeOffset CreatedAtUtc);
public sealed record LocalCompany(string CompanyId,string Name,bool Active,IReadOnlyList<LocalProject> Projects);
public sealed record LocalProject(string ProjectId,string Name,bool Active);
public sealed record IdentityCatalogDocument(int Version,IReadOnlyList<LocalUser> Users,IReadOnlyList<LocalCompany> Companies);
public sealed record AuthenticatedUserView(string UserId,string UserName,LocalUserRole Role,long SecurityVersion,IReadOnlyList<CompanyGrant> Grants);

public sealed partial class IdentityCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web){WriteIndented=true,PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    private readonly string _profileDirectory,_path; private readonly object _gate=new(); private readonly PasswordHasher<LocalUser> _hasher=new();private static readonly SemaphoreSlim _passwordVerifications=new(4,4);
    public IdentityCatalog(string profileDirectory){_profileDirectory=Path.GetFullPath(profileDirectory);RejectLinks(_profileDirectory);Directory.CreateDirectory(_profileDirectory);_path=Path.Combine(_profileDirectory,"identity-catalog.json");}
    public bool IsInitialized { get { lock(_gate)return File.Exists(_path)&&Load().Users.Any(x=>x.Active&&x.Role==LocalUserRole.Admin); } }
    public long? CurrentSecurityVersion(string userId){lock(_gate){var user=Load().Users.SingleOrDefault(x=>x.UserId==userId&&x.Active);return user?.SecurityVersion;}}
    public async Task<AuthenticatedUserView?> AuthenticateAsync(string userName,string password,CancellationToken cancellationToken=default)
    {
        if(!ValidName(userName)||password is null||password.Length is <12 or >256)return null;
        LocalUser? user;lock(_gate)user=Load().Users.SingleOrDefault(x=>x.Active&&x.NormalizedUserName==Normalize(userName));if(user is null)return null;PasswordVerificationResult result;await _passwordVerifications.WaitAsync(cancellationToken);try{result=_hasher.VerifyHashedPassword(user,user.PasswordHash,password);}finally{_passwordVerifications.Release();}if(result==PasswordVerificationResult.Failed)return null;lock(_gate){var current=Load().Users.SingleOrDefault(x=>x.Active&&x.UserId==user.UserId&&x.SecurityVersion==user.SecurityVersion&&x.PasswordHash==user.PasswordHash);return current is null?null:View(current);}
    }
    public async Task<AuthenticatedUserView> BootstrapAdministratorAsync(string userName,string password,Func<bool> consumeNativeProof,CancellationToken cancellationToken=default)
    {
        ValidateUserName(userName);ValidatePassword(password);
        var shell=new LocalUser("usr_"+Guid.NewGuid().ToString("N"),userName.Trim(),Normalize(userName),"",LocalUserRole.Admin,true,1,Array.Empty<CompanyGrant>(),DateTimeOffset.UtcNow);var user=shell with{PasswordHash=await HashPasswordAsync(shell,password,cancellationToken)};
        await using var lease=await ProfileWriteLease.AcquireAsync(_profileDirectory,cancellationToken);lock(_gate){var current=Load();if(current.Users.Any())throw new InvalidOperationException("İlk yönetici kurulumu daha önce tamamlandı.");if(!consumeNativeProof())throw new UnauthorizedAccessException();Save(current with{Users=new[]{user}});return View(user);}
    }
    public IReadOnlyList<LocalCompany> ListCompanies(AuthenticatedSessionPrincipal actor){lock(_gate){var doc=Load();var user=Require(doc,actor);var all=doc.Companies.Where(x=>x.Active);if(user.Role==LocalUserRole.Admin)return all.ToArray();return all.Select(c=>(Company:c,Grant:user.Grants.SingleOrDefault(g=>g.CompanyId==c.CompanyId))).Where(x=>x.Grant is not null).Select(x=>x.Company with{Projects=x.Company.Projects.Where(p=>p.Active&&x.Grant!.ProjectIds.Contains(p.ProjectId,StringComparer.Ordinal)).ToArray()}).Where(c=>c.Projects.Count>0).ToArray();}}
    public async Task<LocalCompany> CreateCompanyAsync(AuthenticatedSessionPrincipal actor,string name,string projectName,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>128||string.IsNullOrWhiteSpace(projectName)||projectName.Length>128)throw new ArgumentException("Şirket ve proje adı geçersiz.");
        await using var lease=await ProfileWriteLease.AcquireAsync(_profileDirectory,cancellationToken);lock(_gate){var doc=Load();RequireAdmin(doc,actor);var company=new LocalCompany("cmp_"+Guid.NewGuid().ToString("N"),name.Trim(),true,new[]{new LocalProject("prj_"+Guid.NewGuid().ToString("N"),projectName.Trim(),true)});Save(doc with{Companies=doc.Companies.Append(company).ToArray()});return company;}
    }
    public bool CanAccess(AuthenticatedSessionPrincipal actor,string companyId,string? projectId=null)
    {
        lock(_gate){var doc=Load();var user=Require(doc,actor);var company=doc.Companies.SingleOrDefault(x=>x.Active&&x.CompanyId==companyId);if(company is null)return false;if(projectId is not null&&!company.Projects.Any(x=>x.Active&&x.ProjectId==projectId))return false;if(user.Role==LocalUserRole.Admin)return true;var grant=user.Grants.SingleOrDefault(x=>x.CompanyId==companyId);return grant is not null&&company.Projects.Any(p=>p.Active&&(projectId is null||p.ProjectId==projectId)&&grant.ProjectIds.Contains(p.ProjectId,StringComparer.Ordinal));}
    }
    public bool IsAdmin(AuthenticatedSessionPrincipal actor){lock(_gate)return Require(actor).Role==LocalUserRole.Admin;}
    public IReadOnlyList<AuthenticatedUserView> ListUsers(AuthenticatedSessionPrincipal actor){lock(_gate){var doc=Load();RequireAdmin(doc,actor);return doc.Users.Select(View).ToArray();}}
    public async Task<AuthenticatedUserView> CreateUserAsync(AuthenticatedSessionPrincipal actor,string userName,string password,LocalUserRole role,IReadOnlyList<CompanyGrant>? grants,CancellationToken cancellationToken=default)
    {
        ValidateUserName(userName);ValidatePassword(password);if(!Enum.IsDefined(role))throw new ArgumentException("Kullanıcı rolü geçersiz.");grants=CopyGrants(grants??Array.Empty<CompanyGrant>());lock(_gate)RequireAdmin(actor);var shell=new LocalUser("usr_"+Guid.NewGuid().ToString("N"),userName.Trim(),Normalize(userName),"",role,true,1,grants??Array.Empty<CompanyGrant>(),DateTimeOffset.UtcNow);var user=shell with{PasswordHash=await HashPasswordAsync(shell,password,cancellationToken)};
        await using var lease=await ProfileWriteLease.AcquireAsync(_profileDirectory,cancellationToken);lock(_gate){var doc=Load();RequireAdmin(doc,actor);if(doc.Users.Any(x=>x.NormalizedUserName==user.NormalizedUserName))throw new InvalidOperationException("Kullanıcı adı zaten var.");foreach(var grant in user.Grants)if(!doc.Companies.Any(c=>c.Active&&c.CompanyId==grant.CompanyId&&grant.ProjectIds.All(p=>c.Projects.Any(x=>x.Active&&x.ProjectId==p))))throw new ArgumentException("Kullanıcı kapsamı geçersiz.");Save(doc with{Users=doc.Users.Append(user).ToArray()});return View(user);}
    }
    private LocalUser Require(AuthenticatedSessionPrincipal actor)=>Require(Load(),actor);
    private static LocalUser Require(IdentityCatalogDocument doc,AuthenticatedSessionPrincipal actor)=>doc.Users.SingleOrDefault(x=>x.Active&&x.UserId==actor.UserId&&x.SecurityVersion==actor.SecurityVersion)??throw new UnauthorizedAccessException();
    private void RequireAdmin(AuthenticatedSessionPrincipal actor){if(Require(actor).Role!=LocalUserRole.Admin)throw new UnauthorizedAccessException();}
    private static void RequireAdmin(IdentityCatalogDocument doc,AuthenticatedSessionPrincipal actor){if(Require(doc,actor).Role!=LocalUserRole.Admin)throw new UnauthorizedAccessException();}
    private static string Normalize(string value)=>value.Trim().Normalize().ToUpperInvariant();
    private static void ValidateUserName(string value){if(!ValidName(value))throw new ArgumentException("Kullanıcı adı geçersiz.");}
    private static void ValidatePassword(string value){if(value is null||value.Length<12||value.Length>256)throw new ArgumentException("Parola 12-256 karakter olmalıdır.");}
    private static AuthenticatedUserView View(LocalUser user)=>new(user.UserId,user.UserName,user.Role,user.SecurityVersion,CopyGrants(user.Grants));
}
