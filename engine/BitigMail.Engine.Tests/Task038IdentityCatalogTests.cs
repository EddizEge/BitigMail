using System.Security.Cryptography;
using BitigMail.LocalHost.Security;
using Xunit;

namespace BitigMail.Engine.Tests;
public sealed class Task038IdentityCatalogTests:IDisposable
{
 private readonly string _dir=Path.Combine(Path.GetTempPath(),"bitigmail-task038-"+Guid.NewGuid().ToString("N"));
 public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}

 [Fact] public async Task Bootstrap_hashes_password_and_authenticates_without_plaintext()
 {
  byte[] proof=RandomNumberGenerator.GetBytes(32);using var gate=new SetupProofGate();gate.InstallFromNativeChannel(proof);var catalog=new IdentityCatalog(_dir);
  var user=await catalog.BootstrapAdministratorAsync("admin","correct horse battery",()=>gate.TryConsume(Convert.ToHexString(proof).ToLowerInvariant()));
  Assert.Equal(LocalUserRole.Admin,user.Role);Assert.NotNull(await catalog.AuthenticateAsync("ADMIN","correct horse battery"));Assert.Null(await catalog.AuthenticateAsync("admin","wrong password value"));
  string stored=File.ReadAllText(Path.Combine(_dir,"identity-catalog.json"));Assert.DoesNotContain("correct horse battery",stored);Assert.DoesNotContain(Convert.ToHexString(proof),stored,StringComparison.OrdinalIgnoreCase);
 }

 [Fact] public async Task Bootstrap_proof_is_single_use_and_catalog_is_single_admin()
 {
  byte[] proof=RandomNumberGenerator.GetBytes(32);using var gate=new SetupProofGate();gate.InstallFromNativeChannel(proof);var catalog=new IdentityCatalog(_dir);string value=Convert.ToHexString(proof).ToLowerInvariant();
  await catalog.BootstrapAdministratorAsync("admin","correct horse battery",()=>gate.TryConsume(value));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>catalog.BootstrapAdministratorAsync("second","another secure password",()=>gate.TryConsume(value)));
 }

 [Fact] public void Corrupt_catalog_fails_closed()
 {Directory.CreateDirectory(_dir);File.WriteAllText(Path.Combine(_dir,"identity-catalog.json"),"{broken");var catalog=new IdentityCatalog(_dir);Assert.Throws<InvalidDataException>(()=>_ = catalog.IsInitialized);}

 [Fact] public async Task Admin_company_creation_and_scope_are_server_resolved()
 {
  byte[] proof=RandomNumberGenerator.GetBytes(32);using var gate=new SetupProofGate();gate.InstallFromNativeChannel(proof);var catalog=new IdentityCatalog(_dir);var user=await catalog.BootstrapAdministratorAsync("admin","correct horse battery",()=>gate.TryConsume(Convert.ToHexString(proof).ToLowerInvariant()));var actor=new AuthenticatedSessionPrincipal(user.UserId,"session",user.SecurityVersion);
  var company=await catalog.CreateCompanyAsync(actor,"Acme","Migration");Assert.True(catalog.CanAccess(actor,company.CompanyId,company.Projects[0].ProjectId));Assert.False(catalog.CanAccess(actor,"cmp_other","prj_other"));
 }

 [Fact] public async Task Session_uses_authoritative_security_version()
 {
  byte[] proof=RandomNumberGenerator.GetBytes(32);using var gate=new SetupProofGate();gate.InstallFromNativeChannel(proof);var catalog=new IdentityCatalog(_dir);var user=await catalog.BootstrapAdministratorAsync("admin","correct horse battery",()=>gate.TryConsume(Convert.ToHexString(proof).ToLowerInvariant()));var sessions=new AuthenticatedSessionRegistry();var issued=sessions.Issue(user.UserId,user.SecurityVersion);Assert.NotNull(sessions.Validate(issued.Token,catalog.CurrentSecurityVersion));Assert.Null(sessions.Validate(issued.Token,_=>user.SecurityVersion+1));
 }
}
