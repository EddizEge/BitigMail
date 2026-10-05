using BitigMail.Engine.Models;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Pop;

namespace BitigMail.LocalHost;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PopPreviewApiRequest(string AccountId, string CompanyId, string ProjectId);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PopStartApiRequest(string PlanId, string CompanyId, string ProjectId, string OutputDirHandle, string IdempotencyKey, bool EnqueueIfBusy = true);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PopResumeApiRequest(string CompanyId, string ProjectId, bool EnqueueIfBusy = true);
public static class LocalEngineApiEndpointsPop
{
    public static void MapPopEndpoints(this WebApplication app)
    {
        app.MapPost("/api/pop/accounts", (PopAccountStore store, CreatePopAccountRequest request) =>
        { try { return Results.Ok(store.Create(request)); } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); } });
        app.MapGet("/api/pop/accounts", (PopAccountStore store, string companyId, string projectId) =>
        { try { return Results.Ok(store.List(companyId, projectId)); } catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); } });
        app.MapPost("/api/pop/preview", async (PopSnapshotService service, PopPreviewApiRequest request, CancellationToken ct) =>
        { try { return Results.Ok(await service.PreviewAsync(request.AccountId, request.CompanyId, request.ProjectId, ct)); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or KeyNotFoundException) { return Results.BadRequest(new { error = ex.Message }); } });
        app.MapPost("/api/pop/start", (PopSnapshotService service, PopAccountStore accounts, FileHandleRegistry handles, JobManager jobs, PopStartApiRequest request) =>
        {
            try
            {
                var plan = service.GetPlan(request.PlanId, request.CompanyId, request.ProjectId); _ = accounts.Resolve(plan.AccountId, request.CompanyId, request.ProjectId);
                string output = handles.GetOutputDirPath(request.OutputDirHandle) ?? throw new ArgumentException("Geçersiz çıktı klasörü tanıtıcısı.");
                var owner = new ClientProjectContext { CompanyId = request.CompanyId, ProjectId = request.ProjectId };
                return Results.Ok(jobs.StartPopSnapshotJob(plan, output, request.IdempotencyKey, owner, service, request.EnqueueIfBusy));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or KeyNotFoundException) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPost("/api/pop/resume/{jobId}", (PopSnapshotService service, JobManager jobs, string jobId, PopResumeApiRequest request) =>
        {
            try
            {
                var old = jobs.GetJob(jobId) ?? throw new KeyNotFoundException("POP işi bulunamadı.");
                if (old.JobKind != "pop-snapshot" || old.Status is not ("failed" or "interrupted")) throw new InvalidOperationException("Yalnız başarısız veya kesintili POP işi sürdürülebilir.");
                if (old.ClientContext.CompanyId != request.CompanyId || old.ClientContext.ProjectId != request.ProjectId) throw new InvalidOperationException("POP işi müşteri/proje kapsamıyla uyuşmuyor.");
                var plan = service.GetPlan(old.PlanId ?? throw new InvalidDataException("POP işinde kalıcı plan kimliği yok."), request.CompanyId, request.ProjectId);
                return Results.Ok(jobs.StartPopSnapshotJob(plan, old.OutputDirectoryPath ?? throw new InvalidDataException("POP işinde çıktı yolu yok."), "resume-" + Guid.NewGuid().ToString("N"), old.ClientContext, service, request.EnqueueIfBusy));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or KeyNotFoundException) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }
}
