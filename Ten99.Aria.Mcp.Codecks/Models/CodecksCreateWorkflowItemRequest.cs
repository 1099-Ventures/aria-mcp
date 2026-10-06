namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to add a workflow (journey) item to a deck. Payload is built in the operation.</summary>
internal class CodecksCreateWorkflowItemRequest
{
	public string DeckId { get; set; } = string.Empty;
	public string Content { get; set; } = string.Empty;
	public string? AssigneeId { get; set; }
	public int? Effort { get; set; }
	public string? Priority { get; set; }
	/// <summary>Step-zone label this item belongs to (matches a deck workflowItemOrderLabels entry).</summary>
	public string? Label { get; set; }
	/// <summary>Deck that applied cards land in when the journey is applied.</summary>
	public string? TargetDeckId { get; set; }
}
