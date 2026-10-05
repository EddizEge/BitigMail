using System;
using System.IO;
using System.Text.Json;
using BitigMail.Engine.Archive;
using BitigMail.LocalHost.Security;

namespace BitigMail.LocalHost.Archive;

public sealed class ArchivePlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _plansDir;
    private readonly object _lock = new();
    private readonly TransientResourceOwnershipRegistry? _owners;

    public ArchivePlanStore(string runtimeDir,TransientResourceOwnershipRegistry? owners=null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDir);
        _plansDir = Path.Combine(Path.GetFullPath(runtimeDir), "archive-plans");
        _owners=owners;
        Directory.CreateDirectory(_plansDir);
    }

    public string PlansDirectory => _plansDir;

    public void SavePlan(ArchiveIngestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateId(plan.PlanId);

        string targetPath = Path.Combine(_plansDir, plan.PlanId + ".json");
        string tmpPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        lock (_lock)
        {
            try
            {
                using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(fs, plan, JsonOptions);
                    fs.Flush(true);
                }
                File.Move(tmpPath, targetPath, overwrite: false);
                _owners?.Bind(plan.PreviewId);
            }
            finally
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }
    }

    public ArchiveIngestPlan? GetPlan(string planId)
    {
        if (string.IsNullOrWhiteSpace(planId)) return null;
        ValidateId(planId);

        string targetPath = Path.Combine(_plansDir, planId + ".json");
        lock (_lock)
        {
            if (!File.Exists(targetPath)) return null;

            try
            {
                using var fs = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return JsonSerializer.Deserialize<ArchiveIngestPlan>(fs, JsonOptions);
            }
            catch
            {
                throw new InvalidOperationException("Arşivleme planı okunamadı veya bozuk.");
            }
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
            !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("Geçersiz arşiv plan kimliği.");
        }
    }
}
