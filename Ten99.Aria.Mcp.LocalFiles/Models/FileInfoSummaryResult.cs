using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class FileInfoSummaryResult
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("fullName")]
	public string FullName { get; set; } = string.Empty;

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("lastWriteTime")]
	public DateTime LastWriteTime { get; set; }
}