using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class FileOperationResult
{
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("sourcePath")]
	public string SourcePath { get; set; } = string.Empty;

	[JsonPropertyName("destinationPath")]
	public string DestinationPath { get; set; } = string.Empty;

	[JsonPropertyName("error")]
	public string? Error { get; set; }

	[JsonPropertyName("timestamp")]
	public DateTime Timestamp { get; set; } = DateTime.Now;

	[JsonPropertyName("fileSize")]
	public long? FileSize { get; set; }
}