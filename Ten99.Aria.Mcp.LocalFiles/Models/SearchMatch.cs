namespace Ten99.Aria.Mcp.LocalFiles.Models;

internal class SearchMatch
{
	/// <summary>
	/// Byte offset from start of file - compatible with read_file chunk_offset parameter
	/// </summary>
	public long ByteOffset { get; set; }

	/// <summary>
	/// 1-based line number - compatible with modify_file_lines startLine parameter
	/// </summary>
	public int LineNumber { get; set; }

	/// <summary>
	/// 0-based character position within the line
	/// </summary>
	public int CharacterPosition { get; set; }

	/// <summary>
	/// Preview of the match with surrounding context (~80-100 chars)
	/// </summary>
	public string MatchPreview { get; set; } = string.Empty;

	/// <summary>
	/// The actual matched text
	/// </summary>
	public string MatchedText { get; set; } = string.Empty;
}
