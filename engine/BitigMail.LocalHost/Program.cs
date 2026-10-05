using System;
using System.IO;
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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BitigMail.Engine.Distribution;

int? desktopPort = ReadIntArg(args, "--desktop-port");
string runtimeDir = ReadStringArg(args, "--profile") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BitigMail", "production");
string? staticRoot = ReadStringArg(args, "--static-root");
DesktopInstanceLease? desktopLease = desktopPort.HasValue ? DesktopInstanceLease.Acquire(runtimeDir) : null;
if (desktopLease is not null) DesktopProfileSchema.RequireCurrent(desktopLease);
var startupProtector = new WindowsImapCredentialProtector(); var licenseStore = new AsposeLicenseConfigurationStore(runtimeDir, startupProtector); string? licenseOverride = Environment.GetEnvironmentVariable("BITIGMAIL_ASPOSE_LICENSE_PATH");
var (sdkStartup, recoverySdkBootstrap) = RecoverySdkBootstrap.CreateAtStartup(licenseStore, licenseOverride);

var builder = WebApplication.CreateBuilder(args);
if(desktopPort.HasValue)builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);

// Strictly bind to 127.0.0.1:6174
builder.WebHost.UseKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, desktopPort ?? 6174);
});

var securityConfig = desktopPort.HasValue ? SecurityConfig.ForDesktop(desktopPort.Value) : new SecurityConfig { ExpectedHost = "127.0.0.1:6174", DevelopmentAnonymousSession = false };
builder.Services.AddSingleton(securityConfig);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TransientResourceOwnershipRegistry>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<AuthenticatedSessionRegistry>();
builder.Services.AddSingleton<LoginRateLimiter>();
builder.Services.AddSingleton<SetupProofGate>();
builder.Services.AddSingleton(sp => new IdentityCatalog(Path.Combine(runtimeDir, "identity")));
builder.Services.AddSingleton(sp => new AuditLogStore(Path.Combine(runtimeDir, "security")));
builder.Services.AddSingleton<FileHandleRegistry>();
builder.Services.AddSingleton<DesktopOperationGate>();
builder.Services.AddSingleton<IFilePickerService, WinFormsFilePickerService>();
builder.Services.AddSingleton(sp => new JobManager(runtimeDir, new WindowsDiskCapacityProbe(), sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), sp.GetRequiredService<IdentityCatalog>()));
builder.Services.AddSingleton<DamagedStoreRecoveryService>();
builder.Services.AddSingleton(recoverySdkBootstrap);
builder.Services.AddSingleton(sp => new TransferTemplateStore(runtimeDir));
builder.Services.AddSingleton(sdkStartup);
builder.Services.AddTransient<OstAnalyzer>();
builder.Services.AddTransient<PstSplitter>();
builder.Services.AddTransient<MimeSourceInspector>();
builder.Services.AddTransient<MimeToPstConverter>();

// TASK-014 IMAP Account services (strict production TLS policy)
builder.Services.AddSingleton(new ImapConnectionPolicy(allowTask014Loopback: false));
builder.Services.AddSingleton<IImapCredentialProtector>(startupProtector);
builder.Services.AddSingleton(licenseStore);
string accountsDir = Path.Combine(runtimeDir, "accounts");
builder.Services.AddSingleton<ImapAccountStore>(sp => new ImapAccountStore(
    accountsDir,
    sp.GetRequiredService<IImapCredentialProtector>(),
    sp.GetRequiredService<ImapConnectionPolicy>()));
builder.Services.AddSingleton<IMicrosoftAuthProvider, MsalMicrosoftAuthProvider>();
builder.Services.AddSingleton<IGoogleAuthProvider, GoogleAuthProvider>();
builder.Services.AddSingleton<IImapOAuthConnectionTester, RealImapOAuthConnectionTester>();
builder.Services.AddSingleton<IImapCredentialResolver, ImapCredentialResolver>();
builder.Services.AddSingleton<BitigMail.LocalHost.OAuth.MicrosoftOAuthOperationManager>();
builder.Services.AddSingleton<BitigMail.LocalHost.OAuth.GoogleOAuthOperationManager>();
builder.Services.AddSingleton<ImapClientService>();
builder.Services.AddSingleton<IImapTransferClientFactory, MailKitTransferClientFactory>();
builder.Services.AddSingleton<ImapTransferJournal>(sp => new ImapTransferJournal(runtimeDir));
builder.Services.AddSingleton<ImapTransferPreviewService>();
builder.Services.AddSingleton<PopAccountStore>(sp => new PopAccountStore(Path.Combine(runtimeDir, "pop-accounts"), sp.GetRequiredService<IImapCredentialProtector>()));
builder.Services.AddSingleton<IPopSnapshotClientFactory, MailKitPopSnapshotClientFactory>();
builder.Services.AddSingleton<PopSnapshotService>();

// TASK-017 Bridge Transfer services
builder.Services.AddSingleton<BridgeTransferJournal>(sp => new BridgeTransferJournal(runtimeDir));
builder.Services.AddSingleton<IDiskCapacityProbe, WindowsDiskCapacityProbe>();
builder.Services.AddSingleton<BridgeImportPreviewService>();
builder.Services.AddSingleton<BridgeExportPreviewService>();

// TASK-018 Local Archive & Search services
builder.Services.AddSingleton<BitigMail.Engine.Archive.ArchiveStorageManager>(sp => new BitigMail.Engine.Archive.ArchiveStorageManager(runtimeDir));
builder.Services.AddSingleton<BitigMail.Engine.Archive.ArchiveSearchIndex>(sp => new BitigMail.Engine.Archive.ArchiveSearchIndex(runtimeDir));
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchivePlanStore>(sp => new BitigMail.LocalHost.Archive.ArchivePlanStore(runtimeDir, sp.GetRequiredService<TransientResourceOwnershipRegistry>()));
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveCatalogService>();
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveSelectedJobService>();
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveGovernanceService>();
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveBackupService>();
builder.Services.AddSingleton<BitigMail.LocalHost.Archive.ArchiveRestoreService>();

var app = builder.Build();
app.UseMiddleware<LocalSecurityMiddleware>();
app.UseMiddleware<DesktopOperationGateMiddleware>();
if (desktopPort.HasValue)
{
    if (string.IsNullOrWhiteSpace(staticRoot) || !Directory.Exists(staticRoot)) throw new InvalidOperationException("Paketlenmiş kullanıcı arayüzü bulunamadı.");
    var provider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(staticRoot));
    app.Use(async (context, next) => { context.Response.OnStarting(() => { context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; connect-src 'self'; object-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'self'"; context.Response.Headers.XContentTypeOptions = "nosniff"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; return Task.CompletedTask; }); await next(); });
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider }); app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, ServeUnknownFileTypes = false });
}
string? desktopReadyLine = null;
if (Console.IsInputRedirected)
{
    byte[] nativeProof = new byte[32]; int offset = 0; Stream input = Console.OpenStandardInput(); while (offset < nativeProof.Length) { int read = input.Read(nativeProof, offset, nativeProof.Length - offset); if (read == 0) break; offset += read; }
    if (offset == nativeProof.Length){app.Services.GetRequiredService<SetupProofGate>().InstallFromNativeChannel(nativeProof);if(desktopPort.HasValue)desktopReadyLine=DesktopReadyProof.CreateLine(nativeProof,desktopPort.Value);} System.Security.Cryptography.CryptographicOperations.ZeroMemory(nativeProof); _ = Task.Run(async () =>
    {
        using var reader = new StreamReader(input);
        while (true)
        {
            string? command = await DesktopProtocolReader.ReadLineAsync(reader, maximumCharacters: 128);
            if (desktopPort.HasValue && command?.StartsWith(DesktopSetupProofRefresh.Prefix, StringComparison.Ordinal) == true)
            {
                string reply = DesktopSetupProofRefresh.Handle(command, desktopPort.Value,
                    app.Services.GetRequiredService<SetupProofGate>(), app.Services.GetRequiredService<IdentityCatalog>(),
                    app.Services.GetRequiredService<DesktopOperationGate>());
                Console.Out.WriteLine(reply);
                continue;
            }
            if (command is not (null or "EXIT")) continue;
            var jobs = app.Services.GetRequiredService<JobManager>();
            var gate = app.Services.GetRequiredService<DesktopOperationGate>();
            gate.BeginDrain();
            try { jobs.BeginDesktopShutdown(); }
            catch { Console.Out.WriteLine("BITIGMAIL_SHUTDOWN_BLOCKED PERSISTENCE"); if (command is null) return; else continue; }
            while (true)
            {
                var status = jobs.GetDesktopShutdownStatus();
                if (status.PersistenceFailure) { Console.Out.WriteLine("BITIGMAIL_SHUTDOWN_BLOCKED PERSISTENCE"); break; }
                if (status.UnresolvedWorker) { Console.Out.WriteLine("BITIGMAIL_SHUTDOWN_BLOCKED UNRESOLVED_WORKER"); break; }
                if (status.CanExit && gate.IsDrained) { app.Lifetime.StopApplication(); return; }
                await Task.Delay(250);
            }
            if (command is null) return;
        }
    });
}
sdkStartup.EnsureReady();
app.MapGet("/api/sdk/status", () => Results.Ok(sdkStartup.Status));
app.MapPost("/api/sdk/license/select", async (IFilePickerService picker, AsposeLicenseConfigurationStore store) => { var picked = await picker.PickAsposeLicenseAsync(); if (picked.Cancelled) return Results.Ok(new { cancelled = true, restartRequired = false }); if (!string.IsNullOrEmpty(picked.Error) || string.IsNullOrEmpty(picked.DisplayPath)) return Results.BadRequest(new { error = picked.Error ?? "Lisans dosyası seçilemedi." }); store.SaveFromFile(picked.DisplayPath); return Results.Ok(new { cancelled = false, restartRequired = true }); });

// Write PID file for clean process tracking only after successful bind
string pidFile = Path.Combine(runtimeDir, "localhost.pid");
int currentPid = Environment.ProcessId;

app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        Directory.CreateDirectory(runtimeDir);
        File.WriteAllText(pidFile, currentPid.ToString());
    }
    catch { }
    if (desktopReadyLine is not null) Console.Out.WriteLine(desktopReadyLine);
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
app.Lifetime.ApplicationStopped.Register(()=>desktopLease?.Dispose());

// Map production endpoints
app.MapIdentityEndpoints();
app.MapArchiveGovernanceEndpoints();
app.MapLocalEngineEndpoints();
if (desktopPort.HasValue) app.MapFallback(async context => { if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; return; } context.Response.ContentType = "text/html; charset=utf-8"; await context.Response.SendFileAsync(Path.Combine(Path.GetFullPath(staticRoot!), "index.html")); });
ApiRoutePolicy.AssertComplete(app, development: false);

app.Run();

static string? ReadStringArg(string[] values, string name) { int i = Array.IndexOf(values, name); return i >= 0 && i + 1 < values.Length ? values[i + 1] : null; }
static int? ReadIntArg(string[] values, string name) { string? value = ReadStringArg(values, name); return value is null ? null : int.TryParse(value, out int parsed) ? parsed : throw new ArgumentException($"Geçersiz {name}."); }
