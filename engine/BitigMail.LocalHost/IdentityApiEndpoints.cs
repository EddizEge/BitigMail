using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost;
public sealed record FirstAdminRequest(string UserName,string Password,string Proof);
public sealed record LoginRequest(string UserName,string Password);
public sealed record CreateCompanyRequest(string Name,string ProjectName);
public sealed record CreateProjectRequest(string Name);
public sealed record CreateLocalUserRequest(string UserName,string Password,LocalUserRole Role,IReadOnlyList<CompanyGrant>? Grants);
public sealed record UpdateLocalUserRequest(LocalUserRole? Role,bool? Active,IReadOnlyList<CompanyGrant>? Grants,string? NewPassword);
public static class IdentityApiEndpoints
{
 public static void MapIdentityEndpoints(this WebApplication app)
 {
  app.MapGet("/api/setup/status",(IdentityCatalog catalog,SetupProofGate proof)=>Results.Ok(new{initialized=catalog.IsInitialized,nativeProofAvailable=proof.Available}));
  app.MapPost("/api/setup/first-admin",async (IdentityCatalog catalog,SetupProofGate proof,AuthenticatedSessionRegistry sessions,FirstAdminRequest req,CancellationToken ct)=>
  {try{if(catalog.IsInitialized)return Results.Conflict(new{error="Kurulum zaten tamamlandı."});if(string.IsNullOrWhiteSpace(req?.UserName)||req.UserName.Length>128||req.Password is null||req.Proof is null)return Results.BadRequest(new{error="Kurulum bilgileri geçersiz."});var user=await catalog.BootstrapAdministratorAsync(req.UserName,req.Password,()=>proof.TryConsume(req.Proof),ct);var issued=sessions.Issue(user.UserId,user.SecurityVersion);return Results.Ok(new{token=issued.Token,user});}catch(UnauthorizedAccessException){return Results.Json(new{code="SETUP_PROOF_INVALID",error="Kurulum doğrulaması geçersiz veya süresi dolmuş. Yönetici oluşturmayı yeniden deneyin; sorun sürerse BitigMail uygulamasından çıkıp yeniden açın."},statusCode:401);}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}catch(InvalidOperationException){return Results.Conflict(new{error="Kurulum zaten tamamlandı."});}});
  app.MapPost("/api/auth/login",async (HttpContext http,IdentityCatalog catalog,AuthenticatedSessionRegistry sessions,LoginRateLimiter limiter,LoginRequest req,CancellationToken ct)=>
  {if(string.IsNullOrWhiteSpace(req?.UserName)||req.UserName.Length>128||req.Password is null||req.Password.Length>1024)return Results.Unauthorized();string key=(http.Connection.RemoteIpAddress?.ToString()??"local")+":"+req.UserName.Trim().ToUpperInvariant();if(!limiter.IsAllowed(key))return Results.Json(new{error="Oturum açılamadı."},statusCode:429);var user=await catalog.AuthenticateAsync(req.UserName,req.Password,ct);if(user is null){limiter.Failed(key);return Results.Unauthorized();}limiter.Succeeded(key);var issued=sessions.Issue(user.UserId,user.SecurityVersion);return Results.Ok(new{token=issued.Token,user});});
  app.MapPost("/api/auth/logout",(HttpContext http,AuthenticatedSessionRegistry sessions)=>{sessions.Revoke(http.Request.Headers[SecurityConfig.SessionHeaderName].FirstOrDefault());return Results.Ok(new{loggedOut=true});});
  app.MapGet("/api/auth/me",(HttpContext http,IdentityCatalog catalog)=>{var actor=Actor(http);return Results.Ok(new{actor.UserId,role=catalog.IsAdmin(actor)?"Admin":"Operator",companies=catalog.ListCompanies(actor)});});
  app.MapGet("/api/catalog/companies",(HttpContext http,IdentityCatalog catalog)=>Results.Ok(catalog.ListCompanies(Actor(http))));
  app.MapPost("/api/catalog/companies",async (HttpContext http,IdentityCatalog catalog,CreateCompanyRequest req,CancellationToken ct)=>{try{return Results.Ok(await catalog.CreateCompanyAsync(Actor(http),req.Name,req.ProjectName,ct));}catch(UnauthorizedAccessException){return Results.NotFound();}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}});
  app.MapPost("/api/catalog/companies/{companyId}/projects", async (HttpContext http, IdentityCatalog catalog, string companyId, CreateProjectRequest req, CancellationToken ct) =>
  {
   try { return Results.Ok(await catalog.CreateProjectAsync(Actor(http), companyId, req.Name, ct)); }
   catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException) { return Results.NotFound(); }
   catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = ex.Message }); }
  });
  app.MapGet("/api/admin/users",(HttpContext http,IdentityCatalog catalog)=>{try{return Results.Ok(catalog.ListUsers(Actor(http)));}catch(UnauthorizedAccessException){return Results.NotFound();}});
  app.MapGet("/api/admin/audit",(HttpContext http,IdentityCatalog catalog,AuditLogStore audit)=>{try{if(!catalog.IsAdmin(Actor(http)))return Results.NotFound();return Results.Ok(audit.Read());}catch(UnauthorizedAccessException){return Results.NotFound();}});
  app.MapPost("/api/admin/users",async (HttpContext http,IdentityCatalog catalog,CreateLocalUserRequest req,CancellationToken ct)=>{try{return Results.Ok(await catalog.CreateUserAsync(Actor(http),req.UserName,req.Password,req.Role,req.Grants,ct));}catch(UnauthorizedAccessException){return Results.NotFound();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException){return Results.BadRequest(new{error=ex.Message});}});
  app.MapPut("/api/admin/users/{userId}",async (HttpContext http,IdentityCatalog catalog,AuthenticatedSessionRegistry sessions,string userId,UpdateLocalUserRequest req,CancellationToken ct)=>
  {try{var updated=await catalog.UpdateUserAsync(Actor(http),userId,req.Role,req.Active,req.Grants,req.NewPassword,ct);sessions.RevokeUser(userId);return Results.Ok(updated);}catch(Exception ex)when(ex is UnauthorizedAccessException or KeyNotFoundException){return Results.NotFound();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException){return Results.BadRequest(new{error=ex.Message});}});
 }
 internal static AuthenticatedSessionPrincipal Actor(HttpContext context)=>context.Items[typeof(AuthenticatedSessionPrincipal)] as AuthenticatedSessionPrincipal??throw new UnauthorizedAccessException();
}
