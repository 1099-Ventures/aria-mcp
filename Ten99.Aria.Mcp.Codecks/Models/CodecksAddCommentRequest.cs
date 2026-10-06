namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to create a resolvable (thread) on a card: comment, block, or review.</summary>
internal class CodecksAddCommentRequest
{
	public string CardId { get; set; } = string.Empty;
	/// <summary>Body. Mentions use the form @[userId:&lt;id&gt;].</summary>
	public string Content { get; set; } = string.Empty;
	/// <summary>Resolvable context: "comment" (default), "block", or "review".</summary>
	public string Context { get; set; } = "comment";
}
