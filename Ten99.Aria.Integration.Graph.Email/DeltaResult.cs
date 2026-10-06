namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>Result of a folder-scoped delta sync — the changes since a token, plus the next cursor.</summary>
public record DeltaResult
{
	/// <summary>Messages added or updated since the token (lean projection).</summary>
	public IReadOnlyList<EmailMessage> Changed { get; init; } = [];

	/// <summary>Ids of messages removed from the folder since the token (deleted, or moved out).</summary>
	public IReadOnlyList<string> RemovedIds { get; init; } = [];

	/// <summary>Opaque cursor to pass back next time — a Graph nextLink while still draining the current
	/// sync, otherwise the deltaLink to persist for the next incremental sync. Null only on empty results.</summary>
	public string? NextToken { get; init; }

	/// <summary>True while more pages of the current sync remain — pass <see cref="NextToken"/> back to continue.</summary>
	public bool More { get; init; }

	/// <summary>True when the supplied token was stale (Graph 410) and the sync restarted from a fresh
	/// baseline. The caller should treat this as a normal resync, not an error.</summary>
	public bool Resynced { get; init; }
}
