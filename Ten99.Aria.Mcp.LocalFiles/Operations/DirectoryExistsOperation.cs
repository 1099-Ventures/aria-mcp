using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class DirectoryExistsOperation : FileOperationBase<string>
{
	public override async Task<CallToolResult> ExecuteAsync(string path, CancellationToken cancellationToken)
	{

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			ExistenceCheckResult result = new()
			{
				Path = path,
				Exists = await Task.Run(() => Directory.Exists(path), cancellationToken),
				Type = "directory"
			};
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(result, JsonOptions) }]
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
				Content = [new TextContentBlock { Text = $"Error checking directory existence: {path}\n{ex.Message}" }]
			};
		}
	}
}