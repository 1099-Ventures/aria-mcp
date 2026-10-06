using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.Codecks.Models;

internal class CodecksCreateCardRequest
{
	/// <summary>
	/// Required: The ID of the deck where the card will be created
	/// </summary>
	[JsonPropertyName("deckId")]
	public string DeckId { get; set; } = string.Empty;

	/// <summary>
	/// Required: The card content (supports Markdown formatting)
	/// </summary>
	[JsonPropertyName("content")]
	public string Content { get; set; } = string.Empty;

	/// <summary>
	/// Optional: Card effort/size estimate
	/// </summary>
	[JsonPropertyName("effort")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Effort { get; set; }

	/// <summary>
	/// Optional: Card priority (e.g., "high", "medium", "low")
	/// </summary>
	[JsonPropertyName("priority")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Priority { get; set; }

	/// <summary>
	/// Optional: User ID to assign the card to
	/// </summary>
	[JsonPropertyName("assigneeId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? AssigneeId { get; set; }

	/// <summary>
	/// Optional: Due date in ISO 8601 format (e.g., "2024-12-31T23:59:59Z")
	/// </summary>
	[JsonPropertyName("dueDate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DueDate { get; set; }

	/// <summary>
	/// Optional: Whether the creator should be subscribed to the card
	/// </summary>
	[JsonPropertyName("subscribeCreator")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? SubscribeCreator { get; set; }

	/// <summary>
	/// Optional: Whether to put the card in the queue
	/// </summary>
	[JsonPropertyName("putInQueue")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? PutInQueue { get; set; }

	/// <summary>
	/// Optional: Whether to add the card as a bookmark
	/// </summary>
	[JsonPropertyName("addAsBookmark")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? AddAsBookmark { get; set; }

	/// <summary>
	/// Optional: Whether the card is a document
	/// </summary>
	[JsonPropertyName("isDoc")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IsDoc { get; set; }

	/// <summary>
	/// Optional: the card's tags as projectTag ids (from tag_list).
	/// </summary>
	[JsonPropertyName("masterTags")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[]? MasterTags { get; set; }

	/// <summary>
	/// Optional: assign the card to a milestone.
	/// </summary>
	[JsonPropertyName("milestoneId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? MilestoneId { get; set; }

	/// <summary>
	/// Optional: assign the card to a run (sprint).
	/// </summary>
	[JsonPropertyName("sprintId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SprintId { get; set; }
}
