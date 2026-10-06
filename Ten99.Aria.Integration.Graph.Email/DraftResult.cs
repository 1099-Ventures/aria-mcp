namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>The outcome of creating a draft (e.g. a reply draft): where it landed, ready to review or to
/// send. Composing a draft is ungated; sending it is a separate capability governed by the operator's send
/// flag (off by default) — compose and send are two mechanisms behind one gate, not one forbidden path.</summary>
public record DraftResult
{
	public required string Id { get; init; }
	public required string Subject { get; init; }
	/// <summary>The folder the draft was saved in (Drafts).</summary>
	public string? ParentFolderId { get; init; }
	/// <summary>Deep link to open the draft, where the backend exposes one.</summary>
	public string? WebLink { get; init; }
}
