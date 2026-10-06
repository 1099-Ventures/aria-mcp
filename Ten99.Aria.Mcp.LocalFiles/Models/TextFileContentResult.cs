using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class TextFileContentResult
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("content")]
	public string Content { get; set; } = string.Empty;
}