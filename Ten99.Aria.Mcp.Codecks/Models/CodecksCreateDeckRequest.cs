using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.Codecks.Models;

internal class CodecksCreateDeckRequest
{
	/// <summary>Required: the project the deck belongs to.</summary>
	[JsonPropertyName("projectId")]
	public string ProjectId { get; set; } = string.Empty;

	/// <summary>Required: the deck title.</summary>
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	/// <summary>Optional: deck description.</summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; set; }

	/// <summary>Optional: the project space to place the deck in. Omitted => the project's default space.</summary>
	[JsonPropertyName("spaceId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? SpaceId { get; set; }
}
