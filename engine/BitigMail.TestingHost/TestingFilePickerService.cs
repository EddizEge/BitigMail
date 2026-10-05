using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Aspose.Email.Mapi;
using Aspose.Email.Storage.Pst;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Dialogs;
using BitigMail.LocalHost.Jobs;

namespace BitigMail.TestingHost;

public class TestingFilePickerService : IFilePickerService
{
    public Task<FilePickResult> PickEmlxSourceAsync(string mode = "tree")
    {
        string root = Path.Combine(ResolveRepoRoot(), "fixtures", "emlx-corpus-v1", "complete");
        var service = new EmlxNormalizationService();
        var manifest = string.Equals(mode, "files", StringComparison.OrdinalIgnoreCase)
            ? service.BuildFilesManifest(Directory.GetFiles(root, "*.emlx", SearchOption.AllDirectories))
            : service.BuildDirectoryManifest(root);
        string handle = _handleRegistry.RegisterEmlxSource(manifest, root);
        return Task.FromResult(new FilePickResult { Cancelled = false, Handle = handle, FileName = "emlx-corpus-v1", DisplayPath = root, SizeBytes = manifest.TotalBytes });
    }
    public Task<FilePickResult> PickOutlookEmlSourceAsync()
    {
        string path = Path.Combine(ResolveRepoRoot(), "fixtures", "vendor-olm", "SampleOLM.olm"); var fi = new FileInfo(path);
        return Task.FromResult(new FilePickResult { Handle = _handleRegistry.RegisterSource(path), FileName = fi.Name, DisplayPath = path, SizeBytes = fi.Length });
    }
    public Task<FilePickResult> PickAsposeLicenseAsync()=>Task.FromResult(new FilePickResult{Cancelled=true});

    private readonly FileHandleRegistry _handleRegistry;
    private readonly JobManager? _jobManager;
    private bool _simulateCancelNext;
    private string? _activeSplitFixtureId;
    private string? _activeSplitFixturePath;
    private string? _activeMimeFixtureId;
    private string? _archiveBackupPath;
    public void SetArchiveBackupPath(string path)=>_archiveBackupPath=Path.GetFullPath(path);

    public TestingFilePickerService(FileHandleRegistry handleRegistry, JobManager? jobManager = null)
    {
        _handleRegistry = handleRegistry;
        _jobManager = jobManager;
    }

    public void SetSimulateCancel(bool cancel)
    {
        _simulateCancelNext = cancel;
    }

    public void ClearSplitSourceOverride()
    {
        _activeSplitFixtureId = null;
        _activeSplitFixturePath = null;
    }

    public string? ActiveSplitFixtureId => _activeSplitFixtureId;
    public string? ActiveSplitFixturePath => _activeSplitFixturePath;

    public bool TrySetSplitSourceFixture(string fixtureId, out string error)
    {
        if (string.IsNullOrWhiteSpace(fixtureId))
        {
            error = "Fixture kimliği boş veya eksik olamaz.";
            return false;
        }

        string trimmed = fixtureId.Trim();

        // Server-resolved completed job ID route (e.g. job:job-20260913... or job-20260913...)
        if (trimmed.StartsWith("job:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("job-", StringComparison.OrdinalIgnoreCase))
        {
            string actualJobId = trimmed.StartsWith("job:", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(4).Trim()
                : trimmed;

            if (!Regex.IsMatch(actualJobId, @"^[a-zA-Z0-9_-]+$"))
            {
                error = "Geçersiz iş kimliği biçimi.";
                return false;
            }

            if (_jobManager == null)
            {
                error = "Sunucu iş yöneticisi (JobManager) başlatılmamış.";
                return false;
            }

            var report = _jobManager.GetReport(actualJobId);
            string? outPst = report?.OutputPath;
            if (string.IsNullOrEmpty(outPst))
            {
                var job = _jobManager.GetJob(actualJobId);
                outPst = job?.OutputPath;
            }

            if (string.IsNullOrEmpty(outPst) || !File.Exists(outPst))
            {
                error = $"İş kimliğine ait tamamlanmış PST çıktısı bulunamadı: {actualJobId}";
                return false;
            }

            _activeSplitFixtureId = trimmed;
            _activeSplitFixturePath = outPst;
            error = string.Empty;
            return true;
        }

        // Strictly reject ANY path-like patterns: separators, directory traversal, drive letters, extensions
        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains("..") || trimmed.Contains(':') ||
            Path.IsPathRooted(trimmed) || trimmed.EndsWith(".pst", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".ost", StringComparison.OrdinalIgnoreCase))
        {
            error = "Doğrudan dosya yolu, göreceli yol veya dosya adı ile fixture seçimi yapılamaz. Yalnızca kapalı izin listesindeki fixture kimlikleri kabul edilir.";
            return false;
        }

        string normalized = trimmed.ToLowerInvariant();
        string? resolvedPath = null;

        switch (normalized)
        {
            case "genuine-pst":
            case "genuine_pst":
            case "pst":
                resolvedPath = ResolveApprovedPstFixturePath();
                break;

            case "genuine-ost":
            case "genuine_ost":
            case "ost":
                resolvedPath = ResolveApprovedOstFixturePath();
                break;

            case "synthetic-size":
            case "synthetic_size":
                resolvedPath = ResolveSyntheticSizeFixturePath();
                break;

            case "synthetic-oversize":
            case "synthetic_oversize":
            case "oversize":
                resolvedPath = ResolveSyntheticOversizeFixturePath();
                break;

            default:
                error = $"Bilinmeyen fixture kimliği: '{fixtureId}'. İzin verilen fixture kimlikleri: genuine-pst, genuine-ost, synthetic-size, synthetic-oversize.";
                return false;
        }

        if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
        {
            error = $"Seçilen test fixture dosyası sunucu üzerinde bulunamadı: {normalized}";
            return false;
        }

        _activeSplitFixtureId = normalized;
        _activeSplitFixturePath = resolvedPath;
        error = string.Empty;
        return true;
    }

    public void ClearMimeSourceOverride()
    {
        _activeMimeFixtureId = null;
    }

    public string? ActiveMimeFixtureId => _activeMimeFixtureId;

    public bool TrySetMimeSourceFixture(string fixtureId, out string error)
    {
        if (string.IsNullOrWhiteSpace(fixtureId))
        {
            error = "MIME fixture kimliği boş veya eksik olamaz.";
            return false;
        }

        string trimmed = fixtureId.Trim();

        if (trimmed.StartsWith("job:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("job-", StringComparison.OrdinalIgnoreCase))
        {
            string actualJobId = trimmed.StartsWith("job:", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(4).Trim()
                : trimmed;

            if (!Regex.IsMatch(actualJobId, @"^[a-zA-Z0-9_-]+$"))
            {
                error = "Geçersiz iş kimliği biçimi.";
                return false;
            }

            if (_jobManager == null)
            {
                error = "Sunucu iş yöneticisi (JobManager) başlatılmamış.";
                return false;
            }

            var report = _jobManager.GetReport(actualJobId);
            string? outPst = report?.BridgeTransfer?.OutputPath ?? report?.OutputPath;
            if (string.IsNullOrEmpty(outPst))
            {
                var job = _jobManager.GetJob(actualJobId);
                outPst = job?.BridgeTransfer?.OutputPath ?? job?.OutputPath;
            }

            if (string.IsNullOrEmpty(outPst) || (!Directory.Exists(outPst) && !File.Exists(outPst)))
            {
                error = $"İş kimliğine ait tamamlanmış çıktı bulunamadı: {actualJobId}";
                return false;
            }

            _activeMimeFixtureId = "job:" + actualJobId;
            error = string.Empty;
            return true;
        }

        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains("..") || trimmed.Contains(':') ||
            Path.IsPathRooted(trimmed) || trimmed.EndsWith(".pst", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".ost", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".mbox", StringComparison.OrdinalIgnoreCase))
        {
            error = "Doğrudan dosya yolu, göreceli yol veya dosya adı ile fixture seçimi yapılamaz. Yalnızca kapalı izin listesindeki fixture kimlikleri kabul edilir.";
            return false;
        }

        string normalized = trimmed.ToLowerInvariant();
        switch (normalized)
        {
            case "corpus-eml":
            case "corpus_eml":
            case "corpus-tree":
            case "corpus_tree":
            case "corpus-mbox":
            case "corpus_mbox":
            case "escape-edges":
            case "escape_edges":
            case "missing-date":
            case "missing_date":
            case "empty-eml":
            case "empty_eml":
            case "malformed-eml":
            case "malformed_eml":
            case "oversize-trial":
            case "oversize_trial":
            case "bridge-over50":
            case "bridge_over50":
            case "two-items":
            case "two_items":
            case "junk-mbox":
            case "junk_mbox":
            case "empty-mbox":
            case "empty_mbox":
            case "malformed-mbox":
            case "malformed_mbox":
            case "archive-edgecases":
            case "archive_edgecases":
            case "task019-tree-128m":
            case "task019-mbox-128m":
            case "task019-tree-1g":
            case "task019-mbox-1g":
                _activeMimeFixtureId = normalized;
                error = string.Empty;
                return true;

            default:
                error = $"Bilinmeyen MIME fixture kimliği: '{fixtureId}'. İzin verilen fixture kimlikleri: corpus-eml, corpus-tree, corpus-mbox, escape-edges, missing-date, empty-eml, malformed-eml, oversize-trial, bridge-over50, two-items, junk-mbox, empty-mbox, malformed-mbox, archive-edgecases, task019-tree-128m, task019-mbox-128m, task019-tree-1g, task019-mbox-1g.";
                return false;
        }
    }

    public Task<FilePickResult> PickSourceOstAsync()
    {
        if (_simulateCancelNext)
        {
            _simulateCancelNext = false;
            return Task.FromResult(new FilePickResult { Cancelled = true });
        }

        // Strictly locate the approved genuine task fixture
        string fixturePath = ResolveApprovedOstFixturePath();
        if (!File.Exists(fixturePath))
        {
            return Task.FromResult(new FilePickResult
            {
                Cancelled = false,
                Error = $"Test fixture bulunamadı: {fixturePath}"
            });
        }

        var fi = new FileInfo(fixturePath);
        string handle = _handleRegistry.RegisterSource(fixturePath);

        return Task.FromResult(new FilePickResult
        {
            Cancelled = false,
            Handle = handle,
            FileName = fi.Name,
            DisplayPath = fixturePath,
            SizeBytes = fi.Length
        });
    }

    public Task<FilePickResult> PickTargetPstAsync(string? sourceHandle = null)
    {
        if (_simulateCancelNext)
        {
            _simulateCancelNext = false;
            return Task.FromResult(new FilePickResult { Cancelled = true });
        }

        // Strictly generate an isolated temporary PST target under temp directory
        string tempDir = Path.Combine(Path.GetTempPath(), "bitigmail-qa");
        Directory.CreateDirectory(tempDir);

        string targetPath = Path.Combine(tempDir, $"headless-test-{Guid.NewGuid():N}.pst");
        string handle = _handleRegistry.RegisterTarget(targetPath);

        return Task.FromResult(new FilePickResult
        {
            Cancelled = false,
            Handle = handle,
            FileName = Path.GetFileName(targetPath),
            DisplayPath = targetPath
        });
    }

    public Task<FilePickResult> PickSplitSourceAsync()
    {
        if (_simulateCancelNext)
        {
            _simulateCancelNext = false;
            return Task.FromResult(new FilePickResult { Cancelled = true });
        }

        string fixturePath = !string.IsNullOrEmpty(_activeSplitFixturePath) && File.Exists(_activeSplitFixturePath)
            ? _activeSplitFixturePath
            : ResolveApprovedSplitFixturePath();

        if (!File.Exists(fixturePath))
        {
            return Task.FromResult(new FilePickResult
            {
                Cancelled = false,
                Error = $"Test split fixture bulunamadı: {fixturePath}"
            });
        }

        var fi = new FileInfo(fixturePath);
        string handle = _handleRegistry.RegisterSource(fixturePath);

        return Task.FromResult(new FilePickResult
        {
            Cancelled = false,
            Handle = handle,
            FileName = fi.Name,
            DisplayPath = fixturePath,
            SizeBytes = fi.Length
        });
    }

    public Task<FilePickResult> PickOutputDirAsync()
    {
        if (_simulateCancelNext)
        {
            _simulateCancelNext = false;
            return Task.FromResult(new FilePickResult { Cancelled = true });
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "bitigmail-qa-split", $"split-out-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string handle = _handleRegistry.RegisterOutputDir(tempDir);

        return Task.FromResult(new FilePickResult
        {
            Cancelled = false,
            Handle = handle,
            FileName = Path.GetFileName(tempDir),
            DisplayPath = tempDir
        });
    }

    public Task<FilePickResult> PickArchiveBackupAsync()
    {
        string? path=_archiveBackupPath??Environment.GetEnvironmentVariable("BITIGMAIL_TEST_ARCHIVE_BACKUP");
        if(string.IsNullOrWhiteSpace(path))
        {
            string root=Path.Combine(Path.GetTempPath(),"bitigmail-qa-split");
            path=Directory.Exists(root)?Directory.EnumerateFiles(root,"bitigmail-archive-backup-*.zip",SearchOption.AllDirectories).Select(x=>new FileInfo(x)).OrderByDescending(x=>x.LastWriteTimeUtc).FirstOrDefault()?.FullName:null;
        }
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return Task.FromResult(new FilePickResult{Cancelled=true});
        var fi=new FileInfo(path);return Task.FromResult(new FilePickResult{Handle=_handleRegistry.RegisterSource(fi.FullName),FileName=fi.Name,DisplayPath=fi.FullName,SizeBytes=fi.Length});
    }

    public Task<FilePickResult> PickMimeSourceAsync(string mode = "eml-files")
        => PickMimeSourceInternalAsync(mode, boundedArchive: false);

    public Task<FilePickResult> PickArchiveSourceAsync(string mode = "eml-files")
        => PickMimeSourceInternalAsync(mode, boundedArchive: true);

    private Task<FilePickResult> PickMimeSourceInternalAsync(string mode, bool boundedArchive)
    {
        if (_simulateCancelNext)
        {
            _simulateCancelNext = false;
            return Task.FromResult(new FilePickResult { Cancelled = true });
        }

        string fixtureId = _activeMimeFixtureId ?? (mode == "mbox" ? "corpus-mbox" : (mode == "eml-tree" ? "corpus-tree" : "corpus-eml"));
        string normId = fixtureId.ToLowerInvariant();

        var inspector = new MimeSourceInspector();
        MimeSourceManifest manifest;
        string displayPath;

        MimeSourceManifest BuildEmlFiles(IEnumerable<string> files, string folder) =>
            boundedArchive
                ? inspector.BuildEmlFilesManifest(files, folder, MimeSourceInspector.MaxSingleMessageSizeBytes)
                : inspector.BuildEmlFilesManifest(files, folder);

        MimeSourceManifest BuildEmlDir(string dir) =>
            boundedArchive
                ? inspector.BuildEmlDirectoryManifest(dir, MimeSourceInspector.MaxSingleMessageSizeBytes)
                : inspector.BuildEmlDirectoryManifest(dir);

        MimeSourceManifest BuildMbox(string path, string? folder = null) =>
            boundedArchive
                ? inspector.BuildMboxManifest(path, folder, MimeSourceInspector.MaxSingleMessageSizeBytes)
                : inspector.BuildMboxManifest(path, folder);

        try
        {
            switch (normId)
            {
                case "corpus-eml":
                case "corpus_eml":
                {
                    string emlDir = FindExistingPath(Path.Combine("fixtures", "mail-corpus-v1", "eml"));
                    if (!Directory.Exists(emlDir))
                    {
                        return Task.FromResult(new FilePickResult { Cancelled = false, Error = $"EML dizini bulunamadı: {emlDir}" });
                    }
                    var files = Directory.GetFiles(emlDir, "*.eml", SearchOption.TopDirectoryOnly);
                    manifest = BuildEmlFiles(files, "Corpus");
                    displayPath = "corpus-eml (12 dosya)";
                    break;
                }
                case "corpus-tree":
                case "corpus_tree":
                {
                    string emlDir = FindExistingPath(Path.Combine("fixtures", "mail-corpus-v1", "eml"));
                    if (!Directory.Exists(emlDir))
                    {
                        return Task.FromResult(new FilePickResult { Cancelled = false, Error = $"EML dizini bulunamadı: {emlDir}" });
                    }
                    string treeRoot = Path.Combine(Path.GetTempPath(), "bitigmail-qa-mime-tree", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(treeRoot);
                    string manifestPath = FindExistingPath(Path.Combine("fixtures", "mail-corpus-v1", "manifest.json"));
                    using (var corpus = JsonDocument.Parse(File.ReadAllText(manifestPath)))
                    {
                        foreach (var message in corpus.RootElement.GetProperty("messages").EnumerateArray())
                        {
                            string folder = message.GetProperty("folder").GetString()!;
                            string directory = Path.GetFullPath(Path.Combine(treeRoot, folder.Replace('/', Path.DirectorySeparatorChar)));
                            if (!directory.StartsWith(treeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Fixture klasörü izin verilen kökün dışında.");
                            Directory.CreateDirectory(directory);
                            string fileName = Path.GetFileName(message.GetProperty("emlPath").GetString()!);
                            File.Copy(Path.Combine(emlDir, fileName), Path.Combine(directory, fileName), overwrite: false);
                        }
                    }
                    File.WriteAllText(Path.Combine(treeRoot, "fixture-note.txt"), "Non-EML fixture file; should be counted as ignored.");
                    manifest = BuildEmlDir(treeRoot);
                    displayPath = "corpus-tree (özgün klasör haritası)";
                    break;
                }
                case "corpus-mbox":
                case "corpus_mbox":
                {
                    string mboxPath = FindExistingPath(Path.Combine("fixtures", "mail-corpus-v1", "corpus.mbox"));
                    if (!File.Exists(mboxPath))
                    {
                        return Task.FromResult(new FilePickResult { Cancelled = false, Error = $"MBOX dosyası bulunamadı: {mboxPath}" });
                    }
                    manifest = BuildMbox(mboxPath, "corpus");
                    displayPath = Path.GetFileName(mboxPath);
                    break;
                }
                case "escape-edges":
                case "escape_edges":
                {
                    string escapesPath = FindExistingPath(Path.Combine("fixtures", "mime-import-v1", "escape-edges", "escapes.mbox"));
                    if (!File.Exists(escapesPath))
                    {
                        return Task.FromResult(new FilePickResult { Cancelled = false, Error = $"Escapes MBOX dosyası bulunamadı: {escapesPath}" });
                    }
                    manifest = BuildMbox(escapesPath, "escapes");
                    displayPath = Path.GetFileName(escapesPath);
                    break;
                }
                case "missing-date":
                case "missing_date":
                {
                    string dir = ResolveSyntheticMissingDateFixtureDir();
                    manifest = BuildEmlDir(dir);
                    displayPath = "missing-date-fixture";
                    break;
                }
                case "empty-eml":
                case "empty_eml":
                {
                    string dir = ResolveSyntheticEmptyEmlFixtureDir();
                    manifest = BuildEmlDir(dir);
                    displayPath = "empty-eml-fixture";
                    break;
                }
                case "malformed-eml":
                case "malformed_eml":
                {
                    string dir = ResolveSyntheticMalformedEmlFixtureDir();
                    manifest = BuildEmlDir(dir);
                    displayPath = "malformed-eml-fixture";
                    break;
                }
                case "oversize-trial":
                case "oversize_trial":
                {
                    string dir = ResolveSyntheticOversizeTrialFixtureDir();
                    manifest = BuildEmlDir(dir);
                    displayPath = "oversize-trial-fixture (>50 öğe)";
                    break;
                }
                case "bridge-over50":
                case "bridge_over50":
                {
                    string dir = ResolveBridgeOver50FixtureDir();
                    manifest = BuildEmlDir(dir);
                    displayPath = "bridge-over50 (72 dosya)";
                    break;
                }
                case "two-items":
                case "two_items":
                {
                    string dir = ResolveSyntheticTwoItemsFixtureDir();
                    manifest = BuildEmlDir(dir);
                    displayPath = "two-items-fixture";
                    break;
                }
                case "junk-mbox":
                case "junk_mbox":
                {
                    string mboxPath = ResolveSyntheticJunkMboxFixturePath();
                    manifest = BuildMbox(mboxPath, "junk");
                    displayPath = Path.GetFileName(mboxPath);
                    break;
                }
                case "empty-mbox":
                case "empty_mbox":
                {
                    string mboxPath = ResolveSyntheticEmptyMboxFixturePath();
                    manifest = BuildMbox(mboxPath, "empty");
                    displayPath = Path.GetFileName(mboxPath);
                    break;
                }
                case "malformed-mbox":
                case "malformed_mbox":
                {
                    string mboxPath = ResolveSyntheticMalformedMboxFixturePath();
                    manifest = BuildMbox(mboxPath, "malformed");
                    displayPath = Path.GetFileName(mboxPath);
                    break;
                }
                case "archive-edgecases":
                case "archive_edgecases":
                {
                    string dir = ResolveArchiveEdgeCasesFixtureDir();
                    manifest = BuildEmlDir(dir);
                    displayPath = "archive-edgecases (4 dosya)";
                    break;
                }
                case "task019-tree-128m":
                {
                    string dir = ResolveTask019Path("stage-128m", "eml-tree", isDirectory: true);
                    manifest = BuildEmlDir(dir);
                    displayPath = "task019-tree-128m (128MiB 1024 EMLs)";
                    break;
                }
                case "task019-mbox-128m":
                {
                    string mbox = ResolveTask019Path("stage-128m", "corpus.mbox", isDirectory: false);
                    manifest = BuildMbox(mbox, "corpus");
                    displayPath = "task019-mbox-128m (128MiB)";
                    break;
                }
                case "task019-tree-1g":
                {
                    string dir = ResolveTask019Path("stage-1g", "eml-tree", isDirectory: true);
                    manifest = BuildEmlDir(dir);
                    displayPath = "task019-tree-1g";
                    break;
                }
                case "task019-mbox-1g":
                {
                    string mbox = ResolveTask019Path("stage-1g", "corpus.mbox", isDirectory: false);
                    manifest = BuildMbox(mbox, "corpus");
                    displayPath = "task019-mbox-1g";
                    break;
                }
                default:
                {
                    if (fixtureId.StartsWith("job:", StringComparison.OrdinalIgnoreCase))
                    {
                        string actualJobId = fixtureId.Substring(4).Trim();
                        var report = _jobManager?.GetReport(actualJobId);
                        string? outPath = report?.BridgeTransfer?.OutputPath ?? report?.OutputPath;
                        if (string.IsNullOrEmpty(outPath))
                        {
                            var job = _jobManager?.GetJob(actualJobId);
                            outPath = job?.BridgeTransfer?.OutputPath ?? job?.OutputPath;
                        }

                        if (string.IsNullOrEmpty(outPath))
                        {
                            return Task.FromResult(new FilePickResult { Cancelled = false, Error = $"İş çıktısı bulunamadı: {actualJobId}" });
                        }

                        if (Directory.Exists(outPath))
                        {
                            var mboxFiles = Directory.GetFiles(outPath, "*.mbox", SearchOption.AllDirectories);
                            if (mboxFiles.Length > 0 && Directory.GetFiles(outPath, "*.eml", SearchOption.AllDirectories).Length == 0)
                            {
                                manifest = BuildMbox(mboxFiles[0], Path.GetFileNameWithoutExtension(mboxFiles[0]));
                            }
                            else
                            {
                                manifest = BuildEmlDir(outPath);
                            }
                            displayPath = Path.GetFileName(outPath);
                            break;
                        }
                        else if (File.Exists(outPath))
                        {
                            manifest = BuildMbox(outPath, Path.GetFileNameWithoutExtension(outPath));
                            displayPath = Path.GetFileName(outPath);
                            break;
                        }
                        else
                        {
                            return Task.FromResult(new FilePickResult { Cancelled = false, Error = $"İş çıktısı diskte bulunamadı: {outPath}" });
                        }
                    }

                    return Task.FromResult(new FilePickResult { Cancelled = false, Error = $"Bilinmeyen MIME fixture kimliği: {fixtureId}" });
                }
            }

            string handle = _handleRegistry.RegisterMimeSource(manifest, displayPath);
            long totalSize = manifest.Entries.Sum(e => e.SizeBytes);

            return Task.FromResult(new FilePickResult
            {
                Cancelled = false,
                Handle = handle,
                FileName = displayPath,
                DisplayPath = displayPath,
                SizeBytes = totalSize
            });
        }
        catch (InvalidDataException ex)
        {
            return Task.FromResult(new FilePickResult
            {
                Cancelled = false,
                Error = ex.Message
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new FilePickResult
            {
                Cancelled = false,
                Error = $"MIME fixture hazırlama hatası: {ex.Message}"
            });
        }
    }

    public static string ResolveApprovedSplitFixturePath()
    {
        string[] relativePaths = new[]
        {
            Path.Combine("lab", "ost-spike", "output", "genuine-full-converted-04.pst"),
            Path.Combine("lab", "ost-spike", "input", "bitigmail-lab-full.ost")
        };

        foreach (var rel in relativePaths)
        {
            string candidate = FindExistingPath(rel);
            if (File.Exists(candidate)) return candidate;
        }

        return ResolveApprovedOstFixturePath();
    }

    public static string ResolveApprovedPstFixturePath()
    {
        return FindExistingPath(Path.Combine("lab", "ost-spike", "output", "genuine-full-converted-04.pst"));
    }

    public static string ResolveApprovedOstFixturePath()
    {
        return FindExistingPath(Path.Combine("lab", "ost-spike", "input", "bitigmail-lab-full.ost"));
    }

    private sealed class SyntheticFixtureManifest
    {
        public string SourceSha256 { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public int TotalMessages { get; set; }
    }

    public static string ResolveSyntheticSizeFixturePath()
    {
        // Preserve earlier evidence; v2 explicitly clears dates for the two undated messages.
        string fixtureDir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "task012-v2");
        Directory.CreateDirectory(fixtureDir);
        string pstPath = Path.Combine(fixtureDir, "synthetic-size.pst");
        string manifestPath = Path.Combine(fixtureDir, "synthetic-size-manifest.json");

        if (File.Exists(pstPath) && File.Exists(manifestPath))
        {
            try
            {
                string manifestJson = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<SyntheticFixtureManifest>(manifestJson);
                if (manifest != null && !string.IsNullOrEmpty(manifest.SourceSha256) && manifest.TotalMessages == 9)
                {
                    var fi = new FileInfo(pstPath);
                    if (fi.Length == manifest.FileSizeBytes)
                    {
                        using var fs = File.OpenRead(pstPath);
                        using var sha = SHA256.Create();
                        string actualHash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                        if (string.Equals(actualHash, manifest.SourceSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            return pstPath;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to regenerate safely if manifest read or hashing fails
            }
        }

        // Generate canonical 9 messages x 256 KiB attachments across 2 folders
        // Folder 1: "Projeler" (5 items: 2022, 2023, 2024, 2024, 2025)
        // Folder 2: "Musteriler" (4 items: 2024, 2026, Tarihsiz, Tarihsiz)
        string stagingPst = Path.Combine(fixtureDir, $"staging-size-{Guid.NewGuid():N}.pst");
        string stagingManifest = Path.Combine(fixtureDir, $"staging-manifest-{Guid.NewGuid():N}.json");

        try
        {
            using (var pst = PersonalStorage.Create(stagingPst, FileFormatVersion.Unicode))
            {
                var folderProjeler = pst.RootFolder.AddSubFolder("Projeler");
                var folderMusteriler = pst.RootFolder.AddSubFolder("Musteriler");

                var itemsSpec = new[]
                {
                    new { Folder = folderProjeler, Subject = "Proje 2022 Raporu", Date = (DateTime?)new DateTime(2022, 5, 10, 10, 0, 0, DateTimeKind.Utc), AttSeed = 1 },
                    new { Folder = folderProjeler, Subject = "Proje 2023 Planı", Date = (DateTime?)new DateTime(2023, 8, 15, 14, 30, 0, DateTimeKind.Utc), AttSeed = 2 },
                    new { Folder = folderProjeler, Subject = "Proje 2024 Faz 1", Date = (DateTime?)new DateTime(2024, 1, 20, 9, 0, 0, DateTimeKind.Utc), AttSeed = 3 },
                    new { Folder = folderProjeler, Subject = "Proje 2024 Faz 2", Date = (DateTime?)new DateTime(2024, 6, 12, 11, 0, 0, DateTimeKind.Utc), AttSeed = 4 },
                    new { Folder = folderProjeler, Subject = "Proje 2025 Vizyon", Date = (DateTime?)new DateTime(2025, 3, 1, 15, 0, 0, DateTimeKind.Utc), AttSeed = 5 },

                    new { Folder = folderMusteriler, Subject = "Müşteri 2024 Sözleşme", Date = (DateTime?)new DateTime(2024, 9, 10, 8, 0, 0, DateTimeKind.Utc), AttSeed = 6 },
                    new { Folder = folderMusteriler, Subject = "Müşteri 2026 Mutabakat", Date = (DateTime?)new DateTime(2026, 2, 14, 16, 0, 0, DateTimeKind.Utc), AttSeed = 7 },
                    new { Folder = folderMusteriler, Subject = "Müşteri Tarihsiz Not 1", Date = (DateTime?)null, AttSeed = 8 },
                    new { Folder = folderMusteriler, Subject = "Müşteri Tarihsiz Not 2", Date = (DateTime?)null, AttSeed = 9 },
                };

                const int attSize = 256 * 1024; // 256 KiB

                foreach (var item in itemsSpec)
                {
                    byte[] attBytes = new byte[attSize];
                    for (int i = 0; i < attBytes.Length; i++)
                    {
                        attBytes[i] = (byte)((item.AttSeed * 31 + i) % 256);
                    }

                    string attName = $"ek_{item.AttSeed}.dat";
                    using var msg = new MapiMessage(
                        "sender@bitigmail.test",
                        "receiver@bitigmail.test",
                        item.Subject,
                        $"Gövde metni: {item.Subject} - Boyut: 256 KiB ek içerir.");

                    if (item.Date.HasValue)
                    {
                        msg.ClientSubmitTime = item.Date.Value;
                        msg.DeliveryTime = item.Date.Value;
                    }
                    else
                    {
                        msg.ClientSubmitTime = DateTime.MinValue;
                        msg.DeliveryTime = DateTime.MinValue;
                        msg.RemoveProperty(MapiPropertyTag.PR_CLIENT_SUBMIT_TIME);
                        msg.RemoveProperty(MapiPropertyTag.PR_MESSAGE_DELIVERY_TIME);
                    }

                    msg.Attachments.Add(attName, attBytes);
                    item.Folder.AddMessage(msg);
                }
            }

            var fi = new FileInfo(stagingPst);
            string fileSha256;
            using (var fs = File.OpenRead(stagingPst))
            using (var sha = SHA256.Create())
            {
                fileSha256 = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            }

            var manifestObj = new SyntheticFixtureManifest
            {
                SourceSha256 = fileSha256,
                FileSizeBytes = fi.Length,
                TotalMessages = 9
            };
            File.WriteAllText(stagingManifest, JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true }));

            File.Move(stagingPst, pstPath, overwrite: true);
            File.Move(stagingManifest, manifestPath, overwrite: true);

            return pstPath;
        }
        finally
        {
            if (File.Exists(stagingPst))
            {
                try { File.Delete(stagingPst); } catch { }
            }
            if (File.Exists(stagingManifest))
            {
                try { File.Delete(stagingManifest); } catch { }
            }
        }
    }

    public static string ResolveSyntheticOversizeFixturePath()
    {
        string fixtureDir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures");
        Directory.CreateDirectory(fixtureDir);
        string pstPath = Path.Combine(fixtureDir, "synthetic-oversize.pst");
        if (!File.Exists(pstPath))
        {
            string stagingPst = Path.Combine(fixtureDir, $"staging-oversize-{Guid.NewGuid():N}.pst");
            try
            {
                using (var pst = PersonalStorage.Create(stagingPst, FileFormatVersion.Unicode))
                {
                    var folder = pst.RootFolder.AddSubFolder("BuyukMesaj");
                    using var msg = new MapiMessage(
                        "oversize@bitigmail.test",
                        "receiver@bitigmail.test",
                        "Büyük Tek İleti",
                        "Bu ileti tek başına boyutu 1MB'ı aşan bir ek içerir.");

                    byte[] att = new byte[3 * 1024 * 1024]; // 3 MB
                    for (int i = 0; i < att.Length; i++)
                    {
                        att[i] = (byte)(i % 251);
                    }

                    msg.DeliveryTime = DateTime.UtcNow;
                    msg.Attachments.Add("buyuk_ek.bin", att);
                    folder.AddMessage(msg);
                }

                File.Move(stagingPst, pstPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(stagingPst))
                {
                    try { File.Delete(stagingPst); } catch { }
                }
            }
        }
        return pstPath;
    }

    public static string ResolveSyntheticMissingDateFixtureDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "missing-date");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "no-date.eml");
        if (!File.Exists(file))
        {
            File.WriteAllText(file,
                "From: sender@test.local\r\n" +
                "To: receiver@test.local\r\n" +
                "Subject: Tarihsiz Ileti\r\n" +
                "\r\n" +
                "Bu iletide Date basligi yoktur.\r\n");
        }
        return dir;
    }

    public static string ResolveSyntheticEmptyEmlFixtureDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "empty-eml");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "empty.eml");
        if (!File.Exists(file))
        {
            File.WriteAllBytes(file, Array.Empty<byte>());
        }
        return dir;
    }

    public static string ResolveSyntheticMalformedEmlFixtureDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "malformed-eml");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "malformed.eml");
        if (!File.Exists(file))
        {
            File.WriteAllText(file, "THIS IS NOT RFC822 EMAIL DATA -- CORRUPT STREAM \0\0\0\xFF\xFE");
        }
        return dir;
    }

    public static string ResolveSyntheticOversizeTrialFixtureDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "oversize-trial");
        Directory.CreateDirectory(dir);
        for (int i = 1; i <= 52; i++)
        {
            string file = Path.Combine(dir, $"msg-{i:D3}.eml");
            if (!File.Exists(file))
            {
                File.WriteAllText(file,
                    $"From: sender{i}@test.local\r\n" +
                    $"To: receiver{i}@test.local\r\n" +
                    $"Date: Mon, 10 Jan 2022 10:00:00 +0300\r\n" +
                    $"Subject: Trial Test {i}\r\n" +
                    $"\r\n" +
                    $"Trial limit test body {i}\r\n");
            }
        }
        return dir;
    }

    public static string ResolveBridgeOver50FixtureDir()
    {
        string fixtureJsonPath = FindExistingPath(Path.Combine(".codex-coordination", "evidence", "TASK-017", "over50-fixture.json"));
        if (File.Exists(fixtureJsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(fixtureJsonPath));
                if (doc.RootElement.TryGetProperty("directory", out var dirProp))
                {
                    string configuredDir = dirProp.GetString()!;
                    if (Directory.Exists(configuredDir))
                    {
                        return configuredDir;
                    }
                }
            }
            catch { }
        }

        string fallbackDir = FindExistingPath(Path.Combine("runtime", "task017-qa", "over50-1cd78704"));
        if (Directory.Exists(fallbackDir))
        {
            return fallbackDir;
        }

        throw new DirectoryNotFoundException("bridge-over50 fixture dizini bulunamadı.");
    }

    public static string ResolveArchiveEdgeCasesFixtureDir()
    {
        string fixtureJsonPath = FindExistingPath(Path.Combine(".codex-coordination", "evidence", "TASK-018", "edge-fixture.json"));
        if (File.Exists(fixtureJsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(fixtureJsonPath));
                if (doc.RootElement.TryGetProperty("directory", out var dirProp))
                {
                    string configuredDir = dirProp.GetString()!;
                    if (Directory.Exists(configuredDir))
                    {
                        return configuredDir;
                    }

                    string folderName = Path.GetFileName(configuredDir);
                    string localDir = FindExistingPath(Path.Combine("runtime", "task018-qa", folderName));
                    if (Directory.Exists(localDir))
                    {
                        return localDir;
                    }
                }
            }
            catch { }
        }

        string fallbackDir = FindExistingPath(Path.Combine("runtime", "task018-qa", "archive-edges-cbab019c"));
        if (Directory.Exists(fallbackDir))
        {
            return fallbackDir;
        }

        throw new DirectoryNotFoundException("archive-edgecases fixture dizini bulunamadı.");
    }

    public static string ResolveSyntheticTwoItemsFixtureDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "two-items");
        Directory.CreateDirectory(dir);
        string corpusEmlDir = FindExistingPath(Path.Combine("fixtures", "mail-corpus-v1", "eml"));
        string file1 = Path.Combine(dir, "msg-01.eml");
        string file2 = Path.Combine(dir, "msg-02.eml");
        if (!File.Exists(file1) && File.Exists(Path.Combine(corpusEmlDir, "msg-01.eml")))
            File.Copy(Path.Combine(corpusEmlDir, "msg-01.eml"), file1, true);
        if (!File.Exists(file2) && File.Exists(Path.Combine(corpusEmlDir, "msg-02.eml")))
            File.Copy(Path.Combine(corpusEmlDir, "msg-02.eml"), file2, true);
        return dir;
    }

    public static string ResolveSyntheticJunkMboxFixturePath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "negative-mbox");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "junk.mbox");
        File.WriteAllText(file, "Corrupt leading junk before From line\r\nFrom sender@test.local Mon Jan 10 10:00:00 2022\r\nSubject: Test\r\n\r\nBody\r\n");
        return file;
    }

    public static string ResolveSyntheticEmptyMboxFixturePath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "negative-mbox");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "empty.mbox");
        File.WriteAllBytes(file, Array.Empty<byte>());
        return file;
    }

    public static string ResolveSyntheticMalformedMboxFixturePath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bitigmail-testing-fixtures", "negative-mbox");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "malformed.mbox");
        // Missing blank line separator between header and body
        File.WriteAllText(file, "From sender@test.local Mon Jan 10 10:00:00 2022\r\nSubject: Test\r\nMissingBlankLineSeparatorBodyLine\r\n");
        return file;
    }

    private static string FindExistingPath(string relativePath)
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return Path.GetFullPath(relativePath);
    }

    private static string ResolveTask019Path(string stage, string relativeName, bool isDirectory)
    {
        string repoRoot = ResolveRepoRoot();
        string task019Base = Path.GetFullPath(Path.Combine(repoRoot, "runtime", "task019"));
        string targetPath = Path.GetFullPath(Path.Combine(task019Base, stage, "source", relativeName));

        // Strict containment check: targetPath must be strictly within task019Base
        if (!targetPath.StartsWith(task019Base + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Fixture yolu izin verilen kökün dışında: {targetPath}");
        }

        if (isDirectory)
        {
            if (!Directory.Exists(targetPath))
            {
                throw new DirectoryNotFoundException($"TASK019 EML dizini bulunamadı: {targetPath}");
            }

            var di = new DirectoryInfo(targetPath);
            var curr = di;
            while (curr != null && curr.FullName.StartsWith(task019Base, StringComparison.OrdinalIgnoreCase))
            {
                if (curr.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point / symlink dizini tespit edildi: {curr.FullName}");
                }
                curr = curr.Parent;
            }
        }
        else
        {
            if (!File.Exists(targetPath))
            {
                throw new FileNotFoundException($"TASK019 MBOX dosyası bulunamadı: {targetPath}", targetPath);
            }

            var fi = new FileInfo(targetPath);
            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point / symlink dosyası tespit edildi: {targetPath}");
            }

            var curr = fi.Directory;
            while (curr != null && curr.FullName.StartsWith(task019Base, StringComparison.OrdinalIgnoreCase))
            {
                if (curr.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException($"[GÜVENLİK ENGELİ] Reparse point / symlink dizini tespit edildi: {curr.FullName}");
                }
                curr = curr.Parent;
            }
        }

        return targetPath;
    }

    private static string ResolveRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "engine")) || Directory.Exists(Path.Combine(current, "prototype")))
            {
                return Path.GetFullPath(current);
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "engine")) || Directory.Exists(Path.Combine(current, "prototype")))
            {
                return Path.GetFullPath(current);
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
