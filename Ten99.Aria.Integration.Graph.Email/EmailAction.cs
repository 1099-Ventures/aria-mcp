namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>
/// Produced by the Email Tenant Agent's LLM classification.
/// Consumed by the Productivity Agent to create/update tasks.
/// </summary>
public record EmailAction
{
	public required string SourceMessageId { get; init; }
	public required string SourceTenant { get; init; }
	public required string SourceMailbox { get; init; }

	public required string SuggestedTitle { get; init; }
	public string? SuggestedDescription { get; init; }
	public required EmailActionType ActionType { get; init; }
	public required EmailPriority Priority { get; init; }
	public required string Organization { get; init; }
	public string? ProjectOrClient { get; init; }

	public required string FromAddress { get; init; }
	public required string FromName { get; init; }
	public required string OriginalSubject { get; init; }
	public required DateTimeOffset ReceivedDateTime { get; init; }
	public string? WebLink { get; init; }
	public DateTimeOffset? SuggestedDueDate { get; init; }

	public string? ClassificationReasoning { get; init; }
	public required DateTimeOffset ProcessedAt { get; init; }
}

public enum EmailActionType
{
	CreateTask,
	NeedsReply,
	Informational,
	Ignore
}

public enum EmailPriority
{
	Critical,
	High,
	Normal,
	Low
}
