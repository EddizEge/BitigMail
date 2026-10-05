using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Imap.OAuth;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost;

public static class LocalEngineApiEndpointsOAuth
{
    public static void MapMicrosoftOAuthEndpoints(this WebApplication app)
    {
        // POST /api/oauth/microsoft/start
        app.MapPost("/api/oauth/microsoft/start", (
            MicrosoftOAuthOperationManager manager,
            StartMicrosoftOAuthRequest req) =>
        {
            if (req == null)
            {
                return Results.BadRequest(new { error = "İstek gövdesi boş olamaz." });
            }

            try
            {
                var response = manager.StartOperation(req);
                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (AccountVersionConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Oturum açma işlemi başlatılamadı." });
            }
        });

        // GET /api/oauth/microsoft/operations/{id}
        app.MapGet("/api/oauth/microsoft/operations/{id}", (
            MicrosoftOAuthOperationManager manager,
            string id,
            string? companyId,
            string? projectId) =>
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.BadRequest(new { error = "İşlem kimliği (id) zorunludur." });
            }

            if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
            }

            try
            {
                var status = manager.GetOperation(id, companyId, projectId);
                if (status == null)
                {
                    return Results.NotFound(new { error = "İşlem bulunamadı." });
                }

                return Results.Ok(status);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "İşlem durumu sorgulanamadı." });
            }
        });

        // POST /api/oauth/microsoft/operations/{id}/cancel
        app.MapPost("/api/oauth/microsoft/operations/{id}/cancel", (
            MicrosoftOAuthOperationManager manager,
            string id,
            string? companyId,
            string? projectId,
            CancelMicrosoftOAuthRequest? req) =>
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Results.BadRequest(new { error = "İşlem kimliği (id) zorunludur." });
            }

            string effectiveCompany = !string.IsNullOrWhiteSpace(companyId) ? companyId : req?.CompanyId ?? string.Empty;
            string effectiveProject = !string.IsNullOrWhiteSpace(projectId) ? projectId : req?.ProjectId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(effectiveCompany) || string.IsNullOrWhiteSpace(effectiveProject))
            {
                return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
            }

            try
            {
                var result = manager.CancelAndGetResult(id, effectiveCompany, effectiveProject);
                if (result is null)
                {
                    return Results.NotFound(new { error = "İşlem bulunamadı." });
                }

                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "İşlem iptal edilemedi." });
            }
        });
    }
}
