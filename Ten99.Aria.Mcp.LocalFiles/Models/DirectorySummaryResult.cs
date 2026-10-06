using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class DirectorySummaryResult
{
	[JsonPropertyName("directoryPath")]
	public string DirectoryPath { get; set; } = string.Empty;

	[JsonPropertyName("fileCount")]
	public int FileCount { get; set; }

	[JsonPropertyName("directoryCount")]
	public int DirectoryCount { get; set; }

	[JsonPropertyName("totalSize")]
	public long TotalSize { get; set; }

	[JsonPropertyName("lastModified")]
	public DateTime LastModified { get; set; }

	[JsonPropertyName("fileTypeBreakdown")]
	public Dictionary<string, FileTypeBreakdownResult> FileTypeBreakdown { get; set; } = new();

	[JsonPropertyName("largestFiles")]
	public List<FileInfoSummaryResult> LargestFiles { get; set; } = new();

	[JsonPropertyName("recentFiles")]
	public List<FileInfoSummaryResult> RecentFiles { get; set; } = new();

	[JsonPropertyName("totalSizeFormatted")]
	public string TotalSizeFormatted => FormatBytes(TotalSize);

	private static string FormatBytes(long bytes)
	{
		string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
		int counter = 0;
		decimal number = bytes;
		while (Math.Round(number / 1024) >= 1)
		{
			number /= 1024;
			counter++;
		}
		return $"{number:n1} {suffixes[counter]}";
	}
}