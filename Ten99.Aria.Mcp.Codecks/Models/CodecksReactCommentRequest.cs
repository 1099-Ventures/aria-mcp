namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to add an emoji reaction to a comment entry.</summary>
internal class CodecksReactCommentRequest
{
	public string EntryId { get; set; } = string.Empty;
	/// <summary>The emoji, e.g. "👍".</summary>
	public string Emoji { get; set; } = string.Empty;
}
