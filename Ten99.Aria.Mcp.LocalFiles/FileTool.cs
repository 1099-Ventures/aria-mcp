using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Ten99.Aria.Mcp.LocalFiles;

[McpServerToolType]
public static class FileTool
{
	[McpServerTool(Name = "list_directory")]
	[Description("Lists the contents of a folder. REQUIRES ABSOLUTE PATH.")]
	public static async Task<CallToolResult> List(string path)
	{
		return await new Operations.ListDirectoryOperation().ExecuteAsync(path);
	}

	[McpServerTool(Name = "read_file")]
	[Description("Reads the contents of a file. REQUIRES ABSOLUTE PATH. Optional chunk_size and chunk_offset parameters for reading large files in chunks.")]
	public static async Task<CallToolResult> Read(string path, int? chunk_size = null, long? chunk_offset = null)
	{
		return await new Operations.ReadFileOperation().ExecuteAsync(path, chunk_size, chunk_offset);
	}

	[McpServerTool(Name = "write_file")]
	[Description("Writes the contents to a file. REQUIRES ABSOLUTE PATH.")]
	public static async Task<CallToolResult> Write(string path, string content)
	{
		return await new Operations.WriteFileOperation().ExecuteAsync(path, content);
	}

	[McpServerTool(Name = "create_directory")]
	[Description("Creates a new directory with optional recursive creation. REQUIRES ABSOLUTE PATH.")]
	public static async Task<CallToolResult> CreateDirectory(string path, bool recursive = true)
	{
		return await new Operations.CreateDirectoryOperation().ExecuteAsync(path, recursive);
	}

	[McpServerTool(Name = "rename_file")]
	[Description("Renames or moves a file from old path to new path. REQUIRES ABSOLUTE PATHS.")]
	public static async Task<CallToolResult> RenameFile(string oldPath, string newPath)
	{
		return await new Operations.RenameFileOperation().ExecuteAsync(oldPath, newPath);
	}

	[McpServerTool(Name = "copy_file")]
	[Description("Copies a file from source path to destination path. REQUIRES ABSOLUTE PATHS.")]
	public static async Task<CallToolResult> CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
	{
		return await new Operations.CopyFileOperation().ExecuteAsync(sourcePath, destinationPath, overwrite);
	}

	[McpServerTool(Name = "move_file")]
	[Description("Moves a file from source path to destination path. REQUIRES ABSOLUTE PATHS.")]
	public static async Task<CallToolResult> MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
	{
		return await new Operations.MoveFileOperation().ExecuteAsync(sourcePath, destinationPath, overwrite);
	}

	[McpServerTool(Name = "delete_file")]
	[Description("Deletes a file with safety checks. REQUIRES ABSOLUTE PATH.")]
	public static async Task<CallToolResult> DeleteFile(string path, bool force = false)
	{
		return await new Operations.DeleteFileOperation().ExecuteAsync(path, force);
	}

	[McpServerTool(Name = "delete_directory")]
	[Description("Deletes a directory with safety checks. REQUIRES ABSOLUTE PATH.")]
	public static async Task<CallToolResult> DeleteDirectory(string path, bool recursive = false, bool force = false)
	{
		return await new Operations.DeleteDirectoryOperation().ExecuteAsync(path, recursive, force);
	}

	[McpServerTool(Name = "get_file_info")]
	[Description("Gets detailed information about a file or directory. REQUIRES ABSOLUTE PATH.")]
	public static async Task<CallToolResult> GetFileInfo(string path)
	{
		return await new Operations.GetFileInfoOperation().ExecuteAsync(path);
	}

	[McpServerTool(Name = "file_exists")]
	[Description("Checks if a file exists. REQUIRES ABSOLUTE PATH.")]
	public static async Task<CallToolResult> FileExists(string path)
	{
		return await new Operations.FileExistsOperation().ExecuteAsync(path);
	}

	[McpServerTool(Name = "directory_exists")]
	[Description("Checks if a directory exists. REQUIRES ABSOLUTE PATH.")]
	public static async Task<CallToolResult> DirectoryExists(string path)
	{
		return await new Operations.DirectoryExistsOperation().ExecuteAsync(path);
	}

	[McpServerTool(Name = "batch_move_files")]
	[Description("Move multiple files in a single operation with rollback on failure. REQUIRES ABSOLUTE PATHS. Input format: JSON array of objects with 'source' and 'destination' properties. Example: [{\"source\": \"/absolute/path/file1.txt\", \"destination\": \"/absolute/new/path/file1.txt\"}, {\"source\": \"/absolute/path/file2.txt\", \"destination\": \"/absolute/new/path/file2.txt\"}]")]
	public static async Task<CallToolResult> BatchMoveFiles(string fileMappingsJson, bool overwrite = false)
	{
		return await new Operations.BatchMoveOperation().ExecuteAsync(fileMappingsJson, overwrite);
	}

	[McpServerTool(Name = "create_directory_structure")]
	[Description("Create multiple directories in a single operation with parent directory handling. REQUIRES ABSOLUTE PATHS for all directory paths.")]
	public static async Task<CallToolResult> CreateDirectoryStructure(List<string> directoryPaths, bool recursive = true)
	{
		return await new Operations.CreateDirectoryStructureOperation().ExecuteAsync(directoryPaths, recursive);
	}

	[McpServerTool(Name = "get_directory_summary")]
	[Description("Get comprehensive summary of directory contents including file types, sizes, and recent activity. REQUIRES ABSOLUTE PATH for directoryPath parameter.")]
	public static async Task<CallToolResult> GetDirectorySummary(string directoryPath, bool includeSubdirectories = false, bool includeHidden = false)
	{
		return await new Operations.DirectorySummaryOperation().ExecuteAsync(directoryPath, includeSubdirectories, includeHidden);
	}

	[McpServerTool(Name = "modify_file_lines")]
	[Description("Perform atomic line-based modifications on text files (insert, delete, replace operations). REQUIRES ABSOLUTE PATH. Operations format: JSON array of objects with 'operation' (insert/delete/replace), 'startLine' (1-based), optional 'lineCount' (for delete/replace), optional 'newContent' (array of strings for insert/replace).")]
	//	{ "jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": { "name": "modify_file_lines", "arguments": { "filePath": "D:\\Dev\\1099\\ARIA\\Wiki\\Implementation\\MCP-Servers\\File-Handler\\Issue-File-Locking-Edit-Operations.md", "operationsJson": "[{\"operation\": \"replace\", \"startLine\": 3, \"lineCount\": 2, \"newContent\": [\"## Status\", \"**In Progress** - User Story created, implementation begun\"]}]" } } }
	public static async Task<CallToolResult> ModifyFileLines(string filePath, string operationsJson, bool keepBackup = false)
	{
		return await new Operations.ModifyLinesOperation().ExecuteAsync(filePath, operationsJson, keepBackup);
	}

	[McpServerTool(Name = "search_file")]
	[Description("Search for text or regex patterns within a file and get precise byte offsets for read_file integration and line numbers for modify_file_lines. REQUIRES ABSOLUTE PATH. Returns byteOffset (for read_file chunk_offset), lineNumber (for modify_file_lines), characterPosition, and matchPreview with context.")]
	public static async Task<CallToolResult> SearchFile(string filePath, string pattern, bool isRegex = false, bool caseSensitive = false, int? maxResults = null)
	{
		return await new Operations.SearchFileOperation().ExecuteAsync(filePath, pattern, isRegex, caseSensitive, maxResults);
	}
}