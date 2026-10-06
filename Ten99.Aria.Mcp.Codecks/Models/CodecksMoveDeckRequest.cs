namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to move a deck into a (target) space, optionally positioned after a sibling deck.</summary>
internal class CodecksMoveDeckRequest
{
	public string DeckId { get; set; } = string.Empty;
	public string TargetProjectId { get; set; } = string.Empty;
	public int TargetSpaceId { get; set; }
	/// <summary>Place the deck after this sibling deck in the target space. Null = default placement.</summary>
	public string? AfterDeckId { get; set; }
}
