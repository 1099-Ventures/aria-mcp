namespace Ten99.Aria.Mcp.LocalFiles.Models;

internal class ModifyLinesRequest
{
	public string FilePath { get; set; } = string.Empty;
	public FileModification[] Operations { get; set; } = [];
	public bool KeepBackup { get; set; } = false;
}