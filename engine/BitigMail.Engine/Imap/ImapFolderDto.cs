using System.Collections.Generic;

namespace BitigMail.Engine.Imap;

/// <summary>
/// Represents a folder retrieved from an IMAP server with exact path, delimiter, selectable status and message count.
/// </summary>
public sealed record ImapFolderDto
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string Delimiter { get; init; }
    public required bool IsSelectable { get; init; }
    public int? MessageCount { get; init; }
    public int? UnreadCount { get; init; }
    public required bool CountValid { get; init; }
    public string? StatusError { get; init; }
}

/// <summary>
/// Response payload for listing folders of an IMAP account.
/// </summary>
public sealed record ImapFolderListResponse
{
    public required string AccountId { get; init; }
    public required IReadOnlyList<ImapFolderDto> Folders { get; init; }
}
