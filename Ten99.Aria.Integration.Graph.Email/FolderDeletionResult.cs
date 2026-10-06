namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>The outcome of a soft folder-delete: what was removed and where it came from, so the caller
/// can log it or reverse it (move it back under <see cref="PreviousParentId"/>).</summary>
public record FolderDeletionResult
{
	/// <summary>The folder's id after the soft-delete move (under Deleted Items / Trash).</summary>
	public required string DeletedFolderId { get; init; }

	/// <summary>The parent the folder was under before deletion (null if it was top-level). The undo target.</summary>
	public string? PreviousParentId { get; init; }

	/// <summary>The folder's display name.</summary>
	public required string Name { get; init; }
}
