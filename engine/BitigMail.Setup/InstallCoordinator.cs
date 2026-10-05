using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BitigMail.Engine.Distribution;

namespace BitigMail.Setup;

public sealed record InstallPaths(string Root, string Profile)
{
    public string Versions => Path.Combine(Root, "versions");
    public string Manifests => Path.Combine(Root, "manifests");
    public string Active => Path.Combine(Root, "active-version.txt");
    public string Previous => Path.Combine(Root, "previous-version.txt");
    public string Lock => Path.Combine(Root, ".install.lock");
}

public sealed class InstallCoordinator
{
    readonly InstallPaths _paths;
    readonly PackagePayloadValidator _validator = new();
    public InstallCoordinator(InstallPaths paths) => _paths = paths;

    public async Task<string> InstallAsync(string packageRoot, bool allowUnsigned, CancellationToken ct = default)
    {
        if (!allowUnsigned) throw new InvalidOperationException("Bu iç paket imzasızdır. Yalnızca doğrulanmış kurum içi kullanım için --allow-unsigned-internal gerekir.");
        using var operation = AcquireInstallLock();
        using var profile = DesktopInstanceLease.Acquire(_paths.Profile);
        int schema = DesktopProfileSchema.ReadOrInitialize(profile);
        string manifestPath = Path.GetFullPath(Path.Combine(packageRoot, "package-manifest.json"));
        string payload = Path.GetFullPath(Path.Combine(packageRoot, "payload"));
        await using var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = await PackageManifestReader.ReadAsync(manifestStream, ct);
        await _validator.ValidateAsync(payload, manifest, schema, ct);
        string versionRoot = VersionRoot(manifest.Version);
        if (Directory.Exists(versionRoot)) await _validator.ValidateAsync(versionRoot, manifest, schema, ct);
        else
        {
            string staging = Path.Combine(_paths.Versions, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                foreach (var file in manifest.Files)
                {
                    ct.ThrowIfCancellationRequested();
                    string source = SafeChild(payload, file.RelativePath), target = SafeChild(staging, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, false);
                }
                await _validator.ValidateAsync(staging, manifest, schema, ct);
                Directory.Move(staging, versionRoot);
            }
            finally { /* Failed private staging is preserved for guarded diagnosis/cleanup; never recurse blindly. */ }
        }
        Directory.CreateDirectory(_paths.Manifests);
        string installedManifest = ManifestPath(manifest.Version);
        if (File.Exists(installedManifest))
        {
            await using var priorStream=new FileStream(installedManifest,FileMode.Open,FileAccess.Read,FileShare.Read);
            var prior=await PackageManifestReader.ReadAsync(priorStream,ct);ValidateReceiptManifest(prior,manifest.Version);
            if (!ManifestEquals(prior,manifest)) throw new InvalidDataException("Aynı sürüm için farklı paket bildirimi reddedildi.");
        }
        else PublishReceipt(manifestPath, installedManifest);
        string? old = ReadVersionPointer(_paths.Active, required: false);
        if (old is not null && old != manifest.Version) AtomicWrite(_paths.Previous, old + "\n");
        AtomicWrite(_paths.Active, manifest.Version + "\n");
        return manifest.Version;
    }

    public async Task<string> RollbackAsync(CancellationToken ct = default)
    {
        using var operation = AcquireInstallLock();
        using var profile = DesktopInstanceLease.Acquire(_paths.Profile);
        int schema = DesktopProfileSchema.ReadOrInitialize(profile);
        string current = ReadVersionPointer(_paths.Active, true)!;
        string previous = ReadVersionPointer(_paths.Previous, true)!;
        await ValidateInstalledAsync(previous, schema, ct);
        AtomicWrite(_paths.Active, previous + "\n");
        AtomicWrite(_paths.Previous, current + "\n");
        return previous;
    }

    public async Task<IReadOnlyList<string>> UninstallAsync(CancellationToken ct = default)
    {
        using var operation = AcquireInstallLock();
        using var profile = DesktopInstanceLease.Acquire(_paths.Profile);
        var preserved = new List<string>();
        var owned = new List<(string Path, PackagePayloadFile File)>();
        if (!Directory.Exists(_paths.Manifests)) return preserved;
        foreach (string manifestPath in Directory.EnumerateFiles(_paths.Manifests, "*.json", SearchOption.TopDirectoryOnly))
        {
            RejectReparse(manifestPath);
            await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = await PackageManifestReader.ReadAsync(stream, ct);
            ValidateReceiptManifest(manifest,Path.GetFileNameWithoutExtension(manifestPath));
            string versionRoot = VersionRoot(manifest.Version);
            foreach (var file in manifest.Files) owned.Add((SafeChild(versionRoot, file.RelativePath), file));
        }
        var deletable = new List<string>();
        foreach (var item in owned) if (File.Exists(item.Path))
        {
            RejectReparse(item.Path);
            long length; string hash;
            await using (var input = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read)) { length=input.Length;hash=Convert.ToHexString(await SHA256.HashDataAsync(input, ct)); }
            if (length == item.File.Length && hash.Equals(item.File.Sha256, StringComparison.OrdinalIgnoreCase)) deletable.Add(item.Path); else preserved.Add(item.Path);
        }
        foreach (string path in deletable) { RejectReparse(path); File.Delete(path); }
        foreach (string manifestPath in Directory.EnumerateFiles(_paths.Manifests, "*.json", SearchOption.TopDirectoryOnly))
        {
            string version=Path.GetFileNameWithoutExtension(manifestPath),versionRoot=VersionRoot(version);DeleteEmptyChildren(versionRoot);
            if (!Directory.Exists(versionRoot)||!Directory.EnumerateFileSystemEntries(versionRoot).Any()){if(Directory.Exists(versionRoot))Directory.Delete(versionRoot);File.Delete(manifestPath);}
        }
        foreach (string pointer in new[]{_paths.Active,_paths.Previous}) if (File.Exists(pointer)) File.Delete(pointer);
        return preserved;
    }

    public async Task LaunchAsync(CancellationToken ct = default, string? capturePath = null)
    {
        using var operation = AcquireInstallLock();
        string version = ReadVersionPointer(_paths.Active, true)!;
        using (var profile=DesktopInstanceLease.Acquire(_paths.Profile)) await ValidateInstalledAsync(version,DesktopProfileSchema.ReadOrInitialize(profile),ct);
        string executable = SafeChild(VersionRoot(version), "BitigMail.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Etkin BitigMail çalıştırıcısı bulunamadı.", executable);
        string signal = "Local\\BitigMail.Activation." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, signal);
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        psi.ArgumentList.Add("--profile"); psi.ArgumentList.Add(_paths.Profile); psi.ArgumentList.Add("--install-root"); psi.ArgumentList.Add(_paths.Root); psi.ArgumentList.Add("--activation-signal"); psi.ArgumentList.Add(signal);if(capturePath is not null){psi.ArgumentList.Add("--capture");psi.ArgumentList.Add(Path.GetFullPath(capturePath));}
        using var child = Process.Start(psi) ?? throw new InvalidOperationException("BitigMail başlatılamadı.");
        operation.Dispose();
        Task<bool> signaled = Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(30)), ct);
        Task completed = await Task.WhenAny(signaled, child.WaitForExitAsync(ct));
        if (completed != signaled || !await signaled) throw new InvalidOperationException("BitigMail veri profilini zamanında devralamadı.");
    }

    public async Task ValidateInstalledAsync(string version, int dataSchema, CancellationToken ct)
    {
        await using var stream = new FileStream(ManifestPath(version), FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = await PackageManifestReader.ReadAsync(stream, ct);
        if (manifest.Version != version) throw new InvalidDataException("Sürüm bildirimi eşleşmiyor.");
        await _validator.ValidateAsync(VersionRoot(version), manifest, dataSchema, ct);
    }

    public async Task<string> PublishBootstrapAsync(string source, CancellationToken ct = default)
    {
        using var operation=AcquireInstallLock();string target=Path.Combine(_paths.Root,"BitigMail.Setup.exe"),receipt=Path.Combine(_paths.Root,".setup.sha256");RejectReparse(target);RejectReparse(receipt);
        if(File.Exists(target)){if(!File.Exists(receipt))throw new InvalidDataException("Mevcut kurulum başlatıcısının sahipliği doğrulanamadı.");string expected=File.ReadAllText(receipt).Trim();await using var old=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read);string actual=Convert.ToHexString(await SHA256.HashDataAsync(old,ct));if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Kurulum başlatıcısı değiştirilmiş; üzerine yazılmadı.");}
        string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";await using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){await input.CopyToAsync(output,ct);output.Flush(true);}File.Move(temp,target,true);await using(var check=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read)){AtomicWrite(receipt,Convert.ToHexString(await SHA256.HashDataAsync(check,ct))+"\n");}return target;
    }

    FileStream AcquireInstallLock()
    {
        RejectReparse(_paths.Root);RejectReparse(_paths.Versions);RejectReparse(_paths.Manifests);RejectReparse(_paths.Lock);Directory.CreateDirectory(_paths.Root);Directory.CreateDirectory(_paths.Versions);Directory.CreateDirectory(_paths.Manifests);
        try { return new FileStream(_paths.Lock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("Başka bir BitigMail kurulum veya başlatma işlemi sürüyor.", ex); }
    }
    string VersionRoot(string version) { PackagePayloadValidator.ValidateRelativePath(version); return SafeChild(_paths.Versions, version); }
    string ManifestPath(string version) { PackagePayloadValidator.ValidateRelativePath(version); return SafeChild(_paths.Manifests, version + ".json"); }
    static string SafeChild(string root, string relative) { PackagePayloadValidator.ValidateRelativePath(relative.Replace('\\','/')); string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar))); string prefix=Path.GetFullPath(root)+Path.DirectorySeparatorChar;if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Yol kurulum kökü dışında.");RejectReparse(full);return full; }
    static void RejectReparse(string path){for(string? p=Path.GetFullPath(path);p is not null;p=Path.GetDirectoryName(p))try{if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Kurulum yolu bağlantı içeremez.");}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}}
    static string? ReadVersionPointer(string path,bool required){RejectReparse(path);if(!File.Exists(path)){if(required)throw new InvalidDataException("Sürüm işaretçisi yok.");return null;}var info=new FileInfo(path);if(info.Length is <1 or >64)throw new InvalidDataException("Sürüm işaretçisi geçersiz.");string value=File.ReadAllText(path).Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}(?:\.[0-9]{1,4})?\z"))throw new InvalidDataException("Sürüm işaretçisi geçersiz.");return value;}
    static void AtomicWrite(string path,string value){Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] bytes=new UTF8Encoding(false).GetBytes(value);output.Write(bytes);output.Flush(true);}if(File.Exists(path))File.Move(temp,path,true);else File.Move(temp,path);}
    static void PublishReceipt(string source,string destination){string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}try{File.Move(temp,destination,false);}catch{if(File.Exists(temp))File.Delete(temp);throw;}}
    static void ValidateReceiptManifest(PackagePayloadManifest manifest,string expectedVersion){PackagePayloadValidator.ValidateManifest(manifest,manifest.MinimumDataSchema);if(!string.Equals(manifest.Version,expectedVersion,StringComparison.Ordinal))throw new InvalidDataException("Kurulum makbuzu sürüm adıyla eşleşmiyor.");}
    static bool ManifestEquals(PackagePayloadManifest left,PackagePayloadManifest right)=>left.SchemaVersion==right.SchemaVersion&&left.Product==right.Product&&left.Version==right.Version&&left.MinimumDataSchema==right.MinimumDataSchema&&left.MaximumDataSchema==right.MaximumDataSchema&&left.Files.Count==right.Files.Count&&left.Files.Zip(right.Files).All(x=>x.First.RelativePath==x.Second.RelativePath&&x.First.Length==x.Second.Length&&x.First.Sha256.Equals(x.Second.Sha256,StringComparison.OrdinalIgnoreCase));
    static void DeleteEmptyChildren(string root){if(!Directory.Exists(root))return;RejectReparse(root);var seen=new List<string>();var pending=new Stack<(string Path,int Depth)>();pending.Push((root,0));while(pending.TryPop(out var item)){if(item.Depth>32)throw new InvalidDataException("Kurulum dizin derinliği aşıldı.");RejectReparse(item.Path);foreach(string child in Directory.EnumerateDirectories(item.Path,"*",SearchOption.TopDirectoryOnly)){RejectReparse(child);seen.Add(child);pending.Push((child,item.Depth+1));}}foreach(string directory in seen.OrderByDescending(x=>x.Length))if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);}
}
