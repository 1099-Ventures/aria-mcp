using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class DirectoryInfoResult
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;

	[JsonPropertyName("fileCount")]
	public int FileCount { get; set; }

	[JsonPropertyName("directoryCount")]
	public int DirectoryCount { get; set; }

	[JsonPropertyName("totalItems")]
	public int TotalItems { get; set; }

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("modifiedAt")]
	public DateTime ModifiedAt { get; set; }

	[JsonPropertyName("accessedAt")]
	public DateTime AccessedAt { get; set; }

	[JsonPropertyName("attributes")]
	public string Attributes { get; set; } = string.Empty;

	public DirectoryInfoResult() { }

	public DirectoryInfoResult(DirectoryInfo directoryInfo)
	{
		Name = directoryInfo.Name;
		Path = directoryInfo.FullName;
		Type = "directory";
		FileCount = directoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly).Length;
		DirectoryCount = directoryInfo.GetDirectories("*", SearchOption.TopDirectoryOnly).Length;
		TotalItems = FileCount + DirectoryCount;
		CreatedAt = directoryInfo.CreationTime;
		ModifiedAt = directoryInfo.LastWriteTime;
		AccessedAt = directoryInfo.LastAccessTime;
		Attributes = directoryInfo.Attributes.ToString();
	}
}