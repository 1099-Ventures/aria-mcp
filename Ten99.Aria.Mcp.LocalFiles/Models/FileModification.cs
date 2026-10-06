namespace Ten99.Aria.Mcp.LocalFiles.Models;

public class FileModification
{
	public string Operation { get; set; } = string.Empty; // "insert" | "delete" | "replace"
	public int StartLine { get; set; }                    // 1-based line number
	public int? LineCount { get; set; }                   // null for insert, required for delete/replace
	public string[]? NewContent { get; set; }             // null for delete, required for insert/replace
}