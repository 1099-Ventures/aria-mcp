namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to create a milestone. accountId is resolved by the operation from the token.</summary>
internal class CodecksCreateMilestoneRequest
{
	public string Name { get; set; } = string.Empty;
	/// <summary>Target date, YYYY-MM-DD.</summary>
	public string Date { get; set; } = string.Empty;
	/// <summary>Colour token (e.g. "blue", "pink"). The API validates against its palette.</summary>
	public string Color { get; set; } = string.Empty;
	public string[] ProjectIds { get; set; } = [];
	/// <summary>Optional start date, YYYY-MM-DD.</summary>
	public string? StartDate { get; set; }
	public bool IsGlobal { get; set; }
}
