using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class BatchMoveOperation : FileOperationBase<string, bool>
{
	public override async Task<CallToolResult> ExecuteAsync(string fileMappingsJson, bool overwrite, CancellationToken cancellationToken)
	{

		List<BatchMoveFilesRequest> fileMappings;
		try
		{
			fileMappings = JsonSerializer.Deserialize<List<BatchMoveFilesRequest>>(fileMappingsJson, JsonOptions) ?? [];
		}
		catch (JsonException ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Invalid JSON format for file mappings. Expected: [{{\"src\": \"path1\", \"destination\": \"path2\"}}]. Error: {ex.Message}" }]
			};
		}

		if (fileMappings.Count == 0)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = "No file mappings provided" }]
			};
		}

		var results = new List<FileOperationResult>();
		var successful = new List<string>();
		var startTime = DateTime.Now;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			// Validate all paths first
			foreach (var mapping in fileMappings)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await ValidateMovePathsAsync(mapping.Source, mapping.Destination, overwrite, cancellationToken);
			}

			// Execute moves with rollback tracking
			var pathMappings = fileMappings.ToDictionary(m => m.Source, m => m.Destination);
			foreach (var mapping in fileMappings)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var source = mapping.Source;
				var destination = mapping.Destination;
				try
				{
					EnsureDestinationDirectory(destination);
					await Task.Run(() => File.Move(source, destination), cancellationToken);

					var fileSize = await Task.Run(() => new FileInfo(destination).Length, cancellationToken);
					results.Add(new FileOperationResult
					{
						Success = true,
						SourcePath = source,
						DestinationPath = destination,
						FileSize = fileSize
					});
					successful.Add(source);
				}
				catch (OperationCanceledException)
				{
					// Rollback successful operations before re-throwing
					await RollbackMovesAsync(successful, pathMappings, CancellationToken.None);
					throw;
				}
				catch (Exception ex)
				{
					results.Add(new FileOperationResult
					{
						Success = false,
						SourcePath = source,
						DestinationPath = destination,
						Error = ex.Message
					});

					// If we fail, rollback successful operations
					await RollbackMovesAsync(successful, pathMappings, CancellationToken.None);
					break;
				}
			}

			BatchOperationSummaryResult summary = new()
			{
				StartTime = startTime,
				EndTime = DateTime.Now,
				TotalOperations = fileMappings.Count,
				SuccessfulOperations = results.Count(r => r.Success),
				FailedOperations = results.Count(r => !r.Success),
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
				Content = [new TextContentBlock { Text = $"Batch move operation failed: {ex.Message}" }]
			};
		}
	}

	private static async Task ValidateMovePathsAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
	{
		if (!await Task.Run(() => File.Exists(source), cancellationToken))
			throw new FileNotFoundException($"Source file not found: {source}");

		var sourceFileInfo = await Task.Run(() => new FileInfo(source), cancellationToken);
		if (!IsValidFile(source, sourceFileInfo))
			throw new ArgumentException($"Invalid source file: {source}");

		var destFileInfo = await Task.Run(() => new FileInfo(destination), cancellationToken);
		if (!IsValidFile(destination, destFileInfo, true))
			throw new ArgumentException($"Invalid destination file: {destination}");

		if (!overwrite && await Task.Run(() => File.Exists(destination), cancellationToken))
			throw new InvalidOperationException($"Destination exists: {destination}. Use overwrite=true to replace.");
	}

	private static async Task RollbackMovesAsync(List<string> successful, Dictionary<string, string> originalMappings, CancellationToken cancellationToken)
	{
		foreach (var originalSource in successful)
		{
			try
			{
				var destination = originalMappings[originalSource];
				if (await Task.Run(() => File.Exists(destination), cancellationToken))
				{
					await Task.Run(() => File.Move(destination, originalSource), cancellationToken);
				}
			}
			catch
			{
				// Log rollback failure but continue
			}
		}
	}
}