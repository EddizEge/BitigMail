using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BitigMail.Engine.Storage;

namespace BitigMail.LocalHost.Dialogs;

public class WinFormsFilePickerService : IFilePickerService
{
    private readonly FileHandleRegistry _handleRegistry;

    public WinFormsFilePickerService(FileHandleRegistry handleRegistry)
    {
        _handleRegistry = handleRegistry;
    }

    public Task<FilePickResult> PickSourceOstAsync()
    {
        var tcs = new TaskCompletionSource<FilePickResult>();

        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Dönüştürülecek Outlook Çevrimdışı Veri Dosyasını (OST) Seçin",
                    Filter = "Outlook Çevrimdışı Veri Dosyası (*.ost)|*.ost|Tüm Dosyalar (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    string path = dialog.FileName;
                    var fi = new FileInfo(path);
                    string handle = _handleRegistry.RegisterSource(path);

                    tcs.SetResult(new FilePickResult
                    {
                        Cancelled = false,
                        Handle = handle,
                        FileName = fi.Name,
                        DisplayPath = path,
                        SizeBytes = fi.Length
                    });
                }
                else
                {
                    tcs.SetResult(new FilePickResult { Cancelled = true });
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(new FilePickResult
                {
                    Cancelled = false,
                    Error = $"Dosya seçici açılırken hata oluştu: {ex.Message}"
                });
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    public Task<FilePickResult> PickTargetPstAsync(string? sourceHandle = null)
    {
        var tcs = new TaskCompletionSource<FilePickResult>();

        var thread = new Thread(() =>
        {
            try
            {
                string? sourcePath = !string.IsNullOrEmpty(sourceHandle) ? _handleRegistry.GetSourcePath(sourceHandle) : null;
                string defaultName = !string.IsNullOrEmpty(sourcePath)
                    ? Path.GetFileNameWithoutExtension(sourcePath) + "-donusturulen.pst"
                    : "donusturulen.pst";

                using var dialog = new SaveFileDialog
                {
                    Title = "Oluşturulacak Yeni Outlook Kişisel Klasör (PST) Dosyası Konumunu Belirleyin",
                    Filter = "Outlook Kişisel Klasör Dosyası (*.pst)|*.pst",
                    DefaultExt = "pst",
                    FileName = defaultName,
                    CheckPathExists = true,
                    OverwritePrompt = false // Custom refusal safeguard
                };

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    string targetPath = dialog.FileName;

                    // Safeguard: refuse if target already exists
                    if (File.Exists(targetPath))
                    {
                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Error = $"Hedef dosya zaten mevcut: '{Path.GetFileName(targetPath)}'. Güvenlik politikası gereği mevcut dosyanın üzerine yazılamaz. Lütfen yeni bir dosya adı seçin."
                        });
                        return;
                    }

                    // Safeguard: refuse if target equals source
                    if (!string.IsNullOrEmpty(sourcePath) &&
                        string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
                    {
                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Error = "Hedef dosya kaynak OST dosyası ile aynı olamaz. Lütfen farklı bir PST konumu seçin."
                        });
                        return;
                    }

                    string handle = _handleRegistry.RegisterTarget(targetPath);
                    tcs.SetResult(new FilePickResult
                    {
                        Cancelled = false,
                        Handle = handle,
                        FileName = Path.GetFileName(targetPath),
                        DisplayPath = targetPath
                    });
                }
                else
                {
                    tcs.SetResult(new FilePickResult { Cancelled = true });
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(new FilePickResult
                {
                    Cancelled = false,
                    Error = $"Hedef kayıt seçici açılırken hata: {ex.Message}"
                });
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    public Task<FilePickResult> PickSplitSourceAsync()
    {
        var tcs = new TaskCompletionSource<FilePickResult>();

        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Bölünecek Outlook Veri Dosyasını (PST veya OST) Seçin",
                    Filter = "Outlook Veri Dosyaları (*.pst;*.ost)|*.pst;*.ost|Outlook Kişisel Klasör Dosyası (*.pst)|*.pst|Outlook Çevrimdışı Veri Dosyası (*.ost)|*.ost|Tüm Dosyalar (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
                {
                    string path = dialog.FileName;
                    var fi = new FileInfo(path);
                    string handle = _handleRegistry.RegisterSource(path);

                    tcs.SetResult(new FilePickResult
                    {
                        Cancelled = false,
                        Handle = handle,
                        FileName = fi.Name,
                        DisplayPath = path,
                        SizeBytes = fi.Length
                    });
                }
                else
                {
                    tcs.SetResult(new FilePickResult { Cancelled = true });
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(new FilePickResult
                {
                    Cancelled = false,
                    Error = $"Bölme kaynak dosyası seçici açılırken hata oluştu: {ex.Message}"
                });
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    public Task<FilePickResult> PickOutputDirAsync()
    {
        var tcs = new TaskCompletionSource<FilePickResult>();

        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Bölünmüş arşiv parçalarının kaydedileceği hedef üst klasörü seçin",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                DialogResult result = dialog.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    string path = dialog.SelectedPath;
                    if (!Directory.Exists(path))
                    {
                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Error = $"Seçilen klasör mevcut değil: {path}"
                        });
                        return;
                    }

                    string handle = _handleRegistry.RegisterOutputDir(path);
                    tcs.SetResult(new FilePickResult
                    {
                        Cancelled = false,
                        Handle = handle,
                        FileName = new DirectoryInfo(path).Name,
                        DisplayPath = path
                    });
                }
                else
                {
                    tcs.SetResult(new FilePickResult { Cancelled = true });
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(new FilePickResult
                {
                    Cancelled = false,
                    Error = $"Hedef klasör seçici açılırken hata: {ex.Message}"
                });
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    public Task<FilePickResult> PickMimeSourceAsync(string mode = "eml-files")
    {
        var tcs = new TaskCompletionSource<FilePickResult>();
        var inspector = new MimeSourceInspector();

        var thread = new Thread(() =>
        {
            try
            {
                string normMode = (mode ?? "eml-files").Trim().ToLowerInvariant();

                if (normMode is "eml-tree" or "tree" or "directory")
                {
                    using var dialog = new FolderBrowserDialog
                    {
                        Description = "İçe aktarılacak EML klasör ağacını seçin",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = false
                    };

                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    {
                        string dirPath = dialog.SelectedPath;
                        var manifest = inspector.BuildEmlDirectoryManifest(dirPath);
                        string handle = _handleRegistry.RegisterMimeSource(manifest, dirPath);

                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Handle = handle,
                            FileName = new DirectoryInfo(dirPath).Name,
                            DisplayPath = dirPath,
                            SizeBytes = manifest.Entries.Sum(e => e.SizeBytes)
                        });
                    }
                    else
                    {
                        tcs.SetResult(new FilePickResult { Cancelled = true });
                    }
                }
                else if (normMode is "mbox" or "mboxrd")
                {
                    using var dialog = new OpenFileDialog
                    {
                        Title = "İçe aktarılacak MBOX (mboxrd) Dosyasını Seçin",
                        Filter = "MBOX Dosyaları (*.mbox;*.mbx)|*.mbox;*.mbx|Tüm Dosyalar (*.*)|*.*",
                        CheckFileExists = true,
                        CheckPathExists = true,
                        Multiselect = false
                    };

                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
                    {
                        string filePath = dialog.FileName;
                        var manifest = inspector.BuildMboxManifest(filePath);
                        string handle = _handleRegistry.RegisterMimeSource(manifest, filePath);

                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Handle = handle,
                            FileName = Path.GetFileName(filePath),
                            DisplayPath = filePath,
                            SizeBytes = new FileInfo(filePath).Length
                        });
                    }
                    else
                    {
                        tcs.SetResult(new FilePickResult { Cancelled = true });
                    }
                }
                else // default: eml-files
                {
                    using var dialog = new OpenFileDialog
                    {
                        Title = "İçe aktarılacak EML Dosyalarını Seçin",
                        Filter = "E-posta Dosyaları (*.eml)|*.eml|Tüm Dosyalar (*.*)|*.*",
                        CheckFileExists = true,
                        CheckPathExists = true,
                        Multiselect = true
                    };

                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK && dialog.FileNames != null && dialog.FileNames.Length > 0)
                    {
                        var manifest = inspector.BuildEmlFilesManifest(dialog.FileNames);
                        string display = $"{dialog.FileNames.Length} EML Dosyası";
                        string handle = _handleRegistry.RegisterMimeSource(manifest, display);

                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Handle = handle,
                            FileName = display,
                            DisplayPath = display,
                            SizeBytes = manifest.Entries.Sum(e => e.SizeBytes)
                        });
                    }
                    else
                    {
                        tcs.SetResult(new FilePickResult { Cancelled = true });
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(new FilePickResult
                {
                    Cancelled = false,
                    Error = $"MIME kaynak seçici açılırken hata: {ex.Message}"
                });
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    public Task<FilePickResult> PickEmlxSourceAsync(string mode = "tree")
    {
        var tcs = new TaskCompletionSource<FilePickResult>();
        var thread = new Thread(() =>
        {
            try
            {
                var normalizer = new EmlxNormalizationService();
                if (string.Equals(mode, "files", StringComparison.OrdinalIgnoreCase))
                {
                    using var dialog = new OpenFileDialog { Title = "Apple Mail EMLX dosyalarını seçin", Filter = "Apple Mail EMLX (*.emlx)|*.emlx", Multiselect = true, CheckFileExists = true };
                    if (dialog.ShowDialog() != DialogResult.OK) { tcs.SetResult(new() { Cancelled = true }); return; }
                    var manifest = normalizer.BuildFilesManifest(dialog.FileNames);
                    tcs.SetResult(new() { Cancelled = false, Handle = _handleRegistry.RegisterEmlxSource(manifest, $"{manifest.Entries.Count} EMLX dosyası"), FileName = $"{manifest.Entries.Count} EMLX dosyası", DisplayPath = $"{manifest.Entries.Count} EMLX dosyası", SizeBytes = manifest.TotalBytes });
                }
                else
                {
                    using var dialog = new FolderBrowserDialog { Description = "Apple Mail EMLX kaynak klasörünü seçin", UseDescriptionForTitle = true, ShowNewFolderButton = false };
                    if (dialog.ShowDialog() != DialogResult.OK) { tcs.SetResult(new() { Cancelled = true }); return; }
                    var manifest = normalizer.BuildDirectoryManifest(dialog.SelectedPath);
                    tcs.SetResult(new() { Cancelled = false, Handle = _handleRegistry.RegisterEmlxSource(manifest, dialog.SelectedPath), FileName = new DirectoryInfo(dialog.SelectedPath).Name, DisplayPath = dialog.SelectedPath, SizeBytes = manifest.TotalBytes });
                }
            }
            catch (Exception ex) { tcs.SetResult(new() { Cancelled = false, Error = ex.Message }); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start(); return tcs.Task;
    }

    public Task<FilePickResult> PickOutlookEmlSourceAsync()
    {
        var tcs = new TaskCompletionSource<FilePickResult>();
        var thread = new Thread(() => { try { using var dialog = new OpenFileDialog { Title = "PST, OST veya OLM kaynağını seçin", Filter = "Outlook posta depoları (*.pst;*.ost;*.olm)|*.pst;*.ost;*.olm", Multiselect = false, CheckFileExists = true }; if (dialog.ShowDialog() != DialogResult.OK) { tcs.SetResult(new() { Cancelled = true }); return; } var fi = new FileInfo(dialog.FileName); tcs.SetResult(new() { Cancelled = false, Handle = _handleRegistry.RegisterSource(fi.FullName), FileName = fi.Name, DisplayPath = fi.FullName, SizeBytes = fi.Length }); } catch (Exception ex) { tcs.SetResult(new() { Error = ex.Message }); } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start(); return tcs.Task;
    }

    public Task<FilePickResult> PickAsposeLicenseAsync()
    {
        var tcs=new TaskCompletionSource<FilePickResult>();var thread=new Thread(()=>{try{using var dialog=new OpenFileDialog{Title="Aspose lisans dosyasını seçin",Filter="Aspose lisans dosyası (*.lic)|*.lic",Multiselect=false,CheckFileExists=true};if(dialog.ShowDialog()!=DialogResult.OK){tcs.SetResult(new(){Cancelled=true});return;}var fi=new FileInfo(dialog.FileName);tcs.SetResult(new(){Cancelled=false,DisplayPath=fi.FullName,FileName=fi.Name,SizeBytes=fi.Length});}catch{tcs.SetResult(new(){Error="Lisans dosyası seçilemedi."});}});thread.SetApartmentState(ApartmentState.STA);thread.Start();return tcs.Task;
    }

    public Task<FilePickResult> PickArchiveBackupAsync()
    {
        var tcs=new TaskCompletionSource<FilePickResult>();var thread=new Thread(()=>{try{using var dialog=new OpenFileDialog{Title="BitigMail arşiv yedeğini seçin",Filter="BitigMail ZIP yedeği (*.zip)|*.zip",Multiselect=false,CheckFileExists=true};if(dialog.ShowDialog()!=DialogResult.OK){tcs.SetResult(new(){Cancelled=true});return;}var fi=new FileInfo(dialog.FileName);tcs.SetResult(new(){Cancelled=false,Handle=_handleRegistry.RegisterSource(fi.FullName),FileName=fi.Name,DisplayPath=fi.FullName,SizeBytes=fi.Length});}catch(Exception ex){tcs.SetResult(new(){Error=ex.Message});}});thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();return tcs.Task;
    }

    public Task<FilePickResult> PickArchiveSourceAsync(string mode = "eml-files")
    {
        var tcs = new TaskCompletionSource<FilePickResult>();
        var inspector = new MimeSourceInspector();

        var thread = new Thread(() =>
        {
            try
            {
                string normMode = (mode ?? "eml-files").Trim().ToLowerInvariant();

                if (normMode is "eml-tree" or "tree" or "directory")
                {
                    using var dialog = new FolderBrowserDialog
                    {
                        Description = "Arşivlenecek EML klasör ağacını seçin",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = false
                    };

                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    {
                        string dirPath = dialog.SelectedPath;
                        var manifest = inspector.BuildEmlDirectoryManifest(dirPath, MimeSourceInspector.MaxSingleMessageSizeBytes);
                        string handle = _handleRegistry.RegisterMimeSource(manifest, dirPath);

                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Handle = handle,
                            FileName = new DirectoryInfo(dirPath).Name,
                            DisplayPath = dirPath,
                            SizeBytes = manifest.Entries.Sum(e => e.SizeBytes)
                        });
                    }
                    else
                    {
                        tcs.SetResult(new FilePickResult { Cancelled = true });
                    }
                }
                else if (normMode is "mbox" or "mboxrd")
                {
                    using var dialog = new OpenFileDialog
                    {
                        Title = "Arşivlenecek MBOX (mboxrd) Dosyasını Seçin",
                        Filter = "MBOX Dosyaları (*.mbox;*.mbx)|*.mbox;*.mbx|Tüm Dosyalar (*.*)|*.*",
                        CheckFileExists = true,
                        CheckPathExists = true,
                        Multiselect = false
                    };

                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
                    {
                        string filePath = dialog.FileName;
                        var manifest = inspector.BuildMboxManifest(filePath, defaultFolder: null, maxRecordSizeBytes: MimeSourceInspector.MaxSingleMessageSizeBytes);
                        string handle = _handleRegistry.RegisterMimeSource(manifest, filePath);

                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Handle = handle,
                            FileName = Path.GetFileName(filePath),
                            DisplayPath = filePath,
                            SizeBytes = new FileInfo(filePath).Length
                        });
                    }
                    else
                    {
                        tcs.SetResult(new FilePickResult { Cancelled = true });
                    }
                }
                else // default: eml-files
                {
                    using var dialog = new OpenFileDialog
                    {
                        Title = "Arşivlenecek EML Dosyalarını Seçin",
                        Filter = "E-posta Dosyaları (*.eml)|*.eml|Tüm Dosyalar (*.*)|*.*",
                        CheckFileExists = true,
                        CheckPathExists = true,
                        Multiselect = true
                    };

                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK && dialog.FileNames != null && dialog.FileNames.Length > 0)
                    {
                        var manifest = inspector.BuildEmlFilesManifest(dialog.FileNames, defaultFolder: null, maxFileSizeBytes: MimeSourceInspector.MaxSingleMessageSizeBytes);
                        string display = $"{dialog.FileNames.Length} EML Dosyası";
                        string handle = _handleRegistry.RegisterMimeSource(manifest, display);

                        tcs.SetResult(new FilePickResult
                        {
                            Cancelled = false,
                            Handle = handle,
                            FileName = display,
                            DisplayPath = display,
                            SizeBytes = manifest.Entries.Sum(e => e.SizeBytes)
                        });
                    }
                    else
                    {
                        tcs.SetResult(new FilePickResult { Cancelled = true });
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.SetResult(new FilePickResult
                {
                    Cancelled = false,
                    Error = $"Arşiv kaynak seçici açılırken hata: {ex.Message}"
                });
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }
}
