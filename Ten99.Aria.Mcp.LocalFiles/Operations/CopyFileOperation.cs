using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class CopyFileOperation : FileOperationBase<string, string, bool>
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

			if (File.Exists(destinationPath) && !overwrite)
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Destination file already exists: {destinationPath}. Use overwrite=true to replace." }]
				};
			}

			// Ensure destination directory exists
			EnsureDestinationDirectory(destinationPath);

			// Use async streaming copy for better performance
			using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
			using var destinationStream = new FileStream(destinationPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
			await sourceStream.CopyToAsync(destinationStream, cancellationToken);

			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File copied successfully: {sourcePath} -> {destinationPath}" }]
			};
		}
		catch (OperationCanceledException)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File copy operation was cancelled: {sourcePath} -> {destinationPath}" }]
			};
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error copying file: {sourcePath} -> {destinationPath}\n{ex.Message}" }]
			};
		}
	}
}