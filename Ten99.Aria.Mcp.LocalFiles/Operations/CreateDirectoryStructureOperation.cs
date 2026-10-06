using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class CreateDirectoryStructureOperation : FileOperationBase<List<string>, bool>
{
	public override async Task<CallToolResult> ExecuteAsync(List<string> directoryPaths, bool recursive, CancellationToken cancellationToken)
	{

		var results = new List<DirectoryCreationResult>();

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			// Sort paths to create parent directories first
			var sortedPaths = directoryPaths
				.Select(path => Path.GetFullPath(path))
				.OrderBy(path => path.Length)
				.ToList();

			foreach (var path in sortedPaths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					if (!IsValidFolder(path))
					{
						results.Add(new DirectoryCreationResult
						{
							Success = false,
							Path = path,
							Error = "Invalid directory path"
						});
						continue;
					}

					if (await Task.Run(() => Directory.Exists(path), cancellationToken))
					{
						results.Add(new DirectoryCreationResult
						{
							Success = true,
							Path = path,
							AlreadyExisted = true
						});
						continue;
					}

					await Task.Run(() => Directory.CreateDirectory(path), cancellationToken);
					results.Add(new DirectoryCreationResult
					{
						Success = true,
						Path = path,
						AlreadyExisted = false
					});
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					results.Add(new DirectoryCreationResult
					{
						Success = false,
						Path = path,
						Error = ex.Message
					});
				}
			}

			var summary = new
			{
				TotalDirectories = directoryPaths.Count,
				Created = results.Count(r => r.Success && !r.AlreadyExisted),
				AlreadyExisted = results.Count(r => r.Success && r.AlreadyExisted),
				Failed = results.Count(r => !r.Success),
				Results = results
			};

			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(summary, JsonOptions) }]
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
				Content = [new TextContentBlock { Text = $"Directory structure creation failed: {ex.Message}" }]
			};
		}
	}
}