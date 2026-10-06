namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to set a journey deck's named step zones (in order).</summary>
internal class CodecksSetStepZonesRequest
{
	public string DeckId { get; set; } = string.Empty;
	public string[] Zones { get; set; } = [];
}
