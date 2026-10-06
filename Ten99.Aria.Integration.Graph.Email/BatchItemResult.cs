namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>Per-item outcome of a batched write — so a partial failure reports which ids failed and why,
/// rather than all-or-nothing.</summary>
public record BatchItemResult
{
	public required string MessageId { get; init; }
	public bool Success { get; init; }

	/// <summary>Error message when <see cref="Success"/> is false; null on success.</summary>
	public string? Error { get; init; }

	/// <summary>New message id after a move — Graph reassigns it, so the caller needs this to keep its
	/// handle (and to undo). Move/delete only.</summary>
	public string? NewId { get; init; }

	/// <summary>The folder the message was in before a move — the other half of the undo record. Move/delete only.</summary>
	public string? FromFolderId { get; init; }
}
