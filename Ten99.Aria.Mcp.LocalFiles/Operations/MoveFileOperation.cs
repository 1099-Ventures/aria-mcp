using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class MoveFileOperation : FileOperationBase<string, string, bool>
{
	public override async Task<CallToolResult> ExecuteAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken)
	{

		try
		{
			if (!File.Exists(sourcePath))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Source file not found: {sourcePath}" }]
				};
			}

			if (!IsValidFile(sourcePath, new FileInfo(sourcePath)) || !IsValidFile(destinationPath, new FileInfo(destinationPath), true))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid file paths: {sourcePath} -> {destinationPath}" }]
				};
			}

			if (File.Exists(destinationPath))
			{
				if (!overwrite)
				{
					return new CallToolResult
					{
						Content = [new TextContentBlock { Text = $"Destination file already exists: {destinationPath}. Use overwrite=true to replace." }]
					};
				}
				File.Delete(destinationPath);
			}

			// Ensure destination directory exists
			EnsureDestinationDirectory(destinationPath);

			await Task.Run(() => File.Move(sourcePath, destinationPath), cancellationToken);
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File moved successfully: {sourcePath} -> {destinationPath}" }]
			};
		}
		catch (OperationCanceledException)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File move operation was cancelled: {sourcePath} -> {destinationPath}" }]
			};
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error moving file: {sourcePath} -> {destinationPath}\n{ex.Message}" }]
			};
		}
	}
}