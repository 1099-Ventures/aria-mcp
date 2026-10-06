namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to apply a journey (workflow item set) to a card, making it the Hero.</summary>
internal class CodecksApplyJourneyRequest
{
	public string CardId { get; set; } = string.Empty;
	public string[] ItemIds { get; set; } = [];
}
