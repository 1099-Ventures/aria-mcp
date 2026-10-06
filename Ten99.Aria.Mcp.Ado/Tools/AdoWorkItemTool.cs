using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Ado.Operations;

namespace Ten99.Aria.Mcp.Ado;

/// <summary>
/// Work-item tools — WIQL query, read, and write. Lean-by-default: compact projected fields only,
/// write ops return a minimal ack. Descriptions are always authored as Markdown.
/// </summary>
[McpServerToolType]
public static class AdoWorkItemTool
{
	[McpServerTool(Name = "wit_query_work_items")]
	[Description("Run an ad-hoc WIQL query and hydrate results in one shot (no ID-only two-step). REQUIRED: wiql (e.g. \"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.State] = 'Active'\"). OPTIONAL: project (scope), top (default 50, max 200), fields (comma-separated ref names added to the compact default set), responseType ('compact' default | 'ids' | 'full'), plainText (strip HTML). Returns lean {id, type, title, state, parent, url} per item.")]
	public static Task<CallToolResult> QueryWorkItems(
		IConfiguration config,
		HttpClient httpClient,
		string wiql,
		string? project = null,
		int top = 50,
		string? fields = null,
		string responseType = "compact",
		bool plainText = false)
		=> new AdoQueryWorkItemsOperation().Execute(config, httpClient, wiql, project, top, fields, responseType, plainText);

	[McpServerTool(Name = "wit_get_work_item")]
	[Description("Get one work item, lean by default {id, type, title, state, parent, url}. OPTIONAL: fields (comma-separated ref names added to the compact set), expand ('none' default | 'relations' | 'all' — relations returned trimmed to {rel,url}), plainText (strip HTML from field values).")]
	public static Task<CallToolResult> GetWorkItem(
		IConfiguration config,
		HttpClient httpClient,
		int id,
		string? fields = null,
		string expand = "none",
		bool plainText = false)
		=> new AdoGetWorkItemOperation().Execute(config, httpClient, id, fields, expand, plainText);

	[McpServerTool(Name = "wit_get_work_items_batch")]
	[Description("Batch fetch work items by id (max 200), lean by default {id, type, title, state, parent, url} per item. OPTIONAL: fields (comma-separated ref names added to the compact set), plainText (strip HTML).")]
	public static Task<CallToolResult> GetWorkItemsBatch(
		IConfiguration config,
		HttpClient httpClient,
		int[] ids,
		string? fields = null,
		bool plainText = false)
		=> new AdoGetWorkItemsBatchOperation().Execute(config, httpClient, ids, fields, plainText);

	[McpServerTool(Name = "wit_create_work_item")]
	[Description("Create a work item. REQUIRED: project, type (e.g. Bug, Task, 'User Story'), title. OPTIONAL: description (always stored as Markdown), assignedTo (display name or unique name), areaPath, iterationPath, parent (id — adds a Hierarchy-Reverse link), fieldsJson (JSON object of extra {\"System.Xxx\": value} fields). Returns minimal ack {id, rev, state, url}.")]
	public static Task<CallToolResult> CreateWorkItem(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string type,
		string title,
		string? description = null,
		string? assignedTo = null,
		string? areaPath = null,
		string? iterationPath = null,
		int? parent = null,
		string? fieldsJson = null)
		=> new AdoCreateWorkItemOperation().Execute(config, httpClient, project, type, title, description, assignedTo, areaPath, iterationPath, parent, fieldsJson);

	[McpServerTool(Name = "wit_update_work_item")]
	[Description("Update a work item (only supplied fields change). REQUIRED: id. OPTIONAL: title, state, description (always stored as Markdown), assignedTo, areaPath, iterationPath, parent (id — ADDS a Hierarchy-Reverse link; does NOT reparent an item that already has a parent — remove the old one with wit_remove_link first), fieldsJson (JSON object of extra {\"System.Xxx\": value} fields). Returns minimal ack {id, rev, state, url}. CROSS-PROJECT MOVE (same org only): set System.TeamProject, System.AreaPath and System.IterationPath, all pointing at the TARGET project, together in ONE fieldsJson patch. Do NOT use the areaPath/iterationPath params for a move: they validate against the source project and 400. The item keeps its id and any cross-project parent link.")]
	public static Task<CallToolResult> UpdateWorkItem(
		IConfiguration config,
		HttpClient httpClient,
		int id,
		string? title = null,
		string? state = null,
		string? description = null,
		string? assignedTo = null,
		string? areaPath = null,
		string? iterationPath = null,
		int? parent = null,
		string? fieldsJson = null)
		=> new AdoUpdateWorkItemOperation().Execute(config, httpClient, id, title, state, description, assignedTo, areaPath, iterationPath, parent, fieldsJson);

	[McpServerTool(Name = "wit_remove_link")]
	[Description("Remove a link between two work items — the missing half of link management (create/update ADD a parent link; this REMOVES one). REQUIRED: id (the work item to remove the link FROM), targetId (the work item on the other end). OPTIONAL: linkType (default 'parent') — parent|child|related|successor|predecessor|duplicate|duplicate-of, or a full rel reference name (e.g. System.LinkTypes.Related). REPARENT recipe: wit_remove_link(child, oldParent) then wit_update_work_item(child, parent: newParent). Returns the minimal ack {id, rev, state, url}; errors when no such link exists.")]
	public static Task<CallToolResult> RemoveLink(
		IConfiguration config,
		HttpClient httpClient,
		int id,
		int targetId,
		string linkType = "parent")
		=> new AdoRemoveLinkOperation().Execute(config, httpClient, id, targetId, linkType);

	[McpServerTool(Name = "wit_list_for_iteration")]
	[Description("List the work items scheduled in a team's iteration (the sprint backlog contents). REQUIRED: project, team (name or id), iterationId (the team-iteration GUID from work_iteration_list_team). OPTIONAL: fields (comma-separated ref names added to the compact set), plainText (strip HTML). Returns {count, workItems[]} in the compact shape {id, type, title, state, parent, url}.")]
	public static Task<CallToolResult> ListForIteration(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string team,
		string iterationId,
		string? fields = null,
		bool plainText = false)
		=> new AdoListForIterationOperation().Execute(config, httpClient, project, team, iterationId, fields, plainText);
}
