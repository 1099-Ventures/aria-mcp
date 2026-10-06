using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class FileTypeBreakdownResult
{
	[JsonPropertyName("count")]
	public int Count { get; set; }

	[JsonPropertyName("totalSize")]
	public long TotalSize { get; set; }
}