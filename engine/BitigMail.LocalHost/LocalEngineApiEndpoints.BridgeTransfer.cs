using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BitigMail.Engine.Bridge;
using BitigMail.Engine.Models;
using BitigMail.LocalHost.Bridge.Transfer;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost;

public static class LocalEngineApiEndpointsBridgeTransfer
{
    public const string DefaultImportPreviewError = "Köprü içe aktarım önizlemesi oluşturulurken beklenmeyen bir hata oluştu.";
    public const string DefaultImportPreviewGetError = "Köprü içe aktarım önizlemesi alınamadı.";
    public const string DefaultImportStartError = "Köprü içe aktarım işlemi başlatılırken beklenmeyen bir hata oluştu.";
    public const string DefaultImportResumeError = "Köprü içe aktarım işlemi devam ettirilirken beklenmeyen bir hata oluştu.";

    public const string DefaultExportPreviewError = "Köprü dışa aktarım önizlemesi oluşturulurken beklenmeyen bir hata oluştu.";
    public const string DefaultExportPreviewGetError = "Köprü dışa aktarım önizlemesi alınamadı.";
    public const string DefaultExportStartError = "Köprü dışa aktarım işlemi başlatılırken beklenmeyen bir hata oluştu.";
    public const string DefaultExportResumeError = "Köprü dışa aktarım işlemi devam ettirilirken beklenmeyen bir hata oluştu.";

    private static readonly string[] AllowedSafePrefixes = new[]
    {
        "[ÖN KONTROL ENGELİ]",
        "[GÜVENLİK ENGELİ]",
        "En az bir kaynak dosya seçilmelidir.",
        "Hedef IMAP hesabı zorunludur.",
        "Geçersiz hedef IMAP hesabı.",
        "Seçilen klasör içinde geçerli .eml dosyası bulunamadı.",
        "Seçilen arşiv dosyası (.mbox/.mbx) bulunamadı.",
        "Tarih filtresi başlangıcı bitişinden büyük olamaz.",
        "Kaynak IMAP hesabı zorunludur.",
        "Geçersiz kaynak IMAP hesabı.",
        "En az bir klasör seçilmelidir.",
        "Hedef klasör seçilmelidir.",
        "Müşteri kimliği (companyId) ve proje kimliği (projectId) zorunludur.",
        "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur.",
        "previewId parametresi zorunludur.",
        "idempotencyKey parametresi zorunludur.",
        "Müşteri veya proje kapsamı plan ile uyuşmuyor.",
        "Aktarım planında engel bulunmaktadır.",
        "Aktarım planı bulunamadı",
        "İş kaydı bulunamadı",
        "Önizleme bulunamadı",
        "Halen devam eden bir aktarım veya dönüştürme işi bulunmaktadır",
        "Idempotency key '",
        "İş henüz tamamlanmamış",
        "İş tipi köprü aktarımı değil",
        "Köprü aktarım detayları veya plan bulunamadı",
        "Reauthorization required",
        "Yeniden yetkilendirme gerekiyor",
        "Seçilen '",
        "Hedef IMAP hesabı bulunamadı",
        "Kaynak IMAP hesabı bulunamadı",
        "Kaynak tanıtıcısı (sourceHandle) zorunludur.",
        "Kaynak tanıtıcısı bulunamadı veya süresi dolmuş.",
        "Geçersiz kaynak tanıtıcısı.",
        "Kaynak tanıtıcısı zorunludur."
    };

    public static bool IsSafeControlledDomainMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        // 1. Must match at least one explicitly enumerated allowed safe prefix
        bool matchesAllowed = false;
        foreach (var prefix in AllowedSafePrefixes)
        {
            if (message.StartsWith(prefix, StringComparison.Ordinal))
            {
                matchesAllowed = true;
                break;
            }
        }

        if (!matchesAllowed)
            return false;

        // 2. Reject absolute paths (drive letters, UNC paths, root directories, Unix paths)
        if (message.Contains(":\\", StringComparison.Ordinal) ||
            message.Contains(":/", StringComparison.Ordinal) ||
            message.Contains("\\\\", StringComparison.Ordinal) ||
            message.Contains("/home/", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("/Users/", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("/etc/", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("/var/", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("/tmp/", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("/private/", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("\\Windows\\", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("\\ProgramData\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 3. Reject secrets, credentials, tokens, sentinels, passwords
        if (message.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("sentinel", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("privatekey", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 4. Reject stack traces and raw exception dumps
        if (message.Contains("   at ", StringComparison.Ordinal) ||
            message.Contains("\tat ", StringComparison.Ordinal) ||
            message.Contains(".cs:line", StringComparison.OrdinalIgnoreCase) ||
            message.Contains(".cs:", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Exception:", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("StackTrace", StringComparison.OrdinalIgnoreCase) ||
            message.Contains('\r') ||
            message.Contains('\n'))
        {
            return false;
        }

        return true;
    }

    public static bool IsSafeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
            return false;

        foreach (char c in id)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))
                return false;
        }

        if (id.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("sentinel", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static void MapBridgeTransferEndpoints(this WebApplication app)
    {
        // 0. Vendor-Free Source Descriptor Endpoint (Based solely on FileHandleRegistry MimeSourceManifest)
        app.MapPost("/api/transfer/bridge/source/describe", (
            FileHandleRegistry handleRegistry,
            BridgeSourceDescribeRequest req) =>
            HandleBridgeSourceDescribe(handleRegistry, req));

        app.MapGet("/api/transfer/bridge/source/descriptor/{sourceHandle}", (
            FileHandleRegistry handleRegistry,
            string sourceHandle) =>
            HandleBridgeSourceDescribe(handleRegistry, new BridgeSourceDescribeRequest { SourceHandle = sourceHandle }));

        // -------------------------------------------------------------
        // Direction 1: File -> IMAP Import Endpoints
        // -------------------------------------------------------------

        // 1a. Create Immutable Bridge Import Preview
        app.MapPost("/api/transfer/bridge/import/preview", (
            BridgeImportPreviewService previewService,
            BridgeImportPreviewRequest req,
            CancellationToken ct) =>
            HandleBridgeImportPreviewAsync(previewService, req, ct));

        // 1b. Get Immutable Import Preview by PreviewId
        app.MapGet("/api/transfer/bridge/import/preview/{previewId}", (
            BridgeImportPreviewService previewService,
            string previewId,
            string? companyId,
            string? projectId) =>
            HandleGetBridgeImportPreview(previewService, previewId, companyId, projectId));

        // 1c. Start Bridge Import Job
        app.MapPost("/api/transfer/bridge/import/start", (
            JobManager jobManager,
            BridgeTransferJournal transferJournal,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            IImapCredentialResolver credentialResolver,
            BridgeStartRequest req) =>
            HandleStartBridgeImport(jobManager, transferJournal, handleRegistry, accountStore, clientFactory, credentialResolver, req));

        // 1d. Resume Interrupted Bridge Import Job
        app.MapPost("/api/transfer/bridge/import/resume/{jobId}", (
            JobManager jobManager,
            BridgeTransferJournal transferJournal,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            IImapCredentialResolver credentialResolver,
            string jobId,
            BridgeResumeRequest? req,
            string? companyId,
            string? projectId) =>
            HandleResumeBridgeImport(jobManager, transferJournal, handleRegistry, accountStore, clientFactory, credentialResolver, jobId, req, companyId, projectId));

        app.MapPost("/api/transfer/bridge/import/resume", (
            JobManager jobManager,
            BridgeTransferJournal transferJournal,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            IImapCredentialResolver credentialResolver,
            BridgeResumeRequest req,
            string? companyId,
            string? projectId) =>
            HandleResumeBridgeImport(jobManager, transferJournal, handleRegistry, accountStore, clientFactory, credentialResolver, req?.JobId ?? string.Empty, req, companyId, projectId));

        // -------------------------------------------------------------
        // Direction 2: IMAP -> File Export Endpoints
        // -------------------------------------------------------------

        // 2a. Create Immutable Bridge Export Preview
        app.MapPost("/api/transfer/bridge/export/preview", (
            BridgeExportPreviewService previewService,
            BridgeExportPreviewRequest req,
            CancellationToken ct) =>
            HandleBridgeExportPreviewAsync(previewService, req, ct));

        // 2b. Get Immutable Export Preview by PreviewId
        app.MapGet("/api/transfer/bridge/export/preview/{previewId}", (
            BridgeExportPreviewService previewService,
            string previewId,
            string? companyId,
            string? projectId) =>
            HandleGetBridgeExportPreview(previewService, previewId, companyId, projectId));

        // 2c. Start Bridge Export Job
        app.MapPost("/api/transfer/bridge/export/start", (
            JobManager jobManager,
            BridgeTransferJournal transferJournal,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            IImapCredentialResolver credentialResolver,
            BridgeStartRequest req) =>
            HandleStartBridgeExport(jobManager, transferJournal, handleRegistry, accountStore, clientFactory, credentialResolver, req));

        // 2d. Resume Interrupted Bridge Export Job
        app.MapPost("/api/transfer/bridge/export/resume/{jobId}", (
            JobManager jobManager,
            BridgeTransferJournal transferJournal,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            IImapCredentialResolver credentialResolver,
            string jobId,
            BridgeResumeRequest? req,
            string? companyId,
            string? projectId) =>
            HandleResumeBridgeExport(jobManager, transferJournal, handleRegistry, accountStore, clientFactory, credentialResolver, jobId, req, companyId, projectId));

        app.MapPost("/api/transfer/bridge/export/resume", (
            JobManager jobManager,
            BridgeTransferJournal transferJournal,
            FileHandleRegistry handleRegistry,
            ImapAccountStore accountStore,
            IImapTransferClientFactory clientFactory,
            IImapCredentialResolver credentialResolver,
            BridgeResumeRequest req,
            string? companyId,
            string? projectId) =>
            HandleResumeBridgeExport(jobManager, transferJournal, handleRegistry, accountStore, clientFactory, credentialResolver, req?.JobId ?? string.Empty, req, companyId, projectId));
    }

    // -----------------------------------------------------------------
    // Endpoint Handlers with Strict Sanitization
    // -----------------------------------------------------------------

    public static async Task<IResult> HandleBridgeImportPreviewAsync(
        BridgeImportPreviewService previewService,
        BridgeImportPreviewRequest req,
        CancellationToken ct)
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
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportPreviewError;
            return Results.BadRequest(new { error = safeMsg });
        }
        catch (ReauthorizationRequiredException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : "Yeniden yetkilendirme gerekiyor.";
            string safeAccountId = IsSafeId(ex.AccountId) ? ex.AccountId : string.Empty;
            return Results.Conflict(new { code = ReauthorizationRequiredException.StableCode, error = safeMsg, accountId = safeAccountId });
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportPreviewError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultImportPreviewError });
        }
    }

    public static IResult HandleGetBridgeImportPreview(
        BridgeImportPreviewService previewService,
        string previewId,
        string? companyId,
        string? projectId)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId))
        {
            return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) zorunludur." });
        }

        if (string.IsNullOrWhiteSpace(previewId) || !IsSafeId(previewId))
        {
            return Results.NotFound(new { error = "Önizleme bulunamadı." });
        }

        try
        {
            var preview = previewService.GetPreviewResponse(previewId, companyId, projectId);
            return preview != null ? Results.Ok(preview) : Results.NotFound(new { error = $"Önizleme bulunamadı: {previewId}" });
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportPreviewGetError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultImportPreviewGetError });
        }
    }

    public static IResult HandleStartBridgeImport(
        JobManager jobManager,
        BridgeTransferJournal transferJournal,
        FileHandleRegistry handleRegistry,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        IImapCredentialResolver credentialResolver,
        BridgeStartRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.PreviewId))
        {
            return Results.BadRequest(new { error = "previewId parametresi zorunludur." });
        }

        if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "idempotencyKey parametresi zorunludur." });
        }

        BridgeImportPlan? plan = null;
        try
        {
            plan = transferJournal.GetImportPlan(req.PreviewId);
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportStartError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (ArgumentException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportStartError;
            return Results.BadRequest(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultImportStartError });
        }

        if (plan == null)
        {
            string safeId = IsSafeId(req.PreviewId) ? $" {req.PreviewId}" : string.Empty;
            return Results.NotFound(new { error = $"Aktarım planı bulunamadı veya süresi dolmuş:{safeId}".TrimEnd() });
        }

        if (!plan.CanTransfer || plan.Items.Count == 0)
        {
            string safeBlocker = IsSafeControlledDomainMessage(plan.BlockerReason)
                ? plan.BlockerReason!
                : "Aktarım planında engel bulunmaktadır. Aktarım başlatılamaz.";
            return Results.Conflict(new { error = safeBlocker });
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
            var record = jobManager.StartBridgeImportJob(
                plan,
                req.IdempotencyKey,
                clientContext,
                handleRegistry,
                accountStore,
                clientFactory,
                transferJournal,
                credentialResolver,
                req.EnqueueIfBusy);

            return Results.Ok(record);
        }
        catch (ArgumentException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportStartError;
            return Results.BadRequest(new { error = safeMsg });
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportStartError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultImportStartError });
        }
    }

    public static IResult HandleResumeBridgeImport(
        JobManager jobManager,
        BridgeTransferJournal transferJournal,
        FileHandleRegistry handleRegistry,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        IImapCredentialResolver credentialResolver,
        string jobId,
        BridgeResumeRequest? req,
        string? companyId,
        string? projectId)
    {
        string effectiveCompany = !string.IsNullOrWhiteSpace(companyId) ? companyId : req?.CompanyId ?? string.Empty;
        string effectiveProject = !string.IsNullOrWhiteSpace(projectId) ? projectId : req?.ProjectId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(effectiveCompany) || string.IsNullOrWhiteSpace(effectiveProject))
        {
            return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
        }

        if (string.IsNullOrWhiteSpace(jobId) || !IsSafeId(jobId))
        {
            return Results.NotFound(new { error = "İş kaydı bulunamadı." });
        }

        var clientContext = new ClientProjectContext
        {
            CompanyId = effectiveCompany,
            ProjectId = effectiveProject
        };

        try
        {
            var record = jobManager.ResumeBridgeImportJob(
                jobId,
                clientContext,
                handleRegistry,
                accountStore,
                clientFactory,
                transferJournal,
                credentialResolver,
                req?.EnqueueIfBusy ?? false);

            return Results.Ok(record);
        }
        catch (KeyNotFoundException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : "İş kaydı bulunamadı.";
            return Results.NotFound(new { error = safeMsg });
        }
        catch (ArgumentException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportResumeError;
            return Results.BadRequest(new { error = safeMsg });
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultImportResumeError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultImportResumeError });
        }
    }

    public static async Task<IResult> HandleBridgeExportPreviewAsync(
        BridgeExportPreviewService previewService,
        BridgeExportPreviewRequest req,
        CancellationToken ct)
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
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportPreviewError;
            return Results.BadRequest(new { error = safeMsg });
        }
        catch (ReauthorizationRequiredException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : "Yeniden yetkilendirme gerekiyor.";
            string safeAccountId = IsSafeId(ex.AccountId) ? ex.AccountId : string.Empty;
            return Results.Conflict(new { code = ReauthorizationRequiredException.StableCode, error = safeMsg, accountId = safeAccountId });
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportPreviewError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultExportPreviewError });
        }
    }

    public static IResult HandleGetBridgeExportPreview(
        BridgeExportPreviewService previewService,
        string previewId,
        string? companyId,
        string? projectId)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(projectId))
        {
            return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) zorunludur." });
        }

        if (string.IsNullOrWhiteSpace(previewId) || !IsSafeId(previewId))
        {
            return Results.NotFound(new { error = "Önizleme bulunamadı." });
        }

        try
        {
            var preview = previewService.GetPreviewResponse(previewId, companyId, projectId);
            return preview != null ? Results.Ok(preview) : Results.NotFound(new { error = $"Önizleme bulunamadı: {previewId}" });
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportPreviewGetError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultExportPreviewGetError });
        }
    }

    public static IResult HandleStartBridgeExport(
        JobManager jobManager,
        BridgeTransferJournal transferJournal,
        FileHandleRegistry handleRegistry,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        IImapCredentialResolver credentialResolver,
        BridgeStartRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.PreviewId))
        {
            return Results.BadRequest(new { error = "previewId parametresi zorunludur." });
        }

        if (string.IsNullOrWhiteSpace(req.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "idempotencyKey parametresi zorunludur." });
        }

        BridgeExportPlan? plan = null;
        try
        {
            plan = transferJournal.GetExportPlan(req.PreviewId);
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportStartError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (ArgumentException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportStartError;
            return Results.BadRequest(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultExportStartError });
        }

        if (plan == null)
        {
            string safeId = IsSafeId(req.PreviewId) ? $" {req.PreviewId}" : string.Empty;
            return Results.NotFound(new { error = $"Aktarım planı bulunamadı veya süresi dolmuş:{safeId}".TrimEnd() });
        }

        if (!plan.CanTransfer || plan.Items.Count == 0)
        {
            string safeBlocker = IsSafeControlledDomainMessage(plan.BlockerReason)
                ? plan.BlockerReason!
                : "Aktarım planında engel bulunmaktadır. Aktarım başlatılamaz.";
            return Results.Conflict(new { error = safeBlocker });
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
            var record = jobManager.StartBridgeExportJob(
                plan,
                req.IdempotencyKey,
                clientContext,
                handleRegistry,
                accountStore,
                clientFactory,
                transferJournal,
                credentialResolver,
                req.EnqueueIfBusy);

            return Results.Ok(record);
        }
        catch (ArgumentException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportStartError;
            return Results.BadRequest(new { error = safeMsg });
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportStartError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultExportStartError });
        }
    }

    public static IResult HandleResumeBridgeExport(
        JobManager jobManager,
        BridgeTransferJournal transferJournal,
        FileHandleRegistry handleRegistry,
        ImapAccountStore accountStore,
        IImapTransferClientFactory clientFactory,
        IImapCredentialResolver credentialResolver,
        string jobId,
        BridgeResumeRequest? req,
        string? companyId,
        string? projectId)
    {
        string effectiveCompany = !string.IsNullOrWhiteSpace(companyId) ? companyId : req?.CompanyId ?? string.Empty;
        string effectiveProject = !string.IsNullOrWhiteSpace(projectId) ? projectId : req?.ProjectId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(effectiveCompany) || string.IsNullOrWhiteSpace(effectiveProject))
        {
            return Results.BadRequest(new { error = "Müşteri kimliği (companyId) ve proje kimliği (projectId) parametreleri zorunludur." });
        }

        if (string.IsNullOrWhiteSpace(jobId) || !IsSafeId(jobId))
        {
            return Results.NotFound(new { error = "İş kaydı bulunamadı." });
        }

        var clientContext = new ClientProjectContext
        {
            CompanyId = effectiveCompany,
            ProjectId = effectiveProject
        };

        try
        {
            var record = jobManager.ResumeBridgeExportJob(
                jobId,
                clientContext,
                handleRegistry,
                accountStore,
                clientFactory,
                transferJournal,
                credentialResolver,
                req?.EnqueueIfBusy ?? false);

            return Results.Ok(record);
        }
        catch (KeyNotFoundException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : "İş kaydı bulunamadı.";
            return Results.NotFound(new { error = safeMsg });
        }
        catch (ArgumentException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportResumeError;
            return Results.BadRequest(new { error = safeMsg });
        }
        catch (InvalidOperationException ex)
        {
            string safeMsg = IsSafeControlledDomainMessage(ex.Message) ? ex.Message : DefaultExportResumeError;
            return Results.Conflict(new { error = safeMsg });
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = DefaultExportResumeError });
        }
    }

    public static IResult HandleBridgeSourceDescribe(
        FileHandleRegistry handleRegistry,
        BridgeSourceDescribeRequest? req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.SourceHandle))
        {
            return Results.BadRequest(new { error = "Kaynak tanıtıcısı (sourceHandle) zorunludur." });
        }

        if (!IsSafeId(req.SourceHandle) || !req.SourceHandle.StartsWith("msrc_"))
        {
            return Results.BadRequest(new { error = "Geçersiz kaynak tanıtıcısı." });
        }

        var entry = handleRegistry.GetMimeSourceEntry(req.SourceHandle);
        if (entry == null || entry.Manifest == null)
        {
            return Results.NotFound(new { error = "Kaynak tanıtıcısı bulunamadı veya süresi dolmuş." });
        }

        var manifest = entry.Manifest;
        if (string.Equals(manifest.SourceKind, "mbox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(manifest.Dialect, "mboxrd", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(manifest.RootPath))
            {
                try
                {
                    BridgeMboxrdValidator.ValidateStrictMboxrd(manifest.RootPath);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = $"Geçersiz veya bozuk mboxrd arşivi: {ex.Message}" });
                }
            }
        }

        var folderGroups = manifest.Entries
            .GroupBy(e => string.IsNullOrWhiteSpace(e.MappedFolder) ? "INBOX" : e.MappedFolder, StringComparer.Ordinal)
            .Select(g => new BridgeSourceFolderDescriptor
            {
                FolderName = g.Key,
                ItemCount = g.Count(),
                TotalSizeBytes = g.Sum(e => e.SizeBytes)
            })
            .OrderBy(f => f.FolderName, StringComparer.Ordinal)
            .ToList();

        var descriptor = new BridgeSourceDescriptorResponse
        {
            SourceHandle = entry.Handle,
            SourceKind = manifest.SourceKind,
            Dialect = manifest.Dialect,
            DisplayPath = entry.DisplayPath,
            TotalFiles = manifest.TotalFiles > 0 ? manifest.TotalFiles : manifest.Entries.Count,
            TotalItems = manifest.Entries.Count,
            TotalSizeBytes = manifest.Entries.Sum(e => e.SizeBytes),
            Folders = folderGroups,
            IgnoredNonEmlFilesCount = manifest.IgnoredNonEmlFilesCount
        };

        return Results.Ok(descriptor);
    }
}
