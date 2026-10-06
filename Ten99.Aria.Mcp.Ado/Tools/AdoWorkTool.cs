using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Ado.Operations;

namespace Ten99.Aria.Mcp.Ado;

/// <summary>
/// Work-domain tools — iterations (sprints) as classification nodes and team assignment. Seeds the
/// "work" domain (ADO #440). Lean-by-default: list returns {count, items[]}, writes return a minimal
/// ack. Descriptions are always authored as Markdown.
/// </summary>
[McpServerToolType]
public static class AdoWorkTool
{
	[McpServerTool(Name = "work_iteration_list")]
	[Description("List a project's iteration (sprint) classification nodes as a tree. REQUIRED: project. OPTIONAL: depth (levels of nesting to fetch, default 5). Returns {count, items[]} where count is the number of top-level iterations (0 on empty) and each item is {id, identifier, name, path, hasChildren, startDate, finishDate, children[]}. Use `identifier` (GUID) with work_iteration_assign_team and `path` (or a name) to build a parentPath for work_iteration_create.")]
	public static Task<CallToolResult> IterationList(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		int depth = 5)
		=> new AdoIterationListOperation().Execute(config, httpClient, project, depth);

	[McpServerTool(Name = "work_iteration_create")]
	[Description("Create an iteration (sprint) node in a project, optionally nested. REQUIRED: project, name. OPTIONAL: parentPath (relative path under the project — no leading project name, `/`-separated, e.g. 'Release 1/Sprint A' — to nest the new node; omit for a top-level iteration), startDate & finishDate (ISO 8601, e.g. '2026-01-01T00:00:00Z'). Returns a lean ack {id, identifier, name, path, startDate, finishDate, url}. Re-posting an existing name updates it.")]
	public static Task<CallToolResult> IterationCreate(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string name,
		string? parentPath = null,
		string? startDate = null,
		string? finishDate = null)
		=> new AdoIterationCreateOperation().Execute(config, httpClient, project, name, parentPath, startDate, finishDate);

	[McpServerTool(Name = "work_iteration_assign_team")]
	[Description("Add an existing iteration to a team's backlog iterations (makes the sprint selectable for that team). REQUIRED: project, team (name or id), identifier (the iteration's GUID `identifier` from work_iteration_list). Returns a lean ack {id, name, path, url} for the team iteration.")]
	public static Task<CallToolResult> IterationAssignTeam(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string team,
		string identifier)
		=> new AdoIterationAssignTeamOperation().Execute(config, httpClient, project, team, identifier);

	[McpServerTool(Name = "work_iteration_update")]
	[Description("Update an existing iteration (sprint) node: rename and/or re-date it. REQUIRED: project, path (the iteration's relative path under the project — no leading project name, `/`-separated, e.g. 'Release 1/Sprint A'). OPTIONAL: name (new name), startDate & finishDate (ISO 8601, e.g. '2026-01-01T00:00:00Z'). Supply at least one of name/startDate/finishDate. Returns a lean ack {id, identifier, name, path, startDate, finishDate, url}.")]
	public static Task<CallToolResult> IterationUpdate(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string path,
		string? name = null,
		string? startDate = null,
		string? finishDate = null)
		=> new AdoIterationUpdateOperation().Execute(config, httpClient, project, path, name, startDate, finishDate);

	[McpServerTool(Name = "work_iteration_delete")]
	[Description("Delete an iteration (sprint) node from a project. REQUIRED: project, path (the iteration's relative path under the project — no leading project name, `/`-separated, e.g. 'Release 1/Sprint A'). OPTIONAL: reclassifyId (id of the iteration to move existing work items to — REQUIRED by ADO when the node still has work items scheduled against it, else the delete fails). Returns a lean ack {success, deletedPath, reclassifyId?}.")]
	public static Task<CallToolResult> IterationDelete(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string path,
		int? reclassifyId = null)
		=> new AdoIterationDeleteOperation().Execute(config, httpClient, project, path, reclassifyId);

	[McpServerTool(Name = "work_iteration_list_team")]
	[Description("List the iterations a TEAM has selected onto its backlog (sprint-planning view), not the project's full iteration tree (that is work_iteration_list). REQUIRED: project, team (name or id). OPTIONAL: timeframe ('current' to return only the current sprint). Returns {count, items[]} where each item is {id, name, path, startDate, finishDate, timeFrame}. Use an item's `id` (the team-iteration GUID) with wit_list_for_iteration.")]
	public static Task<CallToolResult> IterationListTeam(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string team,
		string? timeframe = null)
		=> new AdoIterationListTeamOperation().Execute(config, httpClient, project, team, timeframe);
}
