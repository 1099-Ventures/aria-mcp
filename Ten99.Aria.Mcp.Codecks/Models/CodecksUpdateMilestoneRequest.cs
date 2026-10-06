namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to update a milestone. Only supplied fields are sent.</summary>
internal class CodecksUpdateMilestoneRequest
{
	public string Id { get; set; } = string.Empty;
	public string? Name { get; set; }
	public string? Date { get; set; }
	public string? StartDate { get; set; }
	public string? Color { get; set; }
	public bool? IsGlobal { get; set; }
	public string[]? ProjectIds { get; set; }
	/// <summary>Milestone grouping labels (the milestone board's manual order columns).</summary>
	public string[]? ManualOrderLabels { get; set; }
}
