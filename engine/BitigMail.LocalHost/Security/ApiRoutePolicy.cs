using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Cryptography;
using System.Text;

namespace BitigMail.LocalHost.Security;
public enum ApiPolicyKind { PublicSetupStatus,SetupProof,Login,Authenticated,Scoped,Admin,DevelopmentOnly }
public static class ApiRoutePolicy
{
 public static ApiPolicyKind? Classify(string route)
 {
  if(route=="/api/setup/status")return ApiPolicyKind.PublicSetupStatus;if(route=="/api/setup/first-admin")return ApiPolicyKind.SetupProof;if(route=="/api/auth/login")return ApiPolicyKind.Login;
  if(route.StartsWith("/api/testing",StringComparison.Ordinal))return ApiPolicyKind.DevelopmentOnly;if(route is "/api/session" or "/api/session/status")return ApiPolicyKind.DevelopmentOnly;
  if(route.StartsWith("/api/admin/",StringComparison.Ordinal)||route is "/api/sdk/license/select" or "/api/archive/reindex-all")return ApiPolicyKind.Admin;
  if(route is "/api/auth/logout" or "/api/auth/me" or "/api/sdk/status" or "/api/providers/capabilities")return ApiPolicyKind.Authenticated;
  string[] scoped=["/api/catalog/","/api/accounts","/api/archive/","/api/jobs","/api/mime/","/api/source/","/api/split/","/api/picker/","/api/emlx/","/api/outlook-eml/","/api/oauth/","/api/transfer/","/api/pop/","/api/recovery/","/api/templates"];
  return scoped.Any(prefix=>route.StartsWith(prefix,StringComparison.Ordinal))?ApiPolicyKind.Scoped:null;
 }
 public static void AssertComplete(WebApplication app,bool development)
 {
  var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(x=>x.Endpoints).OfType<RouteEndpoint>().Where(x=>x.RoutePattern.RawText?.StartsWith("/api/",StringComparison.Ordinal)==true).ToArray();
  var routes=endpoints.Select(x=>x.RoutePattern.RawText!).Distinct(StringComparer.Ordinal).ToArray();var unknown=routes.Where(x=>Classify(x) is null).ToArray();if(unknown.Length>0)throw new InvalidOperationException("Sınıflandırılmamış API rotaları: "+string.Join(", ",unknown));if(!development&&routes.Any(x=>Classify(x)==ApiPolicyKind.DevelopmentOnly&&x.StartsWith("/api/testing",StringComparison.Ordinal)))throw new InvalidOperationException("Üretim uygulamasında testing rotası bulunamaz.");
  var inventory=endpoints.SelectMany(endpoint=>(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods??Array.Empty<string>()).Select(method=>method.ToUpperInvariant()+" "+endpoint.RoutePattern.RawText)).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
  string digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',inventory)))).ToLowerInvariant();
  string expected=development?"0aac77577fcbc30696253d132a2ca57e47c8caaedb9dcdcf82ab915b85d7b927":"1e8e882941ccc64f4638c9ffb6f7622f5ee1a8bd2bf3ebe2bd781abd0e9f2575";
  if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(digest),Convert.FromHexString(expected)))throw new InvalidOperationException("API method/template envanteri onaylı listeyle eşleşmiyor: "+digest+" ("+string.Join(", ",inventory)+")");
 }
}
