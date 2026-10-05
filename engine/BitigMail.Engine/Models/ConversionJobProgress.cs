namespace BitigMail.Engine.Models;

public class ConversionJobProgress
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "queued"; // "queued", "analyzing", "converting", "verifying", "completed", "failed", "interrupted"
    public string Stage { get; set; } = "Sırada";
    public int ItemsRead { get; set; }
    public int ItemsWritten { get; set; }
    public int FailedItems { get; set; }
    public int TotalItems { get; set; }
    public string CurrentFolder { get; set; } = string.Empty;
    public string? ProgressPhase { get; set; }
    public int? PhaseCompleted { get; set; }
    public int? PhaseTotal { get; set; }
    public int PercentComplete { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
