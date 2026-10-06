using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Ado.Operations;

namespace Ten99.Aria.Mcp.Ado.Tools;

/// <summary>
/// Search domain (<c>search_*</c>) — text search across code, wiki, and work items via the ADO
/// Search service. Lean, trimmed results (ADO #441). Filters are comma-separated.
/// </summary>
[McpServerToolType]
public static class AdoSearchTool
{
	[McpServerTool(Name = "search_code")]
	[Description("Search code across the org. Filters (comma-separated): project, repository, path, branch. Lean {fileName, path, repository, project}.")]
	public static Task<CallToolResult> SearchCode(
		IConfiguration config, HttpClient httpClient,
		[Description("Keywords to search in code.")] string searchText,
		[Description("Project name(s), comma-separated. Omit for all.")] string? project = null,
		[Description("Repository name(s), comma-separated.")] string? repository = null,
		[Description("Path filter(s), comma-separated.")] string? path = null,
		[Description("Branch(es), comma-separated.")] string? branch = null,
		[Description("Max results (default 15).")] int? top = null,
		[Description("Results to skip (default 0).")] int? skip = null)
		=> new AdoSearchOperation().Code(config, httpClient, searchText, project, repository, path, branch, top, skip);

	[McpServerTool(Name = "search_wiki")]
	[Description("Search wiki pages across the org. Filters (comma-separated): project, wiki. Lean {fileName, path, wiki, project}.")]
	public static Task<CallToolResult> SearchWiki(
		IConfiguration config, HttpClient httpClient,
		[Description("Keywords to search in wiki pages.")] string searchText,
		[Description("Project name(s), comma-separated.")] string? project = null,
		[Description("Wiki name(s), comma-separated.")] string? wiki = null,
		[Description("Max results (default 15).")] int? top = null,
		[Description("Results to skip (default 0).")] int? skip = null)
		=> new AdoSearchOperation().Wiki(config, httpClient, searchText, project, wiki, top, skip);

	[McpServerTool(Name = "search_workitem")]
	[Description("Search work items across the org. Filters (comma-separated): project, workItemType, state, assignedTo. Lean {id, title, type, state, project}.")]
	public static Task<CallToolResult> SearchWorkItem(
		IConfiguration config, HttpClient httpClient,
		[Description("Keywords to search in work items.")] string searchText,
		[Description("Project name(s), comma-separated.")] string? project = null,
		[Description("Work item type(s), comma-separated.")] string? workItemType = null,
		[Description("State(s), comma-separated.")] string? state = null,
		[Description("Assigned-to display name(s), comma-separated.")] string? assignedTo = null,
		[Description("Max results (default 15).")] int? top = null,
		[Description("Results to skip (default 0).")] int? skip = null)
		=> new AdoSearchOperation().WorkItem(config, httpClient, searchText, project, workItemType, state, assignedTo, top, skip);
}
