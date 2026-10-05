using System;
using System.Collections.Generic;

namespace BitigMail.Engine.Models;

public class SplitManifest
{
    public string JobId { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string SplitMode { get; set; } = string.Empty;
    public long? SizeCapBytes { get; set; }
    public int TotalParts { get; set; }
    public int TotalMessagesWritten { get; set; }
    public int TotalAttachmentsVerified { get; set; }
    public int TotalCidVerified { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public ClientProjectContext ClientContext { get; set; } = new();
    public List<SplitPartReport> Parts { get; set; } = new();
}
