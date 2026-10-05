namespace BitigMail.LocalHost.Pop;

public sealed record PopAccountPublicDto(string AccountId, string CompanyId, string ProjectId, string DisplayName,
    string Email, string Host, int Port, string TlsMode, string Username, long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, bool AllowUnencryptedConnection = false);
public sealed record CreatePopAccountRequest(string CompanyId, string ProjectId, string DisplayName, string Email,
    string Host, int Port, string TlsMode, string Username, string Password, bool AllowUnencryptedConnection = false);
public sealed record PopSnapshotRequest(string AccountId, string CompanyId, string ProjectId, string OutputDirectory, string IdempotencyKey, bool EnqueueIfBusy = true);
public sealed record PopSnapshotItem(int Sequence, string Uidl, long SizeBytes);
public sealed record PopSnapshotPlan(string PlanId, string AccountId, long AccountVersion, string CompanyId, string ProjectId,
    IReadOnlyList<PopSnapshotItem> Items, string SnapshotSha256, DateTimeOffset CreatedAtUtc);
public sealed record PopOutputItem(int Sequence, string Uidl, string RelativePath, long SizeBytes, string Sha256,
    string? RfcMessageDate, string MetadataQualification);
public sealed record PopSnapshotReport(string Status, string OutputPath, string SnapshotSha256, int PlannedItems,
    int WrittenItems, IReadOnlyList<PopOutputItem> Items, IReadOnlyList<string> Warnings);

internal sealed class PopAccountEnvelope
{
    public int SchemaVersion { get; set; } = 1;
    public required PopAccountPublicDto Account { get; set; }
    public required string ProtectedPasswordBase64 { get; set; }
}
