namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to edit a comment entry.</summary>
internal class CodecksEditCommentRequest
{
	public string EntryId { get; set; } = string.Empty;
	public string Content { get; set; } = string.Empty;
}
