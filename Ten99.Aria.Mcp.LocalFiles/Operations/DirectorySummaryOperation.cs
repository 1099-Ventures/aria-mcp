using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class DirectorySummaryOperation : FileOperationBase<string, bool, bool>
{
	public override async Task<CallToolResult> ExecuteAsync(string directoryPath, bool includeSubdirectories, bool includeHidden, CancellationToken cancellationToken)
	{

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!await Task.Run(() => Directory.Exists(directoryPath), cancellationToken))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Directory not found: {directoryPath}" }]
				};
			}

			if (!IsValidFolder(directoryPath))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid directory path: {directoryPath}" }]
				};
			}

			var (files, directories) = await Task.Run(() => {
				var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
				var directoryInfo = new DirectoryInfo(directoryPath);
				var filesArray = directoryInfo.GetFiles("*", searchOption);
				var dirsArray = directoryInfo.GetDirectories("*", searchOption);
				return (filesArray, dirsArray);
			}, cancellationToken);

			if (!includeHidden)
			{
				files = files.Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden)).ToArray();
				directories = directories.Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden)).ToArray();
			}

			var fileTypeGroups = files
				.GroupBy(f => f.Extension.ToLowerInvariant())
				.ToDictionary(g => string.IsNullOrEmpty(g.Key) ? "no extension" : g.Key,
							 g => new FileTypeBreakdownResult { Count = g.Count(), TotalSize = g.Sum(f => f.Length) });

			DirectorySummaryResult summary = new()
			{
				DirectoryPath = directoryPath,
				FileCount = files.Length,
				DirectoryCount = directories.Length,
				TotalSize = files.Sum(f => f.Length),
				LastModified = files.Any() ? files.Max(f => f.LastWriteTime) : DateTime.MinValue,
				FileTypeBreakdown = fileTypeGroups,
				LargestFiles = files
					.OrderByDescending(f => f.Length)
					.Take(10)
					.Select(f => new FileInfoSummaryResult { Name = f.Name, FullName = f.FullName, Size = f.Length, LastWriteTime = f.LastWriteTime })
					.ToList(),
				RecentFiles = files
					.OrderByDescending(f => f.LastWriteTime)
					.Take(10)
					.Select(f => new FileInfoSummaryResult { Name = f.Name, FullName = f.FullName, Size = f.Length, LastWriteTime = f.LastWriteTime })
					.ToList()
			};

			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(summary, JsonOptions) }]
			};
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error analyzing directory: {directoryPath}\n{ex.Message}" }]
			};
		}
	}
}