using BitigMail.Engine.Archive;using BitigMail.Engine.Models;using BitigMail.Engine.Planning;using BitigMail.LocalHost.Archive;using BitigMail.LocalHost.Dialogs;using BitigMail.LocalHost.Planning;using BitigMail.LocalHost.Security;using Microsoft.AspNetCore.Builder;using Microsoft.AspNetCore.Http;
namespace BitigMail.LocalHost;
public static class LocalEngineApiEndpointsStage7
{
 public static void MapStage7Endpoints(this WebApplication app)
 {
  app.MapGet("/api/templates",(HttpContext http,IdentityCatalog identities,TransferTemplateStore store)=>{var actor=Actor(http);return Results.Ok(store.List(actor?.UserId,actor is not null&&identities.IsAdmin(actor)));});
  app.MapPost("/api/templates",(HttpContext http,TransferTemplateStore store,TransferTemplate template)=>{try{return Results.Ok(store.Save(template,Actor(http)?.UserId));}catch(Exception ex)when(ex is InvalidDataException or ArgumentException or System.Text.Json.JsonException){return Results.BadRequest(new{error=ex.Message});}});
  app.MapGet("/api/templates/{id}/apply",(HttpContext http,IdentityCatalog identities,TransferTemplateStore store,string id)=>{try{var actor=Actor(http);return Results.Ok(new{template=store.Get(id,actor?.UserId,actor is not null&&identities.IsAdmin(actor)).Template,rePreviewRequired=true,liveSnapshotIncluded=false});}catch(KeyNotFoundException){return Results.NotFound(new{error="Şablon bulunamadı."});}});
  app.MapDelete("/api/templates/{id}",(HttpContext http,IdentityCatalog identities,TransferTemplateStore store,string id)=>{try{var actor=Actor(http);store.Delete(id,actor?.UserId,actor is not null&&identities.IsAdmin(actor));return Results.Ok(new{deleted=true});}catch(KeyNotFoundException){return Results.NotFound(new{error="Şablon bulunamadı."});}});
  app.MapPost("/api/archive/selected/preview",(ArchiveSelectedJobService service,ArchiveSelectedPreviewRequest req)=>{try{return Results.Ok(service.Preview(req.SearchRequest,req.SelectedMessageIds,req.FolderMappings,req.DuplicatePolicy,req.CompanyId,req.ProjectId));}catch(KeyNotFoundException){return Results.NotFound();}catch(Exception ex)when(ex is InvalidDataException or ArchiveSearchPolicyException or ArchiveIntegrityException){return Results.BadRequest(new{error=ex.Message});}});
  app.MapPost("/api/archive/selected/start",(ArchiveSelectedJobService service,FileHandleRegistry registry,ArchiveSelectedStartRequest req)=>{string? output=registry.GetOutputDirPath(req.OutputDirectoryHandle);if(output is null)return Results.BadRequest(new{error="Hedef klasör tanıtıcısı geçersiz."});try{return Results.Ok(service.Start(req.PlanId,output,req.ClientContext));}catch(Exception ex)when(ex is InvalidDataException or KeyNotFoundException){return Results.BadRequest(new{error=ex.Message});}});
 }
 private static AuthenticatedSessionPrincipal? Actor(HttpContext http)=>http.Items[typeof(AuthenticatedSessionPrincipal)] as AuthenticatedSessionPrincipal;
}
public sealed record ArchiveSelectedPreviewRequest(ArchiveSearchRequest SearchRequest,IReadOnlyList<string> SelectedMessageIds,IReadOnlyList<FolderMappingRule> FolderMappings,DuplicatePolicy DuplicatePolicy,string CompanyId,string ProjectId);
public sealed record ArchiveSelectedStartRequest(string PlanId,string OutputDirectoryHandle,ClientProjectContext ClientContext);
