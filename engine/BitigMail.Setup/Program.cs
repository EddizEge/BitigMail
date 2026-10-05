namespace BitigMail.Setup;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 0) { Application.Run(new SetupForm(DefaultPaths())); return 0; }
        if (args.Length==2&&args[0]=="--capture-setup") { Application.Run(new SetupForm(DefaultPaths(),args[1])); return 0; }
        return RunCliAsync(args).GetAwaiter().GetResult();
    }

    internal static InstallPaths DefaultPaths()
    {
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new(Path.Combine(local,"Programs","BitigMail"),Path.Combine(local,"BitigMail","desktop"));
    }

    static async Task<int> RunCliAsync(string[] args)
    {
        string command=args[0].ToLowerInvariant();
        string? Arg(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
        bool Flag(string name)=>args.Contains(name,StringComparer.OrdinalIgnoreCase);
        bool testRoots=Environment.GetEnvironmentVariable("BITIGMAIL_SETUP_TEST_ROOTS")=="1";
        var defaults=DefaultPaths();
        var paths=new InstallPaths(Path.GetFullPath(testRoots&&Arg("--install-root") is { } ir?ir:defaults.Root),Path.GetFullPath(testRoots&&Arg("--profile") is { } pr?pr:defaults.Profile));
        var installer=new InstallCoordinator(paths);
        try
        {
            switch(command)
            {
                case "install": case "update": await installer.InstallAsync(Arg("--package")??AppContext.BaseDirectory,Flag("--allow-unsigned-internal"));await InstallShellIntegration.PublishAsync(installer,paths);break;
                case "rollback": await installer.RollbackAsync();break;
                case "uninstall": await installer.UninstallAsync();if(!testRoots)InstallShellIntegration.RemoveShortcut(paths);break;
                case "launch": await installer.LaunchAsync(capturePath:Arg("--capture"));break;
                case "status": break;
                default: throw new ArgumentException("Komut: install|update|rollback|uninstall|launch|status");
            }
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("İşlem başarısız: "+ex.Message);return 1;}
    }
}

internal sealed class SetupForm:Form
{
    readonly InstallPaths _paths; readonly InstallCoordinator _installer; readonly Label _status=new(){AutoSize=true,MaximumSize=new(460,0)};
    public SetupForm(InstallPaths paths,string? capturePath=null)
    {
        _paths=paths;_installer=new(paths);Text="BitigMail Kurulum";Width=540;Height=600;MinimumSize=new(540,600);StartPosition=FormStartPosition.CenterScreen;
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(30),AutoScroll=true};Controls.Add(panel);
        panel.Controls.Add(new Label{Text="BitigMail",Font=new Font("Segoe UI",24,FontStyle.Bold),AutoSize=true});
        panel.Controls.Add(new Label{Text="İmzasız kurum içi Windows paketi",ForeColor=Color.DarkOrange,Font=new Font("Segoe UI",10,FontStyle.Bold),AutoSize=true,Margin=new Padding(3,6,3,12)});
        panel.Controls.Add(new Label{Text="Bu paket güvenilir yayıncı imzası taşımaz. Kurulum bütünlüğü SHA-256 ile denetlenir; bu yayıncı kimliğini doğrulamaz. Microsoft Edge WebView2 Runtime gereklidir.",AutoSize=true,MaximumSize=new(460,0),Margin=new Padding(3,0,3,18)});
        AddButton(panel,"Kur / Güncelle",async()=>{await _installer.InstallAsync(AppContext.BaseDirectory,true);await InstallShellIntegration.PublishAsync(_installer,_paths);return "Kurulum doğrulandı ve etkinleştirildi.";});
        AddButton(panel,"Uygulamayı aç",async()=>{await _installer.LaunchAsync();return "BitigMail başlatıldı.";});
        AddButton(panel,"Önceki sürüme dön",async()=>"Etkin sürüm: "+await _installer.RollbackAsync());
        AddButton(panel,"Kaldır",async()=>{var kept=await _installer.UninstallAsync();InstallShellIntegration.RemoveShortcut(_paths);return kept.Count==0?"Uygulama kaldırıldı. Hesaplar, arşivler ve kullanıcı verileri korundu.":$"Uygulama kaldırıldı; değiştirilmiş {kept.Count} dosya ve tüm kullanıcı verileri korundu.";});
        _status.Text=$"Kurulum: {_paths.Root}\nVeriler: {_paths.Profile}";_status.Margin=new Padding(3,18,3,3);panel.Controls.Add(_status);
        if(capturePath is not null)Shown+=(_,_)=>{using var bitmap=new Bitmap(ClientSize.Width,ClientSize.Height);DrawToBitmap(bitmap,ClientRectangle);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capturePath))!);bitmap.Save(capturePath);Close();};
    }
    void AddButton(Control parent,string text,Func<Task<string>> action){var button=new Button{Text=text,Width=440,Height=42,Margin=new Padding(3,5,3,5)};button.Click+=async(_,_)=>{foreach(Control c in parent.Controls)if(c is Button)c.Enabled=false;try{_status.Text=await action();}catch(Exception ex){_status.Text="İşlem başarısız: "+ex.Message;MessageBox.Show(_status.Text,"BitigMail Kurulum",MessageBoxButtons.OK,MessageBoxIcon.Error);}finally{foreach(Control c in parent.Controls)if(c is Button)c.Enabled=true;}};parent.Controls.Add(button);}
}

internal static class InstallShellIntegration
{
    static string ShortcutPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"BitigMail.lnk");
    public static async Task PublishAsync(InstallCoordinator installer,InstallPaths paths)
    {
        string? source=Environment.ProcessPath;if(source is null||!Path.GetFileName(source).Equals("BitigMail.Setup.exe",StringComparison.OrdinalIgnoreCase))return;
        string launcher=Path.GetFullPath(source).Equals(Path.GetFullPath(Path.Combine(paths.Root,"BitigMail.Setup.exe")),StringComparison.OrdinalIgnoreCase)?source:await installer.PublishBootstrapAsync(source);
        if(Environment.GetEnvironmentVariable("BITIGMAIL_SETUP_TEST_ROOTS")=="1")return;
        Type type=Type.GetTypeFromProgID("WScript.Shell")??throw new InvalidOperationException("Başlat menüsü kısayolu oluşturulamadı.");dynamic shell=Activator.CreateInstance(type)!;dynamic shortcut=shell.CreateShortcut(ShortcutPath);shortcut.TargetPath=launcher;shortcut.Arguments="launch";shortcut.WorkingDirectory=paths.Root;shortcut.Description="BitigMail";shortcut.Save();
    }
    public static void RemoveShortcut(InstallPaths paths){if(!File.Exists(ShortcutPath)||new FileInfo(ShortcutPath).Attributes.HasFlag(FileAttributes.ReparsePoint))return;Type? type=Type.GetTypeFromProgID("WScript.Shell");if(type is null)return;dynamic shell=Activator.CreateInstance(type)!;dynamic shortcut=shell.CreateShortcut(ShortcutPath);string expected=Path.Combine(paths.Root,"BitigMail.Setup.exe");if(string.Equals((string)shortcut.TargetPath,expected,StringComparison.OrdinalIgnoreCase)&&string.Equals(((string)shortcut.Arguments).Trim(),"launch",StringComparison.Ordinal))File.Delete(ShortcutPath);}
}
