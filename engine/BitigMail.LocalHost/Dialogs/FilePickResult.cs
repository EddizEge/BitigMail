namespace BitigMail.LocalHost.Dialogs;

public class FilePickResult
{
    public bool Cancelled { get; set; }
    public string? Handle { get; set; }
    public string? FileName { get; set; }
    public string? DisplayPath { get; set; }
    public long SizeBytes { get; set; }
    public string? Error { get; set; }
}
