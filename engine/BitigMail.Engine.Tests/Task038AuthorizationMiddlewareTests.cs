using System.Security.Cryptography;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BitigMail.Engine.Tests;
public sealed class Task038AuthorizationMiddlewareTests:IDisposable
{
 private readonly string _dir=Path.Combine(Path.GetTempPath(),"bitigmail-task038-mw-"+Guid.NewGuid().ToString("N"));
 public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}

 [Fact] public async Task Production_rejects_legacy_anonymous_session_and_missing_bearer()
 {
  var env=await EnvironmentAsync();Assert.Equal(404,await Send(env,"POST","/api/session",null,"{}"));Assert.Equal(401,await Send(env,"GET","/api/jobs",null));
 }

 [Fact] public async Task Valid_session_allows_authorized_scope_but_hides_substitution()
 {
  var env=await EnvironmentAsync();string token=env.Sessions.Issue(env.User.UserId,env.User.SecurityVersion).Token;
  Assert.Equal(204,await Send(env,"GET",$"/api/accounts?companyId={env.Company.CompanyId}&projectId={env.Company.Projects[0].ProjectId}",token));
  Assert.Equal(404,await Send(env,"GET","/api/accounts?companyId=cmp_other&projectId=prj_other",token));
 }

 [Fact] public async Task Mutations_require_exact_origin_and_json()
 {
  var env=await EnvironmentAsync();string token=env.Sessions.Issue(env.User.UserId,env.User.SecurityVersion).Token;
  Assert.Equal(403,await Send(env,"POST","/api/catalog/companies",token,"{}",origin:null));
  Assert.Equal(415,await Send(env,"POST","/api/catalog/companies",token,"{}",contentType:"text/plain"));
 }

 [Fact] public async Task Preview_id_is_hidden_from_other_or_reauthenticated_session()
 {
  var env=await EnvironmentAsync();string ownerToken=env.Sessions.Issue(env.User.UserId,env.User.SecurityVersion).Token;var owner=env.Sessions.Validate(ownerToken,env.Catalog.CurrentSecurityVersion)!;
  var accessor=env.Services.GetRequiredService<IHttpContextAccessor>();accessor.HttpContext=new DefaultHttpContext();accessor.HttpContext.Items[typeof(AuthenticatedSessionPrincipal)]=owner;
  env.Services.GetRequiredService<TransientResourceOwnershipRegistry>().Bind("preview-1");
  Assert.Equal(204,await Send(env,"GET","/api/archive/ingest/preview/preview-1",ownerToken,routePreviewId:"preview-1"));
  string newSessionToken=env.Sessions.Issue(env.User.UserId,env.User.SecurityVersion).Token;
  Assert.Equal(404,await Send(env,"GET","/api/archive/ingest/preview/preview-1",newSessionToken,routePreviewId:"preview-1"));
 }

 private async Task<(IdentityCatalog Catalog,AuthenticatedSessionRegistry Sessions,AuthenticatedUserView User,LocalCompany Company,ServiceProvider Services)> EnvironmentAsync()
 {
  var catalog=new IdentityCatalog(_dir);byte[] proof=RandomNumberGenerator.GetBytes(32);var user=await catalog.BootstrapAdministratorAsync("admin","correct horse battery",()=>true);var actor=new AuthenticatedSessionPrincipal(user.UserId,"seed",user.SecurityVersion);var company=await catalog.CreateCompanyAsync(actor,"Acme","Project");var sessions=new AuthenticatedSessionRegistry();var services=new ServiceCollection().AddSingleton(catalog).AddSingleton(sessions).AddSingleton<SessionManager>().AddSingleton<FileHandleRegistry>().AddSingleton<IHttpContextAccessor,HttpContextAccessor>().AddSingleton<TransientResourceOwnershipRegistry>().BuildServiceProvider();return(catalog,sessions,user,company,services);
 }
 private static async Task<int> Send((IdentityCatalog Catalog,AuthenticatedSessionRegistry Sessions,AuthenticatedUserView User,LocalCompany Company,ServiceProvider Services) env,string method,string path,string? token,string? body=null,string? origin=SecurityConfig.RequiredOrigin,string contentType="application/json",string? routePreviewId=null)
 {
  var context=new DefaultHttpContext{RequestServices=env.Services};context.Request.Method=method;context.Request.Host=new HostString("127.0.0.1:6174");context.Request.Path=path.Split('?')[0];context.Request.QueryString=path.Contains('?')?new QueryString("?"+path.Split('?',2)[1]):QueryString.Empty;if(routePreviewId is not null)context.Request.RouteValues["previewId"]=routePreviewId;if(origin is not null)context.Request.Headers.Origin=origin;if(token is not null)context.Request.Headers[SecurityConfig.SessionHeaderName]=token;if(body is not null){context.Request.ContentType=contentType;context.Request.Body=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));context.Request.ContentLength=context.Request.Body.Length;}context.Response.Body=new MemoryStream();var middleware=new LocalSecurityMiddleware(c=>{c.Response.StatusCode=204;return Task.CompletedTask;},new SecurityConfig{ExpectedHost="127.0.0.1:6174"});await middleware.InvokeAsync(context);return context.Response.StatusCode;
 }
}
