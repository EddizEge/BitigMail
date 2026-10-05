using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Archive;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Archive;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost;

public static class LocalEngineApiEndpointsArchive
{
    public static void MapArchiveEndpoints(this WebApplication app)
    {
        // 0. Archive Source Native Picker (Bounded to 64 MiB, no arbitrary paths)
        app.MapPost("/api/picker/archive-source", async (IFilePickerService pickerService, MimePickerRequest? req) =>
        {
            var result = await pickerService.PickArchiveSourceAsync(req?.Mode ?? "eml-files");
            return Results.Ok(result);
        });

        app.MapPost("/api/archive/picker/source", async (IFilePickerService pickerService, MimePickerRequest? req) =>
        {
            var result = await pickerService.PickArchiveSourceAsync(req?.Mode ?? "eml-files");
            return Results.Ok(result);
        });

        // 1. Ingest Preview
        app.MapPost("/api/archive/ingest/preview", (
            ArchiveIngestPreviewRequest req,
            ArchiveCatalogService catalogService) =>
        {
            if (req == null)
            {
                return Results.BadRequest(new { error = "Geçersiz istek gövdesi." });
            }

            try
            {
                var preview = catalogService.CreatePreview(req);
                return Results.Ok(preview);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.Problem("Arşiv önizlemesi oluşturulurken beklenmeyen bir hata oluştu.", statusCode: 500);
            }
        });

        // 2. Get Ingest Preview
        app.MapGet("/api/archive/ingest/preview/{previewId}", (
            string previewId,
            ArchiveCatalogService catalogService) =>
        {
            if (string.IsNullOrWhiteSpace(previewId))
            {
                return Results.BadRequest(new { error = "previewId parametresi zorunludur." });
            }

            try
            {
                var preview = catalogService.GetPreview(previewId);
                return Results.Ok(preview);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.Problem("Arşiv önizleme bilgisi alınırken beklenmeyen bir hata oluştu.", statusCode: 500);
            }
        });

        // 3. Start Archive Ingest
        app.MapPost("/api/archive/ingest/start", (
            ArchiveIngestStartRequest req,
            ArchivePlanStore planStore,
            ArchiveStorageManager storageManager,
            ArchiveSearchIndex searchIndex,
            FileHandleRegistry handleRegistry,
            JobManager jobManager) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.PreviewId))
            {
                return Results.BadRequest(new { error = "previewId parametresi zorunludur." });
            }

            var plan = planStore.GetPlan(req.PreviewId);
            if (plan == null)
            {
                return Results.NotFound(new { error = "Arşivleme planı bulunamadı veya süresi dolmuş." });
            }

            var clientContext = new ClientProjectContext
            {
                CompanyId = plan.CompanyId,
                CompanyName = plan.CompanyName ?? plan.CompanyId,
                ProjectId = plan.ProjectId,
                ProjectName = plan.ProjectName ?? plan.ProjectId
            };

            try
            {
                var record = jobManager.StartArchiveIngestJob(
                    plan,
                    req.IdempotencyKey,
                    clientContext,
                    storageManager,
                    searchIndex,
                    planStore,
                    handleRegistry,
                    runInBackground: true,
                    enqueueIfBusy: req.EnqueueIfBusy);

                return Results.Ok(record);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Idempotency key"))
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Halen devam eden"))
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.Problem("Arşivleme işlemi başlatılırken beklenmeyen bir hata oluştu.", statusCode: 500);
            }
        });

        // 4. Resume Archive Ingest (Path or Body)
        app.MapPost("/api/archive/ingest/resume/{jobId}", (
            string jobId,
            ArchivePlanStore planStore,
            ArchiveStorageManager storageManager,
            ArchiveSearchIndex searchIndex,
            FileHandleRegistry handleRegistry,
            JobManager jobManager) =>
        {
            return ResumeArchiveInternal(jobId, planStore, storageManager, searchIndex, handleRegistry, jobManager, enqueueIfBusy: false);
        });

        app.MapPost("/api/archive/ingest/resume", (
            ArchiveIngestResumeRequest? req,
            ArchivePlanStore planStore,
            ArchiveStorageManager storageManager,
            ArchiveSearchIndex searchIndex,
            FileHandleRegistry handleRegistry,
            JobManager jobManager) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.JobId))
            {
                return Results.BadRequest(new { error = "jobId parametresi zorunludur." });
            }

            return ResumeArchiveInternal(req.JobId, planStore, storageManager, searchIndex, handleRegistry, jobManager, req.EnqueueIfBusy);
        });

        // 5. Catalog
        app.MapGet("/api/archive/catalog", (HttpContext http,IdentityCatalog identities,ArchiveCatalogService catalogService) =>
        {
            try
            {
                var catalog = catalogService.GetCatalog();
                if(http.Items[typeof(AuthenticatedSessionPrincipal)] is AuthenticatedSessionPrincipal actor)catalog=catalog.Where(x=>identities.CanAccess(actor,x.CompanyId,x.ProjectId)).ToList();
                return Results.Ok(catalog);
            }
            catch (Exception)
            {
                return Results.Problem("Arşiv kataloğu listelenirken beklenmeyen bir hata oluştu.", statusCode: 500);
            }
        });

        // 6. Manifest
        app.MapGet("/api/archive/{archiveId}/manifest", (
            HttpContext http,IdentityCatalog identities,
            string archiveId,
            ArchiveStorageManager storageManager) =>
        {
            if (string.IsNullOrWhiteSpace(archiveId))
            {
                return Results.BadRequest(new { error = "archiveId parametresi zorunludur." });
            }

            var manifest = storageManager.GetArchiveManifest(archiveId);
            if (manifest == null)
            {
                return Results.NotFound(new { error = "Arşiv manifestosu bulunamadı." });
            }
            if(http.Items[typeof(AuthenticatedSessionPrincipal)] is AuthenticatedSessionPrincipal actor&&!identities.CanAccess(actor,manifest.CompanyId,manifest.ProjectId))return Results.NotFound(new{error="Arşiv manifestosu bulunamadı."});

            return Results.Ok(manifest);
        });

        // 7. Search
        app.MapPost("/api/archive/search", (
            ArchiveSearchRequest req,
            ArchiveCatalogService catalogService,
            IdentityCatalog identities,HttpContext context) => AuthorizedArchiveScopes(context,identities,req.SelectedScopes)?HandleSearch(req, catalogService, context.RequestAborted):Results.NotFound());

        // 8. Message Preview
        app.MapPost("/api/archive/message/preview", (
            ArchiveMessagePreviewRequest req,
            ArchiveCatalogService catalogService,IdentityCatalog identities,HttpContext context) => AuthorizedArchiveScopes(context,identities,req.SearchRequest.SelectedScopes)?HandleMessagePreview(req, catalogService):Results.NotFound());

        // 9. Reindex Archive
        app.MapPost("/api/archive/{archiveId}/reindex", (
            HttpContext http,IdentityCatalog identities,
            string archiveId,
            ArchiveReindexRequest? req,
            ArchivePlanStore planStore,
            ArchiveStorageManager storageManager,
            ArchiveSearchIndex searchIndex,
            FileHandleRegistry handleRegistry,
            JobManager jobManager) =>
        {
            if (string.IsNullOrWhiteSpace(archiveId))
            {
                return Results.BadRequest(new { error = "archiveId parametresi zorunludur." });
            }

            var manifest = storageManager.GetArchiveManifest(archiveId);
            if (manifest == null)
            {
                return Results.NotFound(new { error = "Yeniden indekslenecek arşiv bulunamadı." });
            }
            if(http.Items[typeof(AuthenticatedSessionPrincipal)] is AuthenticatedSessionPrincipal actor&&!identities.CanAccess(actor,manifest.CompanyId,manifest.ProjectId))return Results.NotFound(new{error="Yeniden indekslenecek arşiv bulunamadı."});

            var clientContext = new ClientProjectContext
            {
                CompanyId = manifest.CompanyId,
                CompanyName = manifest.CompanyName ?? manifest.CompanyId,
                ProjectId = manifest.ProjectId,
                ProjectName = manifest.ProjectName ?? manifest.ProjectId
            };

            try
            {
                var record = jobManager.StartArchiveReindexJob(
                    archiveId,
                    req?.ArchiveId,
                    clientContext,
                    storageManager,
                    searchIndex,
                    planStore,
                    handleRegistry,
                    runInBackground: true,
                    enqueueIfBusy: req?.EnqueueIfBusy ?? false);

                return Results.Ok(record);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Halen devam eden"))
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.Problem("Yeniden indeksleme işlemi başlatılırken beklenmeyen bir hata oluştu.", statusCode: 500);
            }
        });

        // 10. Reindex All Archives
        app.MapPost("/api/archive/reindex-all", async (
            ArchiveStorageManager storageManager,
            ArchiveSearchIndex searchIndex) =>
        {
            try
            {
                await searchIndex.RebuildDatabaseAsync(storageManager, CancellationToken.None);
                return Results.Ok(new { success = true, message = "Tüm arşivler başarıyla yeniden indekslendi." });
            }
            catch (Exception)
            {
                return Results.Problem("Tüm arşivler yeniden indekslenirken beklenmeyen bir hata oluştu.", statusCode: 500);
            }
        });
    }

    private static bool AuthorizedArchiveScopes(HttpContext context,IdentityCatalog catalog,IEnumerable<ArchiveScopeTriple> scopes)=>ArchiveScopeAuthorization.Allows(context,catalog,scopes);

    private static IResult ResumeArchiveInternal(
        string jobId,
        ArchivePlanStore planStore,
        ArchiveStorageManager storageManager,
        ArchiveSearchIndex searchIndex,
        FileHandleRegistry handleRegistry,
        JobManager jobManager,
        bool enqueueIfBusy)
    {
        var existingJob = jobManager.GetJob(jobId);
        if (existingJob == null)
        {
            return Results.NotFound(new { error = $"İş bulunamadı: {jobId}" });
        }

        try
        {
            var record = jobManager.ResumeArchiveIngestJob(
                jobId,
                existingJob.ClientContext,
                storageManager,
                searchIndex,
                planStore,
                handleRegistry,
                runInBackground: true,
                enqueueIfBusy: enqueueIfBusy);

            return Results.Ok(record);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Halen devam eden"))
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return Results.Problem("Arşivleme işlemi devam ettirilirken beklenmeyen bir hata oluştu.", statusCode: 500);
        }
    }

    public static IResult HandleSearch(
        ArchiveSearchRequest req,
        ArchiveCatalogService catalogService,
        CancellationToken cancellationToken = default)
    {
        if (req == null)
        {
            return Results.BadRequest(new { error = "Geçersiz arama isteği." });
        }

        try
        {
            // Closed mapping: validate field before conversion (omitted -> All, explicit null/unknown -> HTTP 400)
            req.ResolveField();

            var response = catalogService.Search(req, cancellationToken);
            return Results.Ok(response);
        }
        catch (ArchiveSearchPolicyException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidDataException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499);
        }
        catch (Exception)
        {
            return Results.Problem("Arşiv araması yürütülürken beklenmeyen bir hata oluştu.", statusCode: 500);
        }
    }

    public static IResult HandleMessagePreview(
        ArchiveMessagePreviewRequest req,
        ArchiveCatalogService catalogService)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.MessageId) || req.SearchRequest == null)
        {
            return Results.BadRequest(new { error = "messageId ve searchRequest parametreleri zorunludur." });
        }

        try
        {
            req.SearchRequest.ResolveField();
            var preview = catalogService.GetMessagePreview(req);
            return Results.Ok(preview);
        }
        catch (ArchiveIntegrityException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (ArchiveSearchPolicyException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound(new { error = "Arşiv ileti dosyası bulunamadı." });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return Results.Problem("İleti önizlemesi hazırlanırken beklenmeyen bir hata oluştu.", statusCode: 500);
        }
    }
}
