using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class ExistenceCheckResult
{
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	[JsonPropertyName("exists")]
	public bool Exists { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;
}