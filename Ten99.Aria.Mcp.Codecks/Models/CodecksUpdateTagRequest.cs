namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to edit a project tag. Only supplied fields are sent.</summary>
internal class CodecksUpdateTagRequest
{
	public string Id { get; set; } = string.Empty;
	/// <summary>The tag label/value.</summary>
	public string? Tag { get; set; }
	public string? Description { get; set; }
	/// <summary>Hex colour (e.g. "#C13642").</summary>
	public string? Color { get; set; }
	public string? Emoji { get; set; }
}
