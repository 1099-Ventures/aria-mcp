namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>A mail folder with its item counts. Flat — nesting is expressed via <see cref="ParentFolderId"/>.</summary>
public record MailFolderInfo
{
	public required string Id { get; init; }
	public required string DisplayName { get; init; }
	public string? ParentFolderId { get; init; }
	public int UnreadItemCount { get; init; }
	public int TotalItemCount { get; init; }
	public int ChildFolderCount { get; init; }
}
