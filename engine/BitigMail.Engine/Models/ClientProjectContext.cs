using System.Text.Json.Serialization;

namespace BitigMail.Engine.Models;

public class ClientProjectContext
{
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string? ClientIdLegacy
    {
        get => null;
        set { if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(CompanyId)) CompanyId = value; }
    }

    [JsonPropertyName("clientName")]
    public string? ClientNameLegacy
    {
        get => null;
        set { if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(CompanyName)) CompanyName = value; }
    }
}
