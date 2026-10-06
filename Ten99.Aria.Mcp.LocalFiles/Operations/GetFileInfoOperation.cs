using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class GetFileInfoOperation : FileOperationBase<string>
{
	public override async Task<CallToolResult> ExecuteAsync(string path, CancellationToken cancellationToken)
	{

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (await Task.Run(() => File.Exists(path), cancellationToken))
			{
				var (fi, info) = await Task.Run(() => {
					var fileInfo = new FileInfo(path);
					return (fileInfo, new FileInfoResult(fileInfo));
				}, cancellationToken);
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = JsonSerializer.Serialize(info, JsonOptions) }]
				};
			}
			else if (await Task.Run(() => Directory.Exists(path), cancellationToken))
			{
				var (di, info) = await Task.Run(() => {
					var dirInfo = new DirectoryInfo(path);
					return (dirInfo, new DirectoryInfoResult(dirInfo));
				}, cancellationToken);
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = JsonSerializer.Serialize(info, JsonOptions) }]
				};
			}
			else
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Path not found: {path}" }]
				};
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error getting file info: {path}\n{ex.Message}" }]
			};
		}
	}
}