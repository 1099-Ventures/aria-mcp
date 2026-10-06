using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class FileInfoResult
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;

	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;

	[JsonPropertyName("size")]
	public long Size { get; set; }

	[JsonPropertyName("extension")]
	public string Extension { get; set; } = string.Empty;

	[JsonPropertyName("createdAt")]
	public DateTime CreatedAt { get; set; }

	[JsonPropertyName("modifiedAt")]
	public DateTime ModifiedAt { get; set; }

	[JsonPropertyName("accessedAt")]
	public DateTime AccessedAt { get; set; }

	[JsonPropertyName("isReadOnly")]
	public bool IsReadOnly { get; set; }

	[JsonPropertyName("attributes")]
	public string Attributes { get; set; } = string.Empty;

	public FileInfoResult() { }

	public FileInfoResult(FileInfo fileInfo)
	{
		Name = fileInfo.Name;
		Path = fileInfo.FullName;
		Type = "file";
		Size = fileInfo.Length;
		Extension = fileInfo.Extension;
		CreatedAt = fileInfo.CreationTime;
		ModifiedAt = fileInfo.LastWriteTime;
		AccessedAt = fileInfo.LastAccessTime;
		IsReadOnly = fileInfo.IsReadOnly;
		Attributes = fileInfo.Attributes.ToString();
	}
}