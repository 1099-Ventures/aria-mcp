namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to close a resolvable (comment / block / review), optionally marking the card done.</summary>
internal class CodecksCloseResolvableRequest
{
	public string ResolvableId { get; set; } = string.Empty;
	/// <summary>When closing a review, also mark the card done.</summary>
	public bool? MarkCardDone { get; set; }
}
