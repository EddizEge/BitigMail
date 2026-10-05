using System;
using System.IO;
using BitigMail.Engine.Imap;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost;
using BitigMail.LocalHost.Bridge.Transfer;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Imap;
using BitigMail.LocalHost.Imap.Transfer;
using BitigMail.LocalHost.Jobs;
using BitigMail.LocalHost.Security;
using BitigMail.LocalHost.Pop;
using BitigMail.LocalHost.Recovery;
using BitigMail.LocalHost.Planning;
using BitigMail.TestingHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Root-authorized bounded TestingHost-only diagnostic logger for UnauthorizedAccessException
AppDomain.CurrentDomain.FirstChanceException += (sender, eventArgs) =>
{
    if (eventArgs.Exception is UnauthorizedAccessException uae)
    {
        var st = new System.Diagnostics.StackTrace(1, true);
        Console.Error.WriteLine($"[DIAG_FIRST_CHANCE_UNAUTHORIZED_ACCESS] {uae.GetType().FullName}: {uae.Message}");
        Console.Error.WriteLine($"[DIAG_STACK_TRACE]\n{st}");
    }
};

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options=>options.Limits.MaxRequestBodySize=1_048_576);

// Strictly bind testing host to 127.0.0.1:6175
builder.WebHost.UseKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, 6175);
});

// Isolated runtime directory for testing: runtime/testing-engine
string repoRoot = ResolveRepoRoot();
bool secureTestMode=string.Equals(Environment.GetEnvironmentVariable("BITIGMAIL_TEST_PRODUCTION_SECURITY"),"1",StringComparison.Ordinal);
string testingRuntimeDir = Path.Combine(repoRoot, "runtime", secureTestMode?"testing-engine-secure":"testing-engine");
var (sdkStartup, recoverySdkBootstrap) = RecoverySdkBootstrap.CreateAtStartup(
    new AsposeLicenseConfigurationStore(testingRuntimeDir, new WindowsImapCredentialProtector()),
    Environment.GetEnvironmentVariable("BITIGMAIL_ASPOSE_LICENSE_PATH"));
try
{
    Directory.CreateDirectory(testingRuntimeDir);
}
catch { }

var securityConfig = new SecurityConfig { ExpectedHost = "127.0.0.1:6175", DevelopmentAnonymousSession = !secureTestMode };
builder.Services.AddSingleton(securityConfig);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TransientResourceOwnershipRegistry>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<AuthenticatedSessionRegistry>();
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddSingleton<SetupProofGate>();
builder.Services.AddSingleton(sp=>new IdentityCatalog(Path.Combine(testingRuntimeDir,"identity")));
builder.Services.AddSingleton(sp=>new AuditLogStore(Path.Combine(testingRuntimeDir,"security")));
builder.Services.AddSingleton<FileHandleRegistry>();
builder.Services.AddSingleton(sp => secureTestMode
    ? new JobManager(testingRuntimeDir, httpContextAccessor: sp.GetRequiredService<IHttpContextAccessor>(), identityCatalog: sp.GetRequiredService<IdentityCatalog>())
    : new JobManager(testingRuntimeDir));
builder.Services.AddSingleton<DamagedStoreRecoveryService>();
builder.Services.AddSingleton(recoverySdkBootstrap);
builder.Services.AddSingleton(sp=>new TransferTemplateStore(testingRuntimeDir));
builder.Services.AddSingleton<TestingFilePickerService>(sp => new TestingFilePickerService(sp.GetRequiredService<FileHandleRegistry>(), sp.GetRequiredService<JobManager>()));
builder.Services.AddSingleton<IFilePickerService>(sp => sp.GetRequiredService<TestingFilePickerService>());
builder.Services.AddTransient<OstAnalyzer>();
builder.Services.AddTransient<PstSplitter>();
builder.Services.AddTransient<MimeSourceInspector>();
builder.Services.AddTransient<MimeToPstConverter>();

// TASK-014 IMAP Account services (testing policy allows exact 127.0.0.1:5143 loopback)
builder.Services.AddSingleton(new ImapConnectionPolicy(allowTask014Loopback: true));
builder.Services.AddSingleton<IImapCredentialProtector, WindowsImapCredentialProtector>();
string testingAccountsDir = Path.Combine(testingRuntimeDir, "accounts");
builder.Services.AddSingleton<ImapAccountStore>(sp => new ImapAccountStore(
    testingAccountsDir,
    sp.GetRequiredService<IImapCredentialProtector>(),
    sp.GetRequiredService<ImapConnectionPolicy>()));
builder.Services.AddSingleton<IMicrosoftAuthProvider, MsalMicrosoftAuthProvider>();
builder.Services.AddSingleton<IGoogleAuthProvider, BitigMail.TestingHost.Security.FakeGoogleAuthProvider>();
builder.Services.AddSingleton<IImapOAuthConnectionTester, BitigMail.TestingHost.Security.FakeImapOAuthConnectionTester>();
builder.Services.AddSingleton<IImapCredentialResolver, ImapCredentialResolver>();
builder.Services.AddSingleton<BitigMail.LocalHost.OAuth.MicrosoftOAuthOperationManager>();
builder.Services.AddSingleton<BitigMail.LocalHost.OAuth.GoogleOAuthOperationManager>();
builder.Services.AddSingleton<ImapClientService>();
builder.Services.AddSingleton(sp => new TestingBridgeFaultService(testingRuntimeDir));
builder.Services.AddSingleton<MailKitTransferClientFactory>();
builder.Services.AddSingleton<IImapTransferClientFactory>(sp => new TestingTransferClientFactory(
    sp.GetRequiredService<MailKitTransferClientFactory>(),
    sp.GetRequiredService<TestingBridgeFaultService>()));
builder.Services.AddSingleton(sp => new ImapTransferJournal(testingRuntimeDir));
builder.Services.AddSingleton<ImapTransferPreviewService>();
builder.Services.AddSingleton<PopAccountStore>(sp => new PopAccountStore(Path.Combine(testingRuntimeDir, "pop-accounts"), sp.GetRequiredService<IImapCredentialProtector>(), allowTestingLoopback: true));
builder.Services.AddSingleton<IPopSnapshotClientFactory, MailKitPopSnapshotClientFactory>();
builder.Services.AddSingleton<IDiskCapacityProbe, WindowsDiskCapacityProbe>();
builder.Services.AddSingleton<PopSnapshotService>();

// TASK-017 Bridge Transfer services (isolated testing directory)
builder.Services.AddSingleton(sp => new BridgeTransferJournal(testingRuntimeDir));
builder.Services.AddSingleton<BridgeImportPreviewService>();
builder.Services.AddSingleton<BridgeExportPreviewService>();

// TASK-018 Local Archive & Search services (isolated testing directory)
builder.Services.AddSingleton<BitigMail.Engine.Archive.ArchiveStorageManager>(sp => new BitigMail.Engine.Archive.ArchiveStorageManager(testingRuntimeDir));
builder.Services.AddSingleton<BitigMail.Engine.Archive.ArchiveSearchIndex>(sp => new BitigMail.Engine.Archive.ArchiveSearchIndex(testingRuntimeDir));
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchivePlanStore>(sp => new BitigMail.LocalHost.Archive.ArchivePlanStore(testingRuntimeDir,sp.GetRequiredService<TransientResourceOwnershipRegistry>()));
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveCatalogService>();
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveSelectedJobService>();
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveGovernanceService>();
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveBackupService>();
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveRestoreService>();

var app = builder.Build();
if (secureTestMode && Environment.GetEnvironmentVariable("BITIGMAIL_TEST_SECURE_MIME_FIXTURE") is { Length: > 0 } secureMimeFixture)
{
    var picker = app.Services.GetRequiredService<TestingFilePickerService>();
    if (!picker.TrySetMimeSourceFixture(secureMimeFixture, out string fixtureError)) throw new InvalidOperationException(fixtureError);
}
if(secureTestMode&&Console.IsInputRedirected){byte[] nativeProof=new byte[32];int offset=0;var input=Console.OpenStandardInput();while(offset<32){int read=input.Read(nativeProof,offset,32-offset);if(read==0)break;offset+=read;}if(offset==32)app.Services.GetRequiredService<SetupProofGate>().InstallFromNativeChannel(nativeProof);System.Security.Cryptography.CryptographicOperations.ZeroMemory(nativeProof);}
sdkStartup.EnsureReady();
app.MapGet("/api/sdk/status", () => Results.Ok(sdkStartup.Status));

// Write testing PID file in isolated testing directory only after successful bind
string pidFile = Path.Combine(testingRuntimeDir, "testinghost.pid");
int currentPid = Environment.ProcessId;

app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        Directory.CreateDirectory(testingRuntimeDir);
        File.WriteAllText(pidFile, currentPid.ToString());
    }
    catch { }
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        if (File.Exists(pidFile))
        {
            string content = File.ReadAllText(pidFile).Trim();
            if (int.TryParse(content, out int filePid) && filePid == currentPid)
            {
                File.Delete(pidFile);
            }
        }
    }
    catch { }
});

// Uses the EXACT SAME production security middleware
app.UseMiddleware<LocalSecurityMiddleware>();

// Uses the EXACT SAME production API endpoints
app.MapIdentityEndpoints();
app.MapArchiveGovernanceEndpoints();
app.MapLocalEngineEndpoints();

// Testing specific simulation controls (no free-path picker, no weaker security)
app.MapPost("/api/testing/simulate-cancel", (TestingFilePickerService picker) =>
{
    picker.SetSimulateCancel(true);
    return Results.Ok(new { status = "cancel-simulated" });
});

app.MapPost("/api/testing/set-split-source", (TestingFilePickerService picker, SetSplitSourceRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.FixtureId))
    {
        return Results.BadRequest(new { error = "Fixture kimliği gereklidir." });
    }

    if (!picker.TrySetSplitSourceFixture(req.FixtureId, out string error))
    {
        return Results.BadRequest(new { error });
    }

    return Results.Ok(new { status = "source-set", fixtureId = req.FixtureId });
});

app.MapPost("/api/testing/set-mime-source", (TestingFilePickerService picker, SetMimeSourceRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.FixtureId))
    {
        return Results.BadRequest(new { error = "Fixture kimliği gereklidir." });
    }

    if (!picker.TrySetMimeSourceFixture(req.FixtureId, out string error))
    {
        return Results.BadRequest(new { error });
    }

    return Results.Ok(new { status = "mime-source-set", fixtureId = req.FixtureId });
});

// Test-only seed endpoint for TASK-014 live lab accounts (returns public DTOs only, never secrets)
app.MapPost("/api/testing/seed-task014-accounts", (ImapAccountStore store, ClientProjectContext? ctx) =>
{
    string companyId = string.IsNullOrWhiteSpace(ctx?.CompanyId) ? "company-task014" : ctx.CompanyId;
    string projectId = string.IsNullOrWhiteSpace(ctx?.ProjectId) ? "project-task014" : ctx.ProjectId;

    string credFile = Path.Combine(repoRoot, "lab", "task014", "local-credentials.json");
    if (!File.Exists(credFile))
    {
        return Results.BadRequest(new { error = "Lab kimlik bilgileri dosyası bulunamadı." });
    }

    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(credFile));
    var root = doc.RootElement;
    var src = root.GetProperty("source");
    var tgt = root.GetProperty("target");

    var existing = store.ListAccounts(companyId, projectId);
    var srcDto = existing.FirstOrDefault(a => a.Username == src.GetProperty("username").GetString());
    if (srcDto == null)
    {
        srcDto = store.CreateAccount(new CreateImapAccountRequest
        {
            CompanyId = companyId,
            ProjectId = projectId,
            DisplayName = "Lab Kaynak Hesabı",
            Email = src.GetProperty("email").GetString()!,
            Host = src.GetProperty("imapHost").GetString()!,
            Port = src.GetProperty("imapPort").GetInt32(),
            TlsMode = "none",
            Username = src.GetProperty("username").GetString()!,
            Password = src.GetProperty("password").GetString()!
        });
    }

    var tgtDto = existing.FirstOrDefault(a => a.Username == tgt.GetProperty("username").GetString());
    if (tgtDto == null)
    {
        tgtDto = store.CreateAccount(new CreateImapAccountRequest
        {
            CompanyId = companyId,
            ProjectId = projectId,
            DisplayName = "Lab Hedef Hesabı",
            Email = tgt.GetProperty("email").GetString()!,
            Host = tgt.GetProperty("imapHost").GetString()!,
            Port = tgt.GetProperty("imapPort").GetInt32(),
            TlsMode = "none",
            Username = tgt.GetProperty("username").GetString()!,
            Password = tgt.GetProperty("password").GetString()!
        });
    }

    return Results.Ok(new
    {
        source = srcDto,
        target = tgtDto
    });
});

app.MapPost("/api/testing/bridge-fault", (TestingBridgeFaultService faultService, BridgeFaultRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Fault) || !Enum.TryParse<BridgeTestFaultKind>(req.Fault, true, out var kind) || !Enum.IsDefined(typeof(BridgeTestFaultKind), kind))
    {
        return Results.BadRequest(new { error = $"Geçersiz veya tanımlanmamış fault türü: {req.Fault}" });
    }

    if (req.TargetOrdinal < 1 || req.TargetOrdinal > 1000)
    {
        return Results.BadRequest(new { error = $"Hedef sıra numarası 1 ile 1000 arasında olmalıdır: {req.TargetOrdinal}" });
    }

    // Reset old signal/PauseTcs before arming new fault
    faultService.Reset();

    faultService.ActiveFault = kind;
    faultService.TargetOrdinal = req.TargetOrdinal;
    return Results.Ok(new { status = "fault-set", fault = kind.ToString(), targetOrdinal = faultService.TargetOrdinal });
});

app.MapPost("/api/testing/bridge-fault/reset", (TestingBridgeFaultService faultService) =>
{
    faultService.Reset();
    return Results.Ok(new { status = "fault-reset" });
});

app.MapPost("/api/testing/seed-pop-account", (BitigMail.LocalHost.Pop.PopAccountStore store, SeedPopAccountRequest req) =>
{
    var existing = store.List(req.CompanyId, req.ProjectId).FirstOrDefault(x => x.Host == "127.0.0.1" && x.Port == req.Port && x.Username == req.Username);
    return Results.Ok(existing ?? store.Create(new BitigMail.LocalHost.Pop.CreatePopAccountRequest(req.CompanyId, req.ProjectId, "Owned Local POP", req.Username + "@example.test", "127.0.0.1", req.Port, "none", req.Username, req.Password)));
});

app.MapPost("/api/testing/stop-process", (IHostApplicationLifetime lifetime) =>
{
    lifetime.StopApplication();
    return Results.Ok(new { status = "stopping" });
});

ApiRoutePolicy.AssertComplete(app,development:true);

app.Run();

static string ResolveRepoRoot()
{
    string current = Directory.GetCurrentDirectory();
    for (int i = 0; i < 5; i++)
    {
        if (Directory.Exists(Path.Combine(current, "engine")) || Directory.Exists(Path.Combine(current, "prototype")))
        {
            return current;
        }
        string? parent = Directory.GetParent(current)?.FullName;
        if (parent == null || parent == current) break;
        current = parent;
    }
    return Directory.GetCurrentDirectory();
}

public record SetSplitSourceRequest(string? FixtureId);
public record SetMimeSourceRequest(string? FixtureId);
public record BridgeFaultRequest(string? Fault, int TargetOrdinal);
public record SeedPopAccountRequest(string CompanyId, string ProjectId, int Port, string Username, string Password);
