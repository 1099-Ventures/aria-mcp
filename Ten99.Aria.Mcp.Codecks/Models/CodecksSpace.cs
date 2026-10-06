using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>
/// One entry in a project's <c>spaces</c> array. Spaces are not a standalone Codecks entity — they live
/// on the project and are written back as a whole array via <c>dispatch/projects/update</c>. The
/// <see cref="Id"/> is a per-project integer. <see cref="Name"/> and <see cref="Icon"/> are sent
/// explicitly, including <c>null</c>, to match the API's expected payload shape.
/// </summary>
internal sealed class CodecksSpace
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("icon")]
	public string? Icon { get; set; }

	[JsonPropertyName("defaultDeckType")]
	public string DefaultDeckType { get; set; } = "mixed";
}
