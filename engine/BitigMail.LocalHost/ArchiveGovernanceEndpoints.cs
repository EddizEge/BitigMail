using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost;
public static class ArchiveGovernanceEndpoints
{
 public static void MapArchiveGovernanceEndpoints(this WebApplication app)
 {
  app.MapPost("/api/admin/archive/retention-preview",(HttpContext http,IdentityCatalog identities,ArchiveGovernanceService service,RetentionPreviewRequest req)=>{var actor=IdentityApiEndpoints.Actor(http);if(!identities.IsAdmin(actor)||!identities.CanAccess(actor,req.CompanyId,req.ProjectId))return Results.NotFound();try{return Results.Ok(service.PreviewRetention(req.CompanyId,req.ProjectId,req.OlderThanDays,req.LargerThanBytes));}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}});
  app.MapPost("/api/admin/archive/backup",async (HttpContext http,IdentityCatalog identities,Dialogs.FileHandleRegistry handles,ArchiveBackupService service,ArchiveBackupRequest req,CancellationToken ct)=>{var actor=IdentityApiEndpoints.Actor(http);bool Authorized()=>identities.IsAdmin(actor)&&req.Scopes.All(x=>identities.CanAccess(actor,x.CompanyId,x.ProjectId));if(!Authorized())return Results.NotFound();string? output=handles.GetOutputDirPath(req.OutputDirectoryHandle);if(output is null)return Results.NotFound();try{return Results.Ok(await service.CreateAsync(req.Scopes,output,ct,Authorized));}catch(UnauthorizedAccessException){return Results.NotFound();}catch(KeyNotFoundException){return Results.NotFound();}catch(Exception ex)when(ex is ArgumentException or InvalidDataException or IOException){return Results.BadRequest(new{error=ex.Message});}});
  app.MapPost("/api/admin/archive/restore",async (HttpContext http,IdentityCatalog identities,Dialogs.FileHandleRegistry handles,ArchiveRestoreService service,ArchiveRestoreRequest req,CancellationToken ct)=>{var actor=IdentityApiEndpoints.Actor(http);if(!identities.IsAdmin(actor)||!identities.CanAccess(actor,req.CompanyId,req.ProjectId))return Results.NotFound();string? package=handles.GetSourcePath(req.SourceHandle);if(package is null)return Results.NotFound();try{return Results.Ok(await service.RestoreAsync(package,req.CompanyId,req.ProjectId,actor,ct));}catch(Exception ex)when(ex is ArgumentException or InvalidDataException or IOException or KeyNotFoundException){return Results.BadRequest(new{error=ex.Message});}});
  app.MapPost("/api/admin/archive/restore/{operationId}/repair",async (HttpContext http,IdentityCatalog identities,ArchiveRestoreService service,string operationId,CancellationToken ct)=>{var actor=IdentityApiEndpoints.Actor(http);if(!identities.IsAdmin(actor))return Results.NotFound();try{return Results.Ok(await service.RepairAsync(operationId,actor,ct));}catch(UnauthorizedAccessException){return Results.NotFound();}catch(Exception ex)when(ex is ArgumentException or InvalidDataException or IOException or KeyNotFoundException){return Results.BadRequest(new{error=ex.Message});}});
  app.MapGet("/api/admin/jobs/{jobId}/delivery-report",(HttpContext http,IdentityCatalog identities,JobManager jobs,ArchiveGovernanceService service,string jobId)=>{var actor=IdentityApiEndpoints.Actor(http);var job=jobs.GetJob(jobId);if(!identities.IsAdmin(actor)||job is null||!jobs.CanAccessJob(actor,job))return Results.NotFound();return Results.Ok(service.GetDeliveryReport(jobId));});
 }
}
public sealed record RetentionPreviewRequest(string CompanyId,string? ProjectId,int OlderThanDays,long? LargerThanBytes);
public sealed record ArchiveBackupRequest(IReadOnlyList<BitigMail.Engine.Archive.ArchiveScopeSelection> Scopes,string OutputDirectoryHandle);
public sealed record ArchiveRestoreRequest(string SourceHandle,string CompanyId,string ProjectId);
