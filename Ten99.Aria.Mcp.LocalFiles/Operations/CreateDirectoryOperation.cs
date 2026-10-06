using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class CreateDirectoryOperation : FileOperationBase<string, bool>
{
	public override async Task<CallToolResult> ExecuteAsync(string path, bool recursive, CancellationToken cancellationToken)
	{

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid directory path: {path}" }]
				};
			}

			if (!IsValidFolder(path))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Cannot create directory in protected path: {path}" }]
				};
			}

			if (await Task.Run(() => Directory.Exists(path), cancellationToken))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Directory already exists: {path}" }]
				};
			}

			await Task.Run(() => Directory.CreateDirectory(path), cancellationToken);
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Directory created successfully: {path}" }]
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
				Content = [new TextContentBlock { Text = $"Error creating directory: {path}\n{ex.Message}" }]
			};
		}
	}
}