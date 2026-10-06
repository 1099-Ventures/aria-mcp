using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.Codecks.Models;

internal class CodecksUpdateCardRequest
{
	/// <summary>
	/// Required: The ID of the card to update
	/// </summary>
	[JsonPropertyName("id")]
	public string CardId { get; set; } = string.Empty;

	/// <summary>
	/// Optional: Card title
	/// </summary>
	[JsonPropertyName("title")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Title { get; set; }

	/// <summary>
	/// Optional: The card content (supports Markdown formatting)
	/// </summary>
	[JsonPropertyName("content")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Content { get; set; }

	/// <summary>
	/// Optional: Card effort/size estimate (must be Fibonacci number)
	/// </summary>
	[JsonPropertyName("effort")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Effort { get; set; }

	/// <summary>
	/// Optional: Card priority ('a' = high, 'b' = medium, 'c' = low)
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
	/// Optional: Move card to a different deck
	/// </summary>
	[JsonPropertyName("deckId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DeckId { get; set; }

	/// <summary>
	/// Optional: Assign card to a milestone
	/// </summary>
	[JsonPropertyName("milestoneId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? MilestoneId { get; set; }

	/// <summary>
	/// Optional: Assign card to a sprint
	/// </summary>
	[JsonPropertyName("sprintId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? SprintId { get; set; }

	/// <summary>
	/// Optional: Card visibility ("normal" or "archived" for soft delete)
	/// </summary>
	[JsonPropertyName("visibility")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Visibility { get; set; }

	/// <summary>
	/// Optional: Card status
	/// </summary>
	[JsonPropertyName("status")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Status { get; set; }

	/// <summary>
	/// Optional: the card's tags as projectTag ids (from tag_list). Replaces the card's master tags.
	/// </summary>
	[JsonPropertyName("masterTags")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[]? MasterTags { get; set; }
}
