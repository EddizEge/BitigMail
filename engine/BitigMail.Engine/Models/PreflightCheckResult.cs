namespace BitigMail.Engine.Models;

public class PreflightCheckResult
{
    public bool CanConvert { get; set; } = true;
    public bool HasTrialBlocker { get; set; }
    public string? TrialBlockerReason { get; set; }
    public List<string> Blockers { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public long EstimatedPstSizeBytes { get; set; }
    public long AvailableDiskSizeBytes { get; set; }
    public bool? DiskCapacityAvailable { get; set; }
    public long? AvailableDiskBytes { get; set; }
    public string? DiskCapacityError { get; set; }
    public long? EstimatedRequiredBytes { get; set; }
    public string? EstimateBasis { get; set; }
}
