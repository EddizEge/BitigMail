using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Recovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost;
public static class LocalEngineApiEndpointsRecovery
{
    public static void MapRecoveryEndpoints(this WebApplication app)
    {
        app.MapPost("/api/recovery/preview",(FileHandleRegistry registry,RecoveryPreviewRequest req)=>{var source=registry.GetSourceEntry(req.SourceHandle);var output=registry.GetOutputDirEntry(req.OutputDirectoryHandle);if(source is null||output is null||!File.Exists(source.FullPath)||!Directory.Exists(output.FullPath))return Results.BadRequest(new{error="Kaynak veya hedef tanıtıcısı geçersiz."});string hash=BitigMail.Engine.Recovery.DamagedStoreResultValidator.HashFile(source.FullPath);return Results.Ok(new{sourceHandle=req.SourceHandle,outputDirectoryHandle=req.OutputDirectoryHandle,sourceFileName=source.FileName,sourceSha256=hash,mode="read_only_recover_to_new_output",originalTotal=(int?)null,qualification="REPRESENTATIVE_CORPUS_PENDING",canStart=true,warnings=new[]{"Kaynak hiçbir zaman yerinde değiştirilmez.","Toplam öğe sayısı hasarlı bölgelerde bilinmeyebilir.","Aspose deneme sürümü kısıtları geçerlidir."}});});
        app.MapPost("/api/recovery/start",(FileHandleRegistry registry,DamagedStoreRecoveryService service,RecoveryStartRequest req)=>{var source=registry.GetSourceEntry(req.SourceHandle);var output=registry.GetOutputDirEntry(req.OutputDirectoryHandle);if(source is null||output is null)return Results.BadRequest(new{error="Kaynak veya hedef tanıtıcısı geçersiz."});try{return Results.Ok(service.Start(source.FullPath,output.FullPath,req.ExpectedSourceSha256,req.ClientContext??new(),TimeSpan.FromMinutes(Math.Clamp(req.TimeoutMinutes??30,1,1440))));}catch(InvalidOperationException ex){return Results.BadRequest(new{error=ex.Message});}});
        app.MapGet("/api/recovery/jobs/{jobId}",(DamagedStoreRecoveryService service,string jobId)=>service.Get(jobId) is { } job?Results.Ok(job):Results.NotFound(new{error="Kurtarma işi bulunamadı."}));
        app.MapPost("/api/recovery/jobs/{jobId}/cancel",(DamagedStoreRecoveryService service,string jobId)=>service.Cancel(jobId)?Results.Ok(new{cancelled=true}):Results.BadRequest(new{error="İş iptal edilemedi."}));
        app.MapGet("/api/recovery/jobs/{jobId}/report", (DamagedStoreRecoveryService service, string jobId) =>
        {
            try { var bytes = service.GetReportBytes(jobId); return bytes is null ? Results.NotFound() : Results.File(bytes, "application/json", "bitigmail-kurtarma-raporu.json"); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                { return Results.BadRequest(new { error = "Kurtarma raporu doğrulanamadı veya erişilemiyor." }); }
        });
    }
}
public sealed record RecoveryPreviewRequest(string SourceHandle,string OutputDirectoryHandle);
public sealed record RecoveryStartRequest(string SourceHandle,string OutputDirectoryHandle,string ExpectedSourceSha256,BitigMail.Engine.Models.ClientProjectContext? ClientContext,int? TimeoutMinutes);
