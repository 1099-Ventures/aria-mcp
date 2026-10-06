using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal abstract class FileOperationBase
{
	protected static readonly string[] TextFiles = [
														".txt", ".md", ".json", ".xml", ".cs", ".h", ".cpp", ".js", ".py", ".sql",
														".yml", ".yaml", ".log", ".sh", ".ps1", ".bat", ".cmd", ".bicep", ".xaml",
														// Essential wiki and git files:
														".gitignore", ".mermaid", ".sample", ".order", ".dvignore",
														// Common development/config files:
														".toml", ".ini", ".cfg", ".conf", ".env", ".dockerfile", ".gitattributes",
														".editorconfig", ".ts", ".tsx", ".jsx", ".sln", ".csproj", ".makefile",
														".make", ".cmake", ".gradle", ".pom.xml", ".build.gradle", ".nuget.config",
														".config", ".settings", ".properties", ".json5", ".jsonc",
														//	Vector graphics and other text-based formats:
														".svg", ".csv", ".tsv", ".asm", ".diff", ".patch", ".mdx", ".rst",
														// Common web files:
														".html", ".htm", ".css", ".scss", ".less", ".vue", ".php", ".asp", ".aspx",
													];
	protected static readonly string[] Images = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tiff", ".webp"];
	protected static readonly string[] BinaryFiles = [".pdf"];
	protected static readonly string[] BlockedPaths = [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)"];
	protected static readonly string[] ImportantExtensions = [".csproj", ".sln", ".config", ".settings", ".gitignore"];
	protected static readonly string[] BlockedFileTypes = [".exe", ".dll", ".jar", ".zip", ".tar", ".gz", ".rar"];
	protected static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB"];

	protected static readonly Dictionary<string, string> ImageMimeTypes = new()
	{
		{ ".jpg", "image/jpeg" },
		{ ".jpeg", "image/jpeg" },
		{ ".png", "image/png" },
		{ ".gif", "image/gif" },
		{ ".bmp", "image/bmp" },
		{ ".tiff", "image/tiff" },
		{ ".webp", "image/webp" },
	};

	protected static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	protected static bool IsValidFile(string path, FileInfo fi, bool forWrite = false)
	{
		if (string.IsNullOrWhiteSpace(path))
			return false;

		if (!IsValidFolder(path))
			return false;

		if (!forWrite && fi.Length > 20_000_000) // 20 MB limit
			return false;

		if (BlockedFileTypes.Any(ext => fi.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)))
			return false;

		if (!TextFiles.Any(ext => fi.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
			&& !Images.Any(ext => fi.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
			&& !BinaryFiles.Any(ext => fi.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)))
			return false;

		if (forWrite && (Images.Any(ext => fi.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))
			|| BinaryFiles.Any(ext => fi.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase))))
			return false;

		return true;
	}

	protected static bool IsValidFolder(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return false;

		try
		{
			var fullPath = Path.GetFullPath(path);

			if (BlockedPaths.Any(bp => fullPath.StartsWith(Path.GetFullPath(bp), StringComparison.OrdinalIgnoreCase)))
				return false;

			if (fullPath.Equals(@"C:\", StringComparison.OrdinalIgnoreCase))
				return false;

			// For new directories, check if parent exists. For existing directories, check if it exists.
			var parentDir = Path.GetDirectoryName(fullPath);
			return Directory.Exists(fullPath) || (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir));
		}
		catch
		{
			return false;
		}
	}

	protected static void EnsureDestinationDirectory(string filePath)
	{
		var directory = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
	}

	protected static bool IsImportantFile(string path)
	{
		var ext = Path.GetExtension(path).ToLowerInvariant();
		return ImportantExtensions.Contains(ext);
	}

	protected static string FormatBytes(long bytes)
	{
		int counter = 0;
		decimal number = bytes;
		while (Math.Round(number / 1024) >= 1)
		{
			number /= 1024;
			counter++;
		}
		return $"{number:n1} {Suffixes[counter]}";
	}
}

// Specialized base classes providing CancellationToken overload pattern

/// <summary>
/// Base class for file operations with 1 parameter
/// </summary>
internal abstract class FileOperationBase<T> : FileOperationBase, IFileOperation<T>
{
	/// <summary>
	/// Execute operation with explicit CancellationToken
	/// </summary>
	public abstract Task<CallToolResult> ExecuteAsync(T arg, CancellationToken cancellationToken);

	/// <summary>
	/// Execute operation with CancellationToken.None
	/// </summary>
	public Task<CallToolResult> ExecuteAsync(T arg) => ExecuteAsync(arg, CancellationToken.None);
}

/// <summary>
/// Base class for file operations with 2 parameters
/// </summary>
internal abstract class FileOperationBase<T1, T2> : FileOperationBase, IFileOperation<T1, T2>
{
	/// <summary>
	/// Execute operation with explicit CancellationToken
	/// </summary>
	public abstract Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, CancellationToken cancellationToken);

	/// <summary>
	/// Execute operation with CancellationToken.None
	/// </summary>
	public Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2) => ExecuteAsync(arg1, arg2, CancellationToken.None);
}

/// <summary>
/// Base class for file operations with 3 parameters
/// </summary>
internal abstract class FileOperationBase<T1, T2, T3> : FileOperationBase, IFileOperation<T1, T2, T3>
{
	/// <summary>
	/// Execute operation with explicit CancellationToken
	/// </summary>
	public abstract Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, CancellationToken cancellationToken);

	/// <summary>
	/// Execute operation with CancellationToken.None
	/// </summary>
	public Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3) => ExecuteAsync(arg1, arg2, arg3, CancellationToken.None);
}

/// <summary>
/// Base class for file operations with 5 parameters
/// </summary>
internal abstract class FileOperationBase<T1, T2, T3, T4, T5> : FileOperationBase, IFileOperation<T1, T2, T3, T4, T5>
{
	/// <summary>
	/// Execute operation with explicit CancellationToken
	/// </summary>
	public abstract Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, CancellationToken cancellationToken);

	/// <summary>
	/// Execute operation with CancellationToken.None
	/// </summary>
	public Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) => ExecuteAsync(arg1, arg2, arg3, arg4, arg5, CancellationToken.None);
}