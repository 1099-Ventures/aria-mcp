using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class RenameFileOperation : FileOperationBase<string, string>
{
	public override async Task<CallToolResult> ExecuteAsync(string oldPath, string newPath, CancellationToken cancellationToken)
	{

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!await Task.Run(() => File.Exists(oldPath), cancellationToken))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Source file not found: {oldPath}" }]
				};
			}

			if (!IsValidFile(oldPath, new FileInfo(oldPath)) || !IsValidFile(newPath, new FileInfo(newPath), true))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid file paths: {oldPath} -> {newPath}" }]
				};
			}

			if (await Task.Run(() => File.Exists(newPath), cancellationToken))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Destination file already exists: {newPath}" }]
				};
			}

			// Ensure destination directory exists
			EnsureDestinationDirectory(newPath);

			await Task.Run(() => File.Move(oldPath, newPath), cancellationToken);
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File renamed successfully: {oldPath} -> {newPath}" }]
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
				Content = [new TextContentBlock { Text = $"Error renaming file: {oldPath} -> {newPath}\n{ex.Message}" }]
			};
		}
	}
}