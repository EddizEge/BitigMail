using System;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap.Transfer;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost;

public static class LocalEngineApiEndpointsImapTransfer
{
    public static void MapImapTransferEndpoints(this WebApplication app)
    {
        // 20a. Create Immutable IMAP Transfer Preview
        app.MapPost("/api/transfer/imap/preview", async (
            ImapTransferPreviewService previewService,
            ImapTransferPreviewRequest req,
            CancellationToken ct) =>
        {
            if (req == null)
            {
                return Results.BadRequest(new { error = "Geçersiz önizleme isteği." });
            }

            try
            {
                var response = await previewService.CreatePreviewAsync(req, ct);
                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ReauthorizationRequiredException ex)
            {
                return Results.Conflict(new { code = ReauthorizationRequiredException.StableCode, error = ex.Message, accountId = ex.AccountId });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Önizleme oluşturulamadı." });
            }
        });

        // 20b. Get Immutable Preview by PreviewId
        app.MapGet("/api/transfer/imap/preview/{previewId}", (
            ImapTransferPreviewService previewService,
            string previewId,
            string? companyId,
            string? projectId) =>
        {
            if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) zorunludur." });
            }

            try
            {
                var preview = previewService.GetPreviewResponse(previewId, companyId, projectId);
                return preview != null ? Results.Ok(preview) : Results.NotFound(new { error = $"Önizleme bulunamadı: {previewId}" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Önizleme alınamadı." });
            }
        });

        // 20c. Start IMAP Transfer Job from Preview
        app.MapPost("/api/transfer/imap/start", (
            JobManager jobManager,
            ImapTransferJournal transferJournal,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            IImapCredentialResolver credentialResolver,
            ImapTransferStartRequest req) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.PreviewId))
            {
                return Results.BadRequest(new { error = "previewId parametresi zorunludur." });
            }

            if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
            {
                return Results.BadRequest(new { error = "idempotencyKey parametresi zorunludur." });
            }

            var plan = transferJournal.GetPlan(req.PreviewId);
            if (plan == null)
            {
                return Results.NotFound(new { error = $"Aktarım planı bulunamadı veya süresi dolmuş: {req.PreviewId}" });
            }

            // Server-side start rejection for blocked plan
            if (!plan.CanTransfer || plan.Items.Count == 0)
            {
                return Results.Conflict(new { error = plan.BlockerReason ?? "Aktarım planında engel bulunmaktadır. Aktarım başlatılamaz." });
            }

            string effectiveCompany = !string.IsNullOrWhiteSpace(req.CompanyId) ? req.CompanyId : plan.CompanyId;
            string effectiveProject = !string.IsNullOrWhiteSpace(req.ProjectId) ? req.ProjectId : plan.ProjectId;

            if (!string.Equals(plan.CompanyId, effectiveCompany, StringComparison.Ordinal) ||
                !string.Equals(plan.ProjectId, effectiveProject, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { error = "Müşteri veya proje kapsamı plan ile uyuşmuyor." });
            }

            var clientContext = new ClientProjectContext
            {
                CompanyId = effectiveCompany,
                ProjectId = effectiveProject,
                CompanyName = plan.CompanyName ?? effectiveCompany,
                ProjectName = plan.ProjectName ?? effectiveProject
            };

            try
            {
                var record = jobManager.StartImapTransferJob(
                    plan,
                    req.IdempotencyKey,
                    clientContext,
                    accountStore,
                    clientFactory,
                    transferJournal,
                    credentialResolver,
                    req.EnqueueIfBusy);

                return Results.Ok(record);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Aktarım işlemi başlatılamadı." });
            }
        });

        // 20d. Resume Interrupted IMAP Transfer Job
        app.MapPost("/api/transfer/imap/resume/{jobId}", (
            JobManager jobManager,
            ImapTransferJournal transferJournal,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            IImapCredentialResolver credentialResolver,
            string jobId,
            ImapTransferResumeRequest? req,
            string? companyId,
            string? projectId) =>
        {
            string effectiveCompany = !string.IsNullOrWhiteSpace(companyId) ? companyId : req?.CompanyId ?? string.Empty;
            string effectiveProject = !string.IsNullOrWhiteSpace(projectId) ? projectId : req?.ProjectId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(effectiveCompany) || string.IsNullOrWhiteSpace(effectiveProject))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
            }

            var clientContext = new ClientProjectContext
            {
                CompanyId = effectiveCompany,
                ProjectId = effectiveProject
            };

            try
            {
                var record = jobManager.ResumeImapTransferJob(
                    jobId,
                    clientContext,
                    accountStore,
                    clientFactory,
                    transferJournal,
                    credentialResolver,
                    req?.EnqueueIfBusy ?? false);

                return Results.Ok(record);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}


