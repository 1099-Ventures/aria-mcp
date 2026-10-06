using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class BatchOperationSummaryResult
{
	[JsonPropertyName("totalOperations")]
	public int TotalOperations { get; set; }

	[JsonPropertyName("successfulOperations")]
	public int SuccessfulOperations { get; set; }

	[JsonPropertyName("failedOperations")]
	public int FailedOperations { get; set; }

	[JsonPropertyName("results")]
	public List<FileOperationResult> Results { get; set; } = new();

	[JsonPropertyName("startTime")]
	public DateTime StartTime { get; set; } = DateTime.Now;

	[JsonPropertyName("endTime")]
	public DateTime EndTime { get; set; } = DateTime.Now;

	[JsonPropertyName("duration")]
	public TimeSpan Duration => EndTime - StartTime;

	[JsonPropertyName("hasErrors")]
	public bool HasErrors => FailedOperations > 0;
}