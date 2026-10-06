using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class ModifyLinesOperation : FileOperationBase<string, string, bool>
{
	public override async Task<CallToolResult> ExecuteAsync(string filePath, string operationsJson, bool keepBackup, CancellationToken cancellationToken)
	{
		try
		{
			// Parse and validate request
			var operations = JsonSerializer.Deserialize<FileModification[]>(operationsJson, JsonOptions);
			var request = new ModifyLinesRequest
			{
				FilePath = filePath,
				Operations = operations ?? [],
				KeepBackup = keepBackup
			};

			// Early validation
			var validationResult = ValidateOperations(request.Operations);
			if (!validationResult.Success)
				return CreateErrorResult(validationResult.ErrorMessage!);

			// File system validation using existing patterns
			if (!File.Exists(filePath))
				return CreateErrorResult($"File not found: {filePath}");

			var fileInfo = new FileInfo(filePath);
			if (!IsValidFile(filePath, fileInfo, forWrite: true))
				return CreateErrorResult($"Invalid file for modification: {filePath}");

			// Process modifications
			var response = await ProcessFileModificationsAsync(request);

			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(response, JsonOptions) }]
			};
		}
		catch (Exception ex)
		{
			return CreateErrorResult($"Line modification failed: {ex.Message}");
		}
	}

	private async Task<ModifyLinesResponse> ProcessFileModificationsAsync(ModifyLinesRequest request)
	{
		var operations = request.Operations.OrderBy(op => op.StartLine).ToArray();
		var tempFilePath = $"{request.FilePath}.new";
		var linesProcessed = 0;

		try
		{
			// Process file modifications in own scope to ensure streams are closed before finalization
			{
				// Open file with shared read access to allow other applications to read it
				var fileStream = new FileStream(request.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var reader = new StreamReader(fileStream);
				using var writer = new StreamWriter(tempFilePath);

				int currentLineNumber = 1;
				int operationIndex = 0;

				while (reader.Peek() >= 0)
				{
					// Check if we have an operation at current line
					if (operationIndex < operations.Length && operations[operationIndex].StartLine == currentLineNumber)
					{
						// Read the line for operation processing
						var line = await reader.ReadLineAsync();
						var operationResult = await ProcessOperationAsync(reader, writer, operations[operationIndex], line!);
						linesProcessed += operationResult.LinesProcessed;
						currentLineNumber += operationResult.LinesAdvanced;
						operationIndex++;
					}
					else
					{
						// Optimized direct stream transfer (.NET Core optimization)
						await writer.WriteLineAsync(await reader.ReadLineAsync());
						currentLineNumber++;
					}
				}

				// Handle remaining operations at end of file (insert operations)
				while (operationIndex < operations.Length)
				{
					var op = operations[operationIndex];
					//	TODO: Convert operations to enum and parse instead of string comparison
					if (op.Operation.ToLower() == "insert")
					{
						await WriteNewLinesAsync(writer, op.NewContent);
						linesProcessed += op.NewContent?.Length ?? 0;
					}
					operationIndex++;
				}
			} // Streams are now closed - handles released

			// Handle backup and atomic rename (files are no longer locked)
			var backupPath = await FinalizeFileOperationAsync(request.FilePath, tempFilePath, request.KeepBackup);

			return new ModifyLinesResponse
			{
				Success = true,
				BackupPath = backupPath,
				TotalLinesProcessed = linesProcessed
			};
		}
		catch (Exception ex)
		{
			// Cleanup temporary file on failure
			if (File.Exists(tempFilePath))
				File.Delete(tempFilePath);

			return new ModifyLinesResponse
			{
				Success = false,
				ErrorMessage = ex.Message
			};
		}
	}

	private async Task<OperationResult> ProcessOperationAsync(StreamReader reader, StreamWriter writer, FileModification op, string currentLine)
	{
		switch (op.Operation.ToLower())
		{
			case "insert":
				await WriteNewLinesAsync(writer, op.NewContent);
				await writer.WriteLineAsync(currentLine); // Write the current line after insert
				return new OperationResult { LinesProcessed = (op.NewContent?.Length ?? 0) + 1, LinesAdvanced = 1 };

			case "delete":
				var deleteCount = op.LineCount ?? 1;
				await SkipLinesAsync(reader, deleteCount - 1); // -1 because we already have the first line
				return new OperationResult { LinesProcessed = 0, LinesAdvanced = deleteCount };

			case "replace":
				await WriteNewLinesAsync(writer, op.NewContent);
				var replaceCount = op.LineCount ?? 1;
				await SkipLinesAsync(reader, replaceCount - 1); // -1 because we already have the first line
				return new OperationResult { LinesProcessed = op.NewContent?.Length ?? 0, LinesAdvanced = replaceCount };

			default:
				throw new ArgumentException($"Unknown operation: {op.Operation}");
		}
	}

	private async Task WriteNewLinesAsync(StreamWriter writer, string[]? newContent)
	{
		if (newContent != null)
		{
			foreach (var line in newContent)
				await writer.WriteLineAsync(line);
		}
	}

	private async Task SkipLinesAsync(StreamReader reader, int lineCount)
	{
		for (int i = 0; i < lineCount; i++)
			await reader.ReadLineAsync(); // No-op read
	}

	private ModifyLinesResponse ValidateOperations(FileModification[] operations)
	{
		if (operations.Length == 0)
			return new ModifyLinesResponse { Success = false, ErrorMessage = "No operations specified" };

		// Sort operations for overlap detection
		var sorted = operations.OrderBy(op => op.StartLine).ToArray();

		// Check for overlapping operations
		for (int i = 1; i < sorted.Length; i++)
		{
			var prev = sorted[i - 1];
			var current = sorted[i];

			int prevEnd = prev.Operation.ToLower() == "insert" ? prev.StartLine :
						 prev.StartLine + (prev.LineCount ?? 1) - 1;

			if (current.StartLine <= prevEnd)
			{
				return new ModifyLinesResponse
				{
					Success = false,
					ErrorMessage = $"Operation conflict: {prev.Operation} at lines {prev.StartLine}-{prevEnd} overlaps with {current.Operation} at line {current.StartLine}. Operations cannot overlap."
				};
			}
		}

		// Validate operation parameters and check for warnings
		string? warningMessage = null;
		foreach (var op in operations)
		{
			switch (op.Operation.ToLower())
			{
				case "insert":
					if (op.NewContent == null || op.NewContent.Length == 0)
						warningMessage = $"Insert operation at line {op.StartLine} contains no content - no changes made.";
					break;

				case "delete":
				case "replace":
					if (op.LineCount == null || op.LineCount <= 0)
						return new ModifyLinesResponse
						{
							Success = false,
							ErrorMessage = $"{op.Operation} operation at line {op.StartLine} requires valid LineCount"
						};
					break;

				default:
					return new ModifyLinesResponse
					{
						Success = false,
						ErrorMessage = $"Unknown operation type: {op.Operation}. Supported: insert, delete, replace"
					};
			}
		}

		return new ModifyLinesResponse { Success = true, WarningMessage = warningMessage };
	}

	private static async Task<string?> FinalizeFileOperationAsync(string originalPath, string tempPath, bool keepBackup)
	{
		string? backupPath = null;

		// Retry logic for atomic file operations (handles file indexing/antivirus delays)
		const int maxRetries = 5;
		const int baseDelayMs = 100;

		for (int attempt = 0; attempt < maxRetries; attempt++)
		{
			try
			{
				if (keepBackup)
				{
					backupPath = $"{originalPath}.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
					File.Move(originalPath, backupPath, overwrite: true);
				}
				else
				{
					File.Delete(originalPath);
				}

				File.Move(tempPath, originalPath, overwrite: false);
				return backupPath;
			}
			catch (IOException) when (attempt < maxRetries - 1)
			{
				// Exponential backoff: 100ms, 200ms, 400ms, 800ms
				await Task.Delay(baseDelayMs * (1 << attempt));
			}
		}

		// If we get here, all retries failed
		throw new IOException($"Failed to finalize file operation after {maxRetries} attempts. File may be locked by another process.");
	}

	private CallToolResult CreateErrorResult(string message)
	{
		var response = new ModifyLinesResponse { Success = false, ErrorMessage = message };
		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = JsonSerializer.Serialize(response, JsonOptions) }]
		};
	}
}

// Supporting result class
internal class OperationResult
{
	public int LinesProcessed { get; set; }
	public int LinesAdvanced { get; set; }
}