using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class DeleteDirectoryOperation : FileOperationBase<string, bool, bool>
{
	public override async Task<CallToolResult> ExecuteAsync(string path, bool recursive, bool force, CancellationToken cancellationToken)
	{

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!await Task.Run(() => Directory.Exists(path), cancellationToken))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Directory not found: {path}" }]
				};
			}

			if (!IsValidFolder(path))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid directory path: {path}" }]
				};
			}

			// Safety check for important directories
			var dirName = Path.GetFileName(path).ToLowerInvariant();
			var importantDirs = new[] { "src", "source", "code", ".git", "wiki", "docs", "documentation" };
			if (!force && importantDirs.Contains(dirName))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Important directory detected. Use force=true to delete: {path}" }]
				};
			}

			if (!recursive && await Task.Run(() => Directory.GetFileSystemEntries(path).Length, cancellationToken) > 0)
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Directory not empty. Use recursive=true to delete contents: {path}" }]
				};
			}

			await Task.Run(() => Directory.Delete(path, recursive), cancellationToken);
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Directory deleted successfully: {path}" }]
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
				Content = [new TextContentBlock { Text = $"Error deleting directory: {path}\n{ex.Message}" }]
			};
		}
	}
}