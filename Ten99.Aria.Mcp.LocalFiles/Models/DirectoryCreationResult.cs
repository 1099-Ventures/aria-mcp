using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class DirectoryCreationResult
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	[JsonPropertyName("alreadyExisted")]
	public bool AlreadyExisted { get; set; }

	[JsonPropertyName("error")]
	public string? Error { get; set; }

	[JsonPropertyName("timestamp")]
	public DateTime Timestamp { get; set; } = DateTime.Now;
}