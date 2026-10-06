namespace Ten99.Aria.Mcp.LocalFiles.Models;

internal class ModifyLinesResponse
{
	public bool Success { get; set; }
	public string? ErrorMessage { get; set; }
	public string? WarningMessage { get; set; }
	public string? BackupPath { get; set; }
	public int TotalLinesProcessed { get; set; }
}