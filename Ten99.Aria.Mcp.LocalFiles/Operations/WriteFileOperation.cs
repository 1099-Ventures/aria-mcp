using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class WriteFileOperation : FileOperationBase<string, string>
{
	public override async Task<CallToolResult> ExecuteAsync(string path, string content, CancellationToken cancellationToken)
	{

		try
		{
			if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid file path: {path}" }],
					IsError = true,
				};
			}

			if (!IsValidFile(path, new FileInfo(path), true))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid file: {path}" }],
					IsError = true,
				};
			}

			await File.WriteAllTextAsync(path, content, cancellationToken);
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File written successfully: {path}" }]
			};
		}
		catch (OperationCanceledException)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File write operation was cancelled: {path}" }]
			};
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error writing file: {path}\n{ex.Message}" }]
			};
		}
	}
}