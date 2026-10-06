using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ten99.Aria.Mcp.LocalFiles.Models;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal class ReadFileOperation : FileOperationBase<string, int?, long?>
{
	public override async Task<CallToolResult> ExecuteAsync(string path, int? chunk_size, long? chunk_offset, CancellationToken cancellationToken)
	{

		try
		{
			if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid file path: {path}" }]
				};
			}

			if (!File.Exists(path))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"File not found: {path}" }]
				};
			}

			var fi = new FileInfo(path);

			var pathExt = Path.GetExtension(path).ToLowerInvariant();
			if (!IsValidFile(path, fi))
			{
				return new CallToolResult
				{
					Content = [new TextContentBlock { Text = $"Invalid file: {path}" }]
				};
			}
			else if (TextFiles.Any(ext => pathExt.Equals(ext, StringComparison.OrdinalIgnoreCase)))
			{
				return await ReadTextFileAsync(path, fi, chunk_size, chunk_offset, cancellationToken);
			}
			else if (Images.Any(ext => pathExt.Equals(ext, StringComparison.OrdinalIgnoreCase)))
			{
				return await ReadImageFileAsync(path, fi, cancellationToken);
			}
			else if (BinaryFiles.Any(ext => pathExt.Equals(ext, StringComparison.OrdinalIgnoreCase)))
			{
				return await ReadBinaryFileAsync(path, fi, cancellationToken);
			}

			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Invalid file: {path}" }]
			};
		}
		catch (OperationCanceledException)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"File read operation was cancelled: {path}" }]
			};
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error reading file: {path}\n{ex.Message}" }]
			};
		}
	}

	private static async Task<CallToolResult> ReadTextFileAsync(string path, FileInfo fi, int? chunk_size, long? chunk_offset, CancellationToken cancellationToken)
	{
		if (chunk_size.HasValue)
		{
			// Open file with shared read access to allow other applications to read/write it
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (chunk_offset.HasValue && chunk_offset > 0)
				stream.Seek(chunk_offset.Value, SeekOrigin.Begin);

			byte[] buffer = new byte[chunk_size.Value];
			int bytesRead = stream.Read(buffer, 0, chunk_size.Value);
			string content = Encoding.UTF8.GetString(buffer, 0, bytesRead);

			return new CallToolResult
			{
				Content = [new TextContentBlock
				{
					Text = content,
					Meta = new JsonObject
					{
						["bytes_read"] = bytesRead,
						["chunk_offset"] = chunk_offset ?? 0,
						["is_complete"] = (chunk_offset ?? 0) + bytesRead >= stream.Length,
						["next_offset"] = (chunk_offset ?? 0) + bytesRead
					}
				}]
			};
		}
		else
		{
			// Open file with shared read access to allow other applications to read/write it
			string content;
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (var reader = new StreamReader(stream))
			{
				content = await reader.ReadToEndAsync(cancellationToken);
			}

			TextFileContentResult result = new()
			{
				Name = Path.GetFileName(path),
				Path = path,
				Type = "text",
				Size = fi.Length,
				Content = content,
			};

			return new CallToolResult
			{
				Content = [new TextContentBlock
				{
					Text = JsonSerializer.Serialize(result, JsonOptions)
				}]
			};
		}
	}

	private static async Task<CallToolResult> ReadImageFileAsync(string path, FileInfo fi, CancellationToken cancellationToken)
	{
		Memory<byte> buffer;
		// Open file with shared read access to allow other applications to read/write it
		using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			buffer = new byte[stream.Length];
			await stream.ReadExactlyAsync(buffer, cancellationToken);
		}

		return new CallToolResult
		{
			Content = [new ImageContentBlock
			{
				Data = buffer,
				Meta = new()
				{
					["name"] = Path.GetFileName(path),
					["path"] = path,
					["size"] = fi.Length,
				},
				MimeType = ImageMimeTypes.GetValueOrDefault(Path.GetExtension(path).ToLowerInvariant(), "application/octet-stream"),
			}]
		};
	}

	private static async Task<CallToolResult> ReadBinaryFileAsync(string path, FileInfo fi, CancellationToken cancellationToken)
	{
		// Open file with shared read access to allow other applications to read/write it
		Memory<byte> buffer;
		using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			buffer = new byte[stream.Length];
			await stream.ReadExactlyAsync(buffer, cancellationToken);
		}

		return new CallToolResult
		{
			Content = [new EmbeddedResourceBlock
			{
				Resource = new TextResourceContents
				{
					Text = Convert.ToBase64String(buffer.ToArray()),
					Meta = new()
					{
						["name"] = Path.GetFileName(path),
						["path"] = path,
						["size"] = fi.Length,
						["encoding"] = "base64",
					},
					MimeType = "application/pdf",
					Uri = "",	//	TODO: Find out and fill this with correct info
				},
				Meta = new()
				{
					["name"] = Path.GetFileName(path),
					["mimeType"] = "application/pdf",
					["encoding"] = "base64",
					["path"] = path,
					["size"] = fi.Length,
				}
			}]
		};
	}
}