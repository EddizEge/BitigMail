using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BitigMail.Engine.Imap.Transfer;

namespace BitigMail.LocalHost.Imap.Transfer;

/// <summary>Durable snapshots. A damaged record is never equivalent to an absent record.</summary>
public sealed class ImapTransferJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _plansDir;
    private readonly string _journalsDir;
    private readonly object _lock = new();
    private readonly HashSet<string> _observedJournals = new(StringComparer.Ordinal);

    internal Action<string>? OnBeforeAtomicWrite { get; set; }
    internal Action<ImapTransferJournalEntry>? OnBeforeWriteAppendIntent { get; set; }
    internal Action<ImapTransferJournalEntry>? OnBeforeWriteVerified { get; set; }

    public ImapTransferJournal(string runtimeDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDir);
        _plansDir = Path.Combine(Path.GetFullPath(runtimeDir), "imap-transfers", "plans");
        _journalsDir = Path.Combine(Path.GetFullPath(runtimeDir), "imap-transfers", "journals");
        Directory.CreateDirectory(_plansDir);
        Directory.CreateDirectory(_journalsDir);
    }

    public string PlansDirectory => _plansDir;
    public string JournalsDirectory => _journalsDir;

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128 ||
            id.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
            throw new ArgumentException("Geçersiz aktarım kaydı kimliği.");
    }

    private static InvalidOperationException DamagedRecord() =>
        new("Aktarım kaydı okunamadı veya bütünlüğü bozuldu. Yinelenen ileti riskini önlemek için işlem durduruldu.");

    // Read durable bytes so callers cannot publish an unflushed transition by mutating cached objects.
    private static T? Read<T>(string path) where T : class
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? throw DamagedRecord();
        }
        catch (FileNotFoundException) { return null; }
        catch (JsonException) { throw DamagedRecord(); }
        catch (IOException) { throw DamagedRecord(); }
        catch (UnauthorizedAccessException) { throw DamagedRecord(); }
    }

    private static void ValidatePlan(ImapTransferPlan plan, string id)
    {
        _ = ImapAdvancedFilterPolicy.ValidateFrozen(plan);
        if (plan.PlanId != id || plan.PreviewId != id || string.IsNullOrWhiteSpace(plan.CompanyId) ||
            string.IsNullOrWhiteSpace(plan.ProjectId) || string.IsNullOrWhiteSpace(plan.SourceAccountId) ||
            string.IsNullOrWhiteSpace(plan.TargetAccountId) || plan.Items == null ||
            plan.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.ItemId) || string.IsNullOrWhiteSpace(i.SourceFolder) ||
                string.IsNullOrWhiteSpace(i.TargetFolder) || string.IsNullOrWhiteSpace(i.SourceSha256)) ||
            plan.Items.Select(i => i.ItemId).Distinct(StringComparer.Ordinal).Count() != plan.Items.Count)
            throw DamagedRecord();
    }

    public void SavePlan(ImapTransferPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateId(plan.PlanId);
        ValidatePlan(plan, plan.PlanId);
        lock (_lock)
        {
            AtomicWrite(Path.Combine(_plansDir, plan.PlanId + ".json"), JsonSerializer.SerializeToUtf8Bytes(plan, JsonOptions), false);
        }
    }

    public ImapTransferPlan? GetPlan(string planId)
    {
        ValidateId(planId);
        lock (_lock)
        {
            var plan = Read<ImapTransferPlan>(Path.Combine(_plansDir, planId + ".json"));
            if (plan != null) ValidatePlan(plan, planId);
            return plan;
        }
    }

    public ImapTransferJournalState InitializeJournal(string jobId, ImapTransferPlan plan)
    {
        ValidateId(jobId);
        ArgumentNullException.ThrowIfNull(plan);
        lock (_lock)
        {
            if (_observedJournals.Contains(jobId)) throw DamagedRecord();
            var frozen = GetPlan(plan.PlanId) ?? throw DamagedRecord();
            var state = new ImapTransferJournalState { JobId = jobId, PlanId = frozen.PlanId };
            foreach (var item in frozen.Items)
                state.Entries.Add(item.ItemId, new ImapTransferJournalEntry
                {
                    ItemId = item.ItemId, SourceFolder = item.SourceFolder, SourceUid = item.SourceUid,
                    SourceUidValidity = item.SourceUidValidity, TargetFolder = item.TargetFolder,
                    ExpectedSha256 = item.SourceSha256
                });
            AtomicWrite(Path.Combine(_journalsDir, jobId + ".json"), JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), false);
            _observedJournals.Add(jobId);
            return state;
        }
    }

    public ImapTransferJournalState? GetJournal(string jobId)
    {
        ValidateId(jobId);
        lock (_lock)
        {
            var state = Read<ImapTransferJournalState>(Path.Combine(_journalsDir, jobId + ".json"));
            if (state == null)
            {
                if (_observedJournals.Contains(jobId)) throw DamagedRecord();
                return null;
            }
            _observedJournals.Add(jobId);
            if (state.JobId != jobId || string.IsNullOrEmpty(state.PlanId) || state.Entries == null) throw DamagedRecord();
            var plan = GetPlan(state.PlanId) ?? throw DamagedRecord();
            if (state.Entries.Count != plan.Items.Count) throw DamagedRecord();
            foreach (var item in plan.Items)
            {
                if (!state.Entries.TryGetValue(item.ItemId, out var entry) || entry == null || entry.ItemId != item.ItemId ||
                    entry.SourceFolder != item.SourceFolder || entry.SourceUid != item.SourceUid || entry.SourceUidValidity != item.SourceUidValidity ||
                    entry.TargetFolder != item.TargetFolder || entry.ExpectedSha256 != item.SourceSha256 || !Enum.IsDefined(entry.Status))
                    throw DamagedRecord();
            }
            return state;
        }
    }

    public void UpdateEntry(string jobId, ImapTransferJournalEntry entry)
    {
        ValidateId(jobId);
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            var state = GetJournal(jobId) ?? throw DamagedRecord();
            if (!state.Entries.TryGetValue(entry.ItemId, out var old) ||
                old.SourceFolder != entry.SourceFolder || old.SourceUid != entry.SourceUid || old.SourceUidValidity != entry.SourceUidValidity ||
                old.TargetFolder != entry.TargetFolder || old.ExpectedSha256 != entry.ExpectedSha256) throw DamagedRecord();
            if (entry.Status == ImapTransferItemStatus.AppendIntent) OnBeforeWriteAppendIntent?.Invoke(entry);
            if (entry.Status == ImapTransferItemStatus.Verified) OnBeforeWriteVerified?.Invoke(entry);
            state.Entries[entry.ItemId] = entry;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AtomicWrite(Path.Combine(_journalsDir, jobId + ".json"), JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), true);
        }
    }

    private void AtomicWrite(string targetPath, byte[] data, bool overwrite)
    {
        OnBeforeAtomicWrite?.Invoke(targetPath);
        string tmpPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(true);
            }
            File.Move(tmpPath, targetPath, overwrite);
        }
        finally
        {
            try { File.Delete(tmpPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
