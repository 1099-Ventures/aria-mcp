namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to add a tag (label) to a Codecks project.</summary>
internal class CodecksAddTagRequest
{
	public string ProjectId { get; set; } = string.Empty;
	public string Tag { get; set; } = string.Empty;
}
