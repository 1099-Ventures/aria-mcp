using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class ListDirectoryOperation : FileOperationBase<string>
{
	public override async Task<CallToolResult> ExecuteAsync(string path, CancellationToken cancellationToken)
	{

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsValidFolder(path))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid folder path: {path}" }]
				};
			}

			var files = await Task.Run(() => Directory.GetFiles(path).Select(f => new DirectoryItemResult
			{
				Name = Path.GetFileName(f),
				Path = f,
				Type = "file",
				Size = new FileInfo(f).Length,
			}), cancellationToken);

			var dirs = await Task.Run(() => Directory.GetDirectories(path).Select(d => new DirectoryItemResult
			{
				Name = Path.GetFileName(d),
				Path = d,
				Type = "directory",
				Size = 0L,
			}), cancellationToken);

			return new() { Content = [.. files.Concat(dirs).Select(l => new TextContentBlock { Text = JsonSerializer.Serialize(l, JsonOptions) })] };
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error listing directory: {path}\n{ex.Message}" }]
			};
		}
	}
}