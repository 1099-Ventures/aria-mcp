using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class BatchMoveFilesRequest
{
	[JsonPropertyName("source")]
	public string Source { get; set; } = string.Empty;

	[JsonPropertyName("destination")]
	public string Destination { get; set; } = string.Empty;
}