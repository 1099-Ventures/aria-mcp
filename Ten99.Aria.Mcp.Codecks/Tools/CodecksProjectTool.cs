using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>Project domain (<c>project_*</c>) — workspace projects (ADO #437).</summary>
[McpServerToolType]
public static class CodecksProjectTool
{
	[McpServerTool(Name = "project_list")]
	[Description("✅ TESTED & READY - List projects in the Codecks workspace. Returns project fields plus deck counts (count:decks) only — deck objects are NOT inlined by default to keep the response small. OPTIONAL: offset (int, page start), limit (int, page size, default 20), includeDecks (bool, default false - when true, inlines trimmed deck info {id, title, spaceId, count:cards} per project; use deck_list_by_project for full deck detail). Returns {count, projects:[…], account} — top-level count is 0 on empty.")]
	public static Task<CallToolResult> ListProjects(IConfiguration config, HttpClient httpClient, string accountId, int offset = 0, int limit = 20, bool includeDecks = false)
		=> new CodecksListProjectsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksDetailRequest(accountId, offset) { Limit = limit, IncludeDecks = includeDecks });

	[McpServerTool(Name = "project_get_detail")]
	[Description("✅ TESTED & READY - Get detailed information about a specific project in the Codecks workspace with enhanced fields")]
	public static Task<CallToolResult> GetProjectDetail(IConfiguration config, HttpClient httpClient, string projectId, int offset = 0)
		=> new CodecksGetProjectDetailOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, new CodecksDetailRequest(projectId, offset));
}
