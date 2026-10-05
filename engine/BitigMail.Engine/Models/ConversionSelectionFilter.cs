using System.Collections.Generic;

namespace BitigMail.Engine.Models;

public class ConversionSelectionFilter
{
    private List<string> _folderIds = new();
    private List<SelectedFolderSnapshot> _selectedFolders = new();

    public List<string> FolderIds
    {
        get => _folderIds ??= new();
        set => _folderIds = value ?? new();
    }

    public List<SelectedFolderSnapshot> SelectedFolders
    {
        get => _selectedFolders ??= new();
        set => _selectedFolders = value ?? new();
    }

    public string? StartDate { get; set; } // ISO YYYY-MM-DD
    public string? EndDate { get; set; }   // ISO YYYY-MM-DD
    public string TimeZone { get; set; } = "Europe/Istanbul (UTC+03:00)";
    public string DatePolicy { get; set; } = "SubmissionDateThenDeliveryDate_UtcPlus3_Inclusive";
}

public class SelectedFolderSnapshot
{
    public string FolderId { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
