namespace Ten99.Aria.Mcp.LocalFiles.Models;

internal class SearchFileResponse
{
	public bool Success { get; set; }
	public string? ErrorMessage { get; set; }
	public string FilePath { get; set; } = string.Empty;
	public string SearchPattern { get; set; } = string.Empty;
	public bool IsRegex { get; set; }
	public bool CaseSensitive { get; set; }
	public int MatchCount { get; set; }
	public bool Truncated { get; set; }
	public List<SearchMatch> Matches { get; set; } = new();
}
