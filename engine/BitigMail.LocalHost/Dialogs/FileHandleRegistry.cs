using System;
using System.Collections.Concurrent;
using System.IO;
using BitigMail.Engine.Models;
using BitigMail.Engine.Storage;
using BitigMail.LocalHost.Security;
using Microsoft.AspNetCore.Http;

namespace BitigMail.LocalHost.Dialogs;

public class FileHandleRegistry
{
    private readonly IHttpContextAccessor? _http;
    private readonly ConcurrentDictionary<string,(string UserId,string SessionId)> _owners=new(StringComparer.Ordinal);
    public FileHandleRegistry(IHttpContextAccessor? http=null)=>_http=http;
    private readonly ConcurrentDictionary<string, SourceHandleEntry> _sourceHandles = new();
    private readonly ConcurrentDictionary<string, TargetHandleEntry> _targetHandles = new();
    private readonly ConcurrentDictionary<string, OutputDirHandleEntry> _outputDirHandles = new();
    private readonly ConcurrentDictionary<string, MimeSourceHandleEntry> _mimeSourceHandles = new();
    private readonly ConcurrentDictionary<string, RegisteredSelection> _selections = new();
    private readonly ConcurrentDictionary<string, RegisteredSplitPlan> _splitPlans = new();
    private readonly ConcurrentDictionary<string, EmlxSourceHandleEntry> _emlxSourceHandles = new();

    public int ActiveSelectionsCount => _selections.Count;

    public void RegisterSelection(RegisteredSelection selection)
    {
        _selections[selection.SelectionId] = selection;
        Bind(selection.SelectionId);
    }

    public RegisteredSelection? GetSelection(string selectionId)
    {
        return _selections.TryGetValue(selectionId, out var sel) ? sel : null;
    }

    public void RegisterSplitPlan(RegisteredSplitPlan plan)
    {
        _splitPlans[plan.PlanId] = plan;
        Bind(plan.PlanId);
    }

    public RegisteredSplitPlan? GetSplitPlan(string planId)
    {
        return _splitPlans.TryGetValue(planId, out var plan) ? plan : null;
    }

    public string RegisterSource(string fullPath)
    {
        string handle = "src_" + Guid.NewGuid().ToString("N");
        var fi = new FileInfo(fullPath);
        _sourceHandles[handle] = new SourceHandleEntry
        {
            Handle = handle,
            FullPath = fullPath,
            FileName = fi.Name,
            SizeBytes = fi.Exists ? fi.Length : 0,
            RegisteredAt = DateTimeOffset.UtcNow
        };
        Bind(handle);
        return handle;
    }

    public string RegisterTarget(string fullPath)
    {
        string handle = "tgt_" + Guid.NewGuid().ToString("N");
        _targetHandles[handle] = new TargetHandleEntry
        {
            Handle = handle,
            FullPath = fullPath,
            FileName = Path.GetFileName(fullPath),
            RegisteredAt = DateTimeOffset.UtcNow
        };
        Bind(handle);
        return handle;
    }

    public void AttachAnalysis(string handle, OstAnalysisResult analysis)
    {
        if (_sourceHandles.TryGetValue(handle, out var entry))
        {
            entry.AnalysisResult = analysis;
            entry.BoundSha256 = analysis.SourceSha256;
            entry.BoundItemCount = analysis.PhysicalTotalItems > 0 ? analysis.PhysicalTotalItems : analysis.TotalItems;
        }
    }

    public string RegisterOutputDir(string fullPath)
    {
        string handle = "dir_" + Guid.NewGuid().ToString("N");
        var di = new DirectoryInfo(fullPath);
        _outputDirHandles[handle] = new OutputDirHandleEntry
        {
            Handle = handle,
            FullPath = fullPath,
            DirectoryName = di.Name,
            RegisteredAt = DateTimeOffset.UtcNow
        };
        Bind(handle);
        return handle;
    }

    public string? GetSourcePath(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !handle.StartsWith("src_"))
            return null;
        return _sourceHandles.TryGetValue(handle, out var entry) ? entry.FullPath : null;
    }

    public string? GetTargetPath(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !handle.StartsWith("tgt_"))
            return null;
        return _targetHandles.TryGetValue(handle, out var entry) ? entry.FullPath : null;
    }

    public string? GetOutputDirPath(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !handle.StartsWith("dir_"))
            return null;
        return _outputDirHandles.TryGetValue(handle, out var entry) ? entry.FullPath : null;
    }

    public SourceHandleEntry? GetSourceEntry(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !handle.StartsWith("src_"))
            return null;
        return _sourceHandles.TryGetValue(handle, out var entry) ? entry : null;
    }

    public TargetHandleEntry? GetTargetEntry(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !handle.StartsWith("tgt_"))
            return null;
        return _targetHandles.TryGetValue(handle, out var entry) ? entry : null;
    }

    public string RegisterMimeSource(MimeSourceManifest manifest, string displayPath)
    {
        string handle = "msrc_" + Guid.NewGuid().ToString("N");
        _mimeSourceHandles[handle] = new MimeSourceHandleEntry
        {
            Handle = handle,
            SourceKind = manifest.SourceKind,
            Dialect = manifest.Dialect,
            DisplayPath = displayPath,
            RootPath = manifest.RootPath,
            FilePaths = manifest.Entries.Select(e => e.CanonicalPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RegisteredAt = DateTimeOffset.UtcNow,
            Manifest = manifest,
            BoundFingerprint = manifest.AggregateFingerprint,
            BoundItemCount = manifest.Entries.Count
        };
        Bind(handle);
        return handle;
    }

    public void AttachMimeAnalysis(string handle, MimeAnalysisResult analysis)
    {
        if (_mimeSourceHandles.TryGetValue(handle, out var entry))
        {
            entry.AnalysisResult = analysis;
            entry.BoundFingerprint = analysis.SourceFingerprint;
            entry.BoundItemCount = analysis.TotalItems;
        }
    }

    public MimeSourceHandleEntry? GetMimeSourceEntry(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !handle.StartsWith("msrc_"))
            return null;
        return _mimeSourceHandles.TryGetValue(handle, out var entry) ? entry : null;
    }

    public OutputDirHandleEntry? GetOutputDirEntry(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle) || !handle.StartsWith("dir_"))
            return null;
        return _outputDirHandles.TryGetValue(handle, out var entry) ? entry : null;
    }

    public string RegisterEmlxSource(EmlxSourceManifest manifest, string displayPath)
    {
        string handle = "emlx_" + Guid.NewGuid().ToString("N");
        _emlxSourceHandles[handle] = new() { Handle = handle, DisplayPath = displayPath, Manifest = manifest, RegisteredAt = DateTimeOffset.UtcNow };
        Bind(handle);
        return handle;
    }

    public bool IsOwnedBy(string id,AuthenticatedSessionPrincipal actor)=>_owners.TryGetValue(id,out var owner)&&owner.UserId==actor.UserId&&owner.SessionId==actor.SessionId;
    private void Bind(string id){if(_http?.HttpContext?.Items[typeof(AuthenticatedSessionPrincipal)] is AuthenticatedSessionPrincipal actor)_owners[id]=(actor.UserId,actor.SessionId);}

    public EmlxSourceHandleEntry? GetEmlxSourceEntry(string handle) =>
        !string.IsNullOrWhiteSpace(handle) && handle.StartsWith("emlx_") && _emlxSourceHandles.TryGetValue(handle, out var entry) ? entry : null;

    public sealed class EmlxSourceHandleEntry
    {
        public string Handle { get; set; } = string.Empty;
        public string DisplayPath { get; set; } = string.Empty;
        public EmlxSourceManifest Manifest { get; set; } = null!;
        public DateTimeOffset RegisteredAt { get; set; }
    }

    public class SourceHandleEntry
    {
        public string Handle { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTimeOffset RegisteredAt { get; set; }
        public OstAnalysisResult? AnalysisResult { get; set; }
        public string? BoundSha256 { get; set; }
        public int BoundItemCount { get; set; }
    }

    public class TargetHandleEntry
    {
        public string Handle { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTimeOffset RegisteredAt { get; set; }
    }

    public class OutputDirHandleEntry
    {
        public string Handle { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string DirectoryName { get; set; } = string.Empty;
        public DateTimeOffset RegisteredAt { get; set; }
    }

    public class MimeSourceHandleEntry
    {
        public string Handle { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty; // "eml-files", "eml-tree", "mbox"
        public string Dialect { get; set; } = string.Empty;
        public string DisplayPath { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
        public DateTimeOffset RegisteredAt { get; set; }
        public MimeSourceManifest? Manifest { get; set; }
        public MimeAnalysisResult? AnalysisResult { get; set; }
        public string? BoundFingerprint { get; set; }
        public int BoundItemCount { get; set; }
    }
}
