using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using BitigMail.Engine.Distribution;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BitigMail.Desktop;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try { Application.Run(new MainForm(args)); }
        catch (Exception ex)
        {
            int i=Array.IndexOf(args,"--capture");
            if(i>=0&&i+1<args.Length){File.WriteAllText(Path.GetFullPath(args[i+1])+".error.txt",ex.ToString());Environment.ExitCode=1;}
            else if(ex is WebView2RuntimeNotFoundException)MessageBox.Show("Microsoft Edge WebView2 Runtime bulunamadı. WebView2 Runtime kurup yeniden deneyin.","BitigMail",MessageBoxButtons.OK,MessageBoxIcon.Error);
            else MessageBox.Show("BitigMail başlatılamadı: "+ex.Message,"BitigMail",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}

internal sealed class MainForm:Form
{
    readonly WebView2 _web=new(){Dock=DockStyle.Fill};
    readonly NotifyIcon _tray;
    readonly int _port;
    readonly string _origin,_profile;
    readonly string? _capturePath,_activationSignal,_installRoot;
    readonly DesktopNavigationPolicy _policy;
    readonly SemaphoreSlim _commandWriteLock=new(1,1),_refreshLock=new(1,1);
    TaskCompletionSource<string> _shutdownBlocked=NewShutdownSignal();
    TaskCompletionSource<bool>? _refreshCompletion;
    Process? _engine;
    FileStream? _installLease;
    string? _proof,_proofScriptId,_refreshExpected;
    bool _realExit,_captureStarted,_syntheticSmokeAllowed,_smokeReloaded;
    int _exitInProgress;

    public MainForm(string[] args)
    {
        Text="BitigMail";using(var logo=new Bitmap(Path.Combine(AppContext.BaseDirectory,"bitigmail-mark-v1.png")))Icon=Icon.FromHandle(logo.GetHicon());Width=1280;Height=820;MinimumSize=new(900,620);Controls.Add(_web);
        _port=FreePort();_origin=$"http://127.0.0.1:{_port}/";_policy=new(new Uri(_origin));
        _profile=Arg(args,"--profile")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BitigMail","desktop");
        _capturePath=Arg(args,"--capture");_activationSignal=Arg(args,"--activation-signal");_installRoot=Arg(args,"--install-root")??InferInstallRoot();string? explicitProfile=Arg(args,"--profile");_syntheticSmokeAllowed=_capturePath is not null&&explicitProfile is not null&&Path.GetFullPath(explicitProfile).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase);
        if(_installRoot is not null)_installLease=AcquireInstallActivationLease(_installRoot);
        _tray=new(){Icon=Icon,Text="BitigMail",Visible=false,ContextMenuStrip=new()};
        _tray.ContextMenuStrip.Items.Add("BitigMail'i aç",null,(_,_)=>ShowFromTray());
        _tray.ContextMenuStrip.Items.Add("Hakkında",null,(_,_)=>MessageBox.Show("BitigMail 0.9 iç kullanım derlemesi\nİmzasız paket · WebView2 gerekir · SDK/deneme sınırları uygulamada gösterilir.","BitigMail Hakkında"));
        _tray.ContextMenuStrip.Items.Add("Çıkış",null,async(_,_)=>await ExitApplicationAsync());_tray.DoubleClick+=(_,_)=>ShowFromTray();FormClosing+=OnClosing;Shown+=async(_,_)=>await StartAsync();
    }

    async Task StartAsync()
    {
        try
        {
            string engine=Path.Combine(AppContext.BaseDirectory,"engine","BitigMail.LocalHost.exe"),ui=Path.Combine(AppContext.BaseDirectory,"ui");
            if(!File.Exists(engine)||!File.Exists(Path.Combine(ui,"index.html")))throw new InvalidOperationException("Paket dosyaları eksik.");
            byte[] proof=RandomNumberGenerator.GetBytes(32);_proof=Convert.ToHexString(proof).ToLowerInvariant();string expected=DesktopReadyProof.CreateLine(proof,_port);
            var psi=new ProcessStartInfo(engine,$"--desktop-port {_port} --profile \"{_profile}\" --static-root \"{ui}\""){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
            _engine=Process.Start(psi)??throw new InvalidOperationException("Yerel motor başlatılamadı.");await _engine.StandardInput.BaseStream.WriteAsync(proof);await _engine.StandardInput.BaseStream.FlushAsync();CryptographicOperations.ZeroMemory(proof);
            await WaitOwnedReadyAsync(expected);_ = PumpOutputAsync();await WaitHttpReadyAsync();
            string webData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BitigMail","WebView2");await _web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,webData));
            await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"if(window===top)window.__BITIGMAIL_ENGINE_URL__={JsonSerializer.Serialize(_origin.TrimEnd('/'))};");
            _web.CoreWebView2.NavigationStarting+=(s,e)=>{if(!_policy.IsTrustedDocument(e.Uri))e.Cancel=true;};
            _web.CoreWebView2.NewWindowRequested+=(s,e)=>{e.Handled=true;if(_policy.MayOpenInSystemBrowser(e.Uri,e.IsUserInitiated))Process.Start(new ProcessStartInfo(e.Uri){UseShellExecute=true});};
            _web.CoreWebView2.WebMessageReceived+=OnWebMessageReceived;_web.CoreWebView2.NavigationCompleted+=OnNavigationCompleted;_web.Source=new(_origin);
        }
        catch(Exception ex)
        {
            Exception? cleanupError=null;try{await RequestOwnedExitAsync();}catch(Exception stopEx){cleanupError=stopEx;}
            string message="BitigMail başlatılamadı: "+ex.Message+"\nYeniden deneyin veya diğer BitigMail penceresini kapatın."+(cleanupError is null?string.Empty:"\nYerel motor güvenle kapatılamadı: "+cleanupError.Message);
            if(_capturePath is not null)await File.WriteAllTextAsync(_capturePath+".error.txt",message+Environment.NewLine+ex+(cleanupError is null?string.Empty:Environment.NewLine+cleanupError));else MessageBox.Show(message,"BitigMail",MessageBoxButtons.OK,MessageBoxIcon.Error);
            if(cleanupError is null||_engine is not{HasExited:false}){_realExit=true;Close();}else{Enabled=true;_tray.Visible=true;}
        }
    }

    async void OnWebMessageReceived(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
    {
        Trace("web-message source="+e.Source);
        if(!_policy.IsTrustedDocument(_web.Source?.AbsoluteUri)||!_policy.IsTrustedDocument(e.Source)){Trace("web-message rejected-source");return;}
        string message;try{message=e.TryGetWebMessageAsString();}catch{return;}
        if(message!="BITIGMAIL_REFRESH_SETUP")return;Trace("refresh requested");
        try{await RefreshSetupProofAsync();}catch{}
    }

    async Task RefreshSetupProofAsync()
    {
        if(_realExit||Volatile.Read(ref _exitInProgress)!=0){PostSetupProofError("BitigMail kapanıyor; kurulum doğrulaması yenilenemedi.");return;}
        if(!await _refreshLock.WaitAsync(0)){PostSetupProofError("Güvenli kurulum doğrulaması zaten hazırlanıyor.");return;}
        try
        {
            if(_engine is not{HasExited:false}||!_policy.IsTrustedDocument(_web.Source?.AbsoluteUri))throw new InvalidOperationException("Güvenli yerel motor kullanılamıyor. BitigMail uygulamasını yeniden açın.");
            byte[] proof=RandomNumberGenerator.GetBytes(32);string hex=Convert.ToHexString(proof).ToLowerInvariant();string expected=DesktopReadyProof.CreateLine(proof,_port);CryptographicOperations.ZeroMemory(proof);
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);_refreshExpected=expected;_refreshCompletion=completion;
            try
            {
                await _commandWriteLock.WaitAsync();try{await _engine.StandardInput.WriteLineAsync("SETUP_PROOF "+hex);await _engine.StandardInput.FlushAsync();}finally{_commandWriteLock.Release();}
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));await completion.Task.WaitAsync(timeout.Token);Trace("refresh acknowledged");
                if(!_policy.IsTrustedDocument(_web.Source?.AbsoluteUri))throw new InvalidOperationException("Kurulum sayfası değişti. Yeniden deneyin.");
                _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{type="bitigmail-setup-proof",proof=hex}));
            }
            finally{_refreshCompletion=null;_refreshExpected=null;}
        }
        catch(Exception ex){PostSetupProofError(ex is OperationCanceledException?"Güvenli kurulum kanıtı zamanında yenilenemedi. BitigMail uygulamasını yeniden açın.":ex.Message);}
        finally{_refreshLock.Release();}
    }

    void PostSetupProofError(string error){if(!_realExit&&_policy.IsTrustedDocument(_web.Source?.AbsoluteUri))_web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{type="bitigmail-setup-proof-error",error}));}

    async void OnNavigationCompleted(object? sender,CoreWebView2NavigationCompletedEventArgs e)
    {
        if(!e.IsSuccess||!_policy.IsTrustedDocument(_web.Source?.AbsoluteUri))return;
        if(_proof is not null){string proof=Interlocked.Exchange(ref _proof,null)!;_proofScriptId=await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"if(window===top&&location.origin==={JsonSerializer.Serialize(_origin.TrimEnd('/'))}&&(location.pathname==='/'||location.pathname==='/index.html')&&!location.search)window.__BITIGMAIL_SETUP_PROOF__={JsonSerializer.Serialize(proof)};");_web.Reload();return;}
        if(_proofScriptId is not null){_web.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_proofScriptId);_proofScriptId=null;}
        if(_syntheticSmokeAllowed&&Environment.GetEnvironmentVariable("BITIGMAIL_SYNTHETIC_SETUP_RELOAD")=="1"&&!_smokeReloaded){_smokeReloaded=true;_web.Reload();return;}
        if(_capturePath is not null&&!_captureStarted){_captureStarted=true;for(int i=0;i<30;i++){if(await _web.CoreWebView2.ExecuteScriptAsync("document.readyState==='complete'&&document.styleSheets.length>0&&getComputedStyle(document.body).fontFamily.includes('Segoe UI')")=="true")break;await Task.Delay(100);}try{if(_syntheticSmokeAllowed&&Environment.GetEnvironmentVariable("BITIGMAIL_SYNTHETIC_SETUP_SMOKE")=="1")await RunSyntheticSetupSmokeAsync();}catch(Exception ex){await File.WriteAllTextAsync(_capturePath+".error.txt",ex.ToString());await ExitApplicationAsync();return;}await Task.Delay(500);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_capturePath))!);string diagnostics=await _web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({ready:document.readyState,identityGate:!!document.querySelector('[data-testid=identity-gate]'),adminBar:!!document.querySelector('[data-testid=identity-admin-bar]'),sheets:[...document.styleSheets].map(s=>({href:s.href,rules:(()=>{try{return s.cssRules.length}catch(e){return -1}})()})),links:[...document.querySelectorAll('link')].map(l=>({href:l.href,rel:l.rel})),bodyFont:getComputedStyle(document.body).fontFamily,bodyMargin:getComputedStyle(document.body).margin,html:document.documentElement.outerHTML.slice(0,500)})");await File.WriteAllTextAsync(_capturePath+".json",diagnostics);await using var output=new FileStream(_capturePath,FileMode.Create,FileAccess.Write,FileShare.None);await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output);await ExitApplicationAsync();}
    }

    async Task RunSyntheticSetupSmokeAsync()
    {
        for(int i=0;i<50;i++){if(await _web.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('[data-testid=identity-gate] input').length===2&&!![...document.querySelectorAll('[data-testid=identity-gate] button')].find(x=>x.textContent.includes('Yönetici oluştur')&&!x.disabled)")=="true")break;if(i==49)throw new TimeoutException("Sentetik ilk yönetici formu hazırlanmadı.");await Task.Delay(100);}
        const string fill="(()=>{const set=(el,v)=>{const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;setter.call(el,v);el.dispatchEvent(new Event('input',{bubbles:true}));};const inputs=document.querySelectorAll('[data-testid=identity-gate] input');if(inputs.length!==2)return false;set(inputs[0],'stage9-admin');set(inputs[1],'Stage9!Synthetic123');return true;})()";
        if(await _web.CoreWebView2.ExecuteScriptAsync(fill)!="true")throw new InvalidOperationException("Sentetik ilk yönetici formu doldurulamadı.");Trace("smoke filled");await Task.Delay(100);if(await _web.CoreWebView2.ExecuteScriptAsync("(()=>{const b=[...document.querySelectorAll('[data-testid=identity-gate] button')].find(x=>x.textContent.includes('Yönetici oluştur'));if(!b)return false;b.click();return true;})()")!="true")throw new InvalidOperationException("Sentetik ilk yönetici düğmesi çalıştırılamadı.");Trace("smoke clicked");
        for(int i=0;i<100;i++){if(await _web.CoreWebView2.ExecuteScriptAsync("!document.querySelector('[data-testid=identity-gate]')&&!!document.querySelector('[data-testid=header-account-control]')")=="true")return;string error=await _web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[role=alert]')?.textContent||''");if(error!="\"\"")throw new InvalidOperationException("Sentetik ilk yönetici kurulumu başarısız: "+error);await Task.Delay(100);}
        throw new TimeoutException("Sentetik ilk yönetici kurulumu zamanında tamamlanmadı.");
    }

    async Task WaitOwnedReadyAsync(string expected){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));while(true){if(_engine?.HasExited==true)throw new InvalidOperationException("Yerel motor bağlanamadı veya profil başka bir BitigMail örneğinde açık.");string? line=await DesktopProtocolReader.ReadLineAsync(_engine!.StandardOutput,timeout.Token,512);if(line is null)throw new InvalidOperationException("Yerel motor hazır sinyali vermedi.");if(string.Equals(line,expected,StringComparison.Ordinal))return;}}
    async Task PumpOutputAsync(){try{while(_engine is{HasExited:false}){string? line=await DesktopProtocolReader.ReadLineAsync(_engine.StandardOutput,maximumCharacters:2048);if(line is null)break;if(line.StartsWith("BITIGMAIL_SHUTDOWN_BLOCKED ",StringComparison.Ordinal))_shutdownBlocked.TrySetResult(line);else if(line=="BITIGMAIL_SETUP_REJECTED"){Trace("refresh rejected");_refreshCompletion?.TrySetException(new InvalidOperationException("Güvenli kurulum kanıtı yenilenemedi. Kurulum tamamlanmış olabilir; sayfayı yenileyin."));}else if(_refreshExpected is not null&&string.Equals(line,_refreshExpected,StringComparison.Ordinal)){Trace("refresh mac matched");_refreshCompletion?.TrySetResult(true);}}}catch(Exception ex){_shutdownBlocked.TrySetException(ex);_refreshCompletion?.TrySetException(ex);}}
    async Task WaitHttpReadyAsync(){using var client=new HttpClient{Timeout=TimeSpan.FromMilliseconds(500)};for(int i=0;i<80;i++){if(_engine?.HasExited==true)throw new InvalidOperationException("Yerel motor beklenmedik şekilde kapandı.");try{using var request=new HttpRequestMessage(HttpMethod.Get,_origin+"api/setup/status");request.Headers.Host=$"127.0.0.1:{_port}";if((await client.SendAsync(request)).StatusCode==HttpStatusCode.OK){_installLease?.Dispose();_installLease=null;SignalActivation();return;}}catch{}await Task.Delay(100);}throw new TimeoutException("Yerel motor zamanında hazır olmadı.");}
    void SignalActivation(){if(string.IsNullOrWhiteSpace(_activationSignal))return;using var signal=EventWaitHandle.OpenExisting(_activationSignal);signal.Set();}
    void OnClosing(object? sender,FormClosingEventArgs e){if(_realExit)return;e.Cancel=true;Hide();_tray.Visible=true;_tray.ShowBalloonTip(2000,"BitigMail","BitigMail arka planda çalışıyor. Devam eden işler korunuyor.",ToolTipIcon.Info);}
    void ShowFromTray(){Show();WindowState=FormWindowState.Normal;Activate();_tray.Visible=false;}
    async Task RequestOwnedExitAsync(){if(_engine is not{HasExited:false})return;if(_shutdownBlocked.Task.IsCompleted)_shutdownBlocked=NewShutdownSignal();await _commandWriteLock.WaitAsync();try{await _engine.StandardInput.WriteLineAsync("EXIT");await _engine.StandardInput.FlushAsync();}finally{_commandWriteLock.Release();}Task completed=await Task.WhenAny(_engine.WaitForExitAsync(),_shutdownBlocked.Task);if(completed==_shutdownBlocked.Task){string reason=await _shutdownBlocked.Task;throw new InvalidOperationException(reason.EndsWith("PERSISTENCE",StringComparison.Ordinal)?"İş durumu diske güvenle yazılamadı.":"Çalışan işin durumu çözümlenemedi.");}await _engine.WaitForExitAsync();}
    async Task ExitApplicationAsync(){if(_realExit||Interlocked.CompareExchange(ref _exitInProgress,1,0)!=0)return;Enabled=false;_tray.ShowBalloonTip(2000,"BitigMail","Devam eden işler güvenle tamamlanırken BitigMail kapanıyor.",ToolTipIcon.Info);try{await RequestOwnedExitAsync();_realExit=true;_tray.Visible=false;Close();}catch(Exception ex){Enabled=true;MessageBox.Show("Güvenli kapanış tamamlanamadı: "+ex.Message+"\nDevam eden işi kontrol edip yeniden deneyin.","BitigMail",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{Interlocked.Exchange(ref _exitInProgress,0);}}
    protected override void Dispose(bool disposing){if(disposing){_installLease?.Dispose();_web.Dispose();_tray.Dispose();_engine?.Dispose();}base.Dispose(disposing);}
    static int FreePort(){using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();return((IPEndPoint)listener.LocalEndpoint).Port;}
    static FileStream AcquireInstallActivationLease(string installRoot){string root=Path.GetFullPath(installRoot),baseDir=Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),version=Path.GetFileName(baseDir),expected=Path.TrimEndingDirectorySeparator(Path.Combine(root,"versions",version));if(!baseDir.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Kurulu uygulama yolu etkin sürüm düzeniyle eşleşmiyor.");string lockPath=Path.Combine(root,".install.lock");FileStream? lease=null;for(int i=0;i<100&&lease is null;i++){try{lease=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){Thread.Sleep(100);}}if(lease is null)throw new InvalidOperationException("Kurulum işleminin tamamlanması beklenemedi.");try{string pointer=Path.Combine(root,"active-version.txt");if(new FileInfo(pointer).Length is <1 or >64||File.ReadAllText(pointer).Trim()!=version)throw new InvalidOperationException("Bu uygulama sürümü artık etkin değil.");return lease;}catch{lease.Dispose();throw;}}
    static string? InferInstallRoot(){string baseDir=Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));DirectoryInfo version=new(baseDir);DirectoryInfo? versions=version.Parent;DirectoryInfo? root=versions?.Parent;if(versions is null||root is null||!versions.Name.Equals("versions",StringComparison.OrdinalIgnoreCase))return null;foreach(string path in new[]{baseDir,versions.FullName,root.FullName})if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Kurulu uygulama yolu bağlantı içeremez.");return File.Exists(Path.Combine(root.FullName,"active-version.txt"))&&File.Exists(Path.Combine(root.FullName,".install.lock"))?root.FullName:null;}
    static TaskCompletionSource<string> NewShutdownSignal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    void Trace(string message){if(_capturePath is not null)try{File.AppendAllText(_capturePath+".trace.txt",DateTimeOffset.UtcNow.ToString("O")+" "+message+Environment.NewLine);}catch{}}
    static string? Arg(string[] args,string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
}
