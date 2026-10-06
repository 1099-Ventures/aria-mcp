using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class DeleteFileOperation : FileOperationBase<string, bool>
{
	public override async Task<CallToolResult> ExecuteAsync(string path, bool force, CancellationToken cancellationToken)
	{

		try
		{
			if (!File.Exists(path))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"File not found: {path}" }]
				};
			}

			if (!IsValidFile(path, new FileInfo(path)))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid file path: {path}" }]
				};
			}

			// Safety check for important file extensions
			if (!force && IsImportantFile(path))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Important file type detected. Use force=true to delete: {path}" }]
				};
			}

			await Task.Run(() => File.Delete(path), cancellationToken);
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File deleted successfully: {path}" }]
			};
		}
		catch (OperationCanceledException)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File delete operation was cancelled: {path}" }]
			};
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error deleting file: {path}\n{ex.Message}" }]
			};
		}
	}
}