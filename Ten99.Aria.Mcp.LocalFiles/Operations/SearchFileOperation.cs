using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class SearchFileOperation : FileOperationBase<string, string, bool, bool, int?>
{
	public override async Task<CallToolResult> ExecuteAsync(
		string filePath,
		string pattern,
		bool isRegex,
		bool caseSensitive,
		int? maxResults,
		CancellationToken cancellationToken)
	{
		try
		{
			// Validate inputs
			if (string.IsNullOrWhiteSpace(filePath))
				return CreateErrorResult("File path is required");

			if (string.IsNullOrWhiteSpace(pattern))
				return CreateErrorResult("Search pattern is required");

			if (!File.Exists(filePath))
				return CreateErrorResult($"File not found: {filePath}");

			var fileInfo = new FileInfo(filePath);
			if (!IsValidFile(filePath, fileInfo, forWrite: false))
				return CreateErrorResult($"Invalid file for search: {filePath}");

			// Perform search
			var response = await SearchFileAsync(filePath, pattern, isRegex, caseSensitive, maxResults ?? int.MaxValue, cancellationToken);

			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(response, JsonOptions) }]
			};
		}
		catch (Exception ex)
		{
			return CreateErrorResult($"Search failed: {ex.Message}");
		}
	}

	private static async Task<SearchFileResponse> SearchFileAsync(
		string filePath,
		string pattern,
		bool isRegex,
		bool caseSensitive,
		int maxResults,
		CancellationToken cancellationToken)
	{
		var response = new SearchFileResponse
		{
			Success = true,
			FilePath = filePath,
			SearchPattern = pattern,
			IsRegex = isRegex,
			CaseSensitive = caseSensitive
		};

		try
		{
			// Validate regex pattern if needed
			Regex? regex = null;
			if (isRegex)
			{
				try
				{
					var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
					regex = new Regex(pattern, options | RegexOptions.Compiled, TimeSpan.FromSeconds(5));
				}
				catch (Exception ex)
				{
					response.Success = false;
					response.ErrorMessage = $"Invalid regex pattern: {ex.Message}";
					return response;
				}
			}

			// Open file with shared read access
			await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var reader = new StreamReader(fileStream, Encoding.UTF8);

			long currentByteOffset = 0;
			var lineNumber = 0;
			var encoding = Encoding.UTF8;

			while (response.MatchCount < maxResults)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var line = await reader.ReadLineAsync(cancellationToken);
				if (line == null)
					break;  // end of stream

				lineNumber++;

				// Search for matches in this line
				var matches = FindMatchesInLine(line, pattern, regex, caseSensitive, currentByteOffset, lineNumber);

				foreach (var match in matches)
				{
					if (response.MatchCount >= maxResults)
					{
						response.Truncated = true;
						break;
					}

					response.Matches.Add(match);
					response.MatchCount++;
				}

				// Update byte offset for next line
				// Account for the line content + line ending
				var lineBytes = encoding.GetByteCount(line);
				var lineEndingBytes = encoding.GetByteCount(Environment.NewLine);
				currentByteOffset += lineBytes + lineEndingBytes;
			}

			return response;
		}
		catch (Exception ex)
		{
			response.Success = false;
			response.ErrorMessage = ex.Message;
			return response;
		}
	}

	private static List<SearchMatch> FindMatchesInLine(
		string line,
		string pattern,
		Regex? regex,
		bool caseSensitive,
		long lineStartByteOffset,
		int lineNumber)
	{
		var matches = new List<SearchMatch>();
		var encoding = Encoding.UTF8;

		if (regex != null)
		{
			// Regex matching
			var regexMatches = regex.Matches(line);
			foreach (Match match in regexMatches)
			{
				var charPosition = match.Index;
				var byteOffset = lineStartByteOffset + encoding.GetByteCount(line.Substring(0, charPosition));

				matches.Add(new SearchMatch
				{
					ByteOffset = byteOffset,
					LineNumber = lineNumber,
					CharacterPosition = charPosition,
					MatchedText = match.Value,
					MatchPreview = ExtractMatchPreview(line, charPosition, match.Length)
				});
			}
		}
		else
		{
			// Literal text matching
			var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
			int searchStart = 0;

			while (searchStart < line.Length)
			{
				int index = line.IndexOf(pattern, searchStart, comparison);
				if (index == -1)
					break;

				var byteOffset = lineStartByteOffset + encoding.GetByteCount(line.Substring(0, index));

				matches.Add(new SearchMatch
				{
					ByteOffset = byteOffset,
					LineNumber = lineNumber,
					CharacterPosition = index,
					MatchedText = line.Substring(index, pattern.Length),
					MatchPreview = ExtractMatchPreview(line, index, pattern.Length)
				});

				searchStart = index + pattern.Length;
			}
		}

		return matches;
	}

	private static string ExtractMatchPreview(string line, int matchIndex, int matchLength)
	{
		const int contextChars = 40; // Characters before and after match
		const int maxPreviewLength = 100;

		int start = Math.Max(0, matchIndex - contextChars);
		int end = Math.Min(line.Length, matchIndex + matchLength + contextChars);

		var preview = line.Substring(start, end - start);

		// Add ellipsis if truncated
		if (start > 0)
			preview = "..." + preview;
		if (end < line.Length)
			preview = preview + "...";

		// Ensure we don't exceed max preview length
		if (preview.Length <= maxPreviewLength) return preview;
		var trimAmount = preview.Length - maxPreviewLength;
		// Trim from the beginning
		preview = start > 0 ? string.Concat("...", preview.AsSpan(trimAmount + 3)) :
			// Trim from the end
			string.Concat(preview.AsSpan(0, maxPreviewLength - 3), "...");

		return preview;
	}

	private static CallToolResult CreateErrorResult(string message)
	{
		var response = new SearchFileResponse
		{
			Success = false,
			ErrorMessage = message
		};

		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = JsonSerializer.Serialize(response, JsonOptions) }]
		};
	}
}
