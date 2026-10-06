using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Creates a work item: POST <c>{project}/_apis/wit/workitems/${type}</c> with a JSON-Patch body.
/// Returns the minimal ack <c>{id, rev, state, url}</c> — never the full created item.
/// </summary>
internal sealed class AdoCreateWorkItemOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
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
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("create_work_item", "project is required.", config);
			if (string.IsNullOrWhiteSpace(type)) return Failure("create_work_item", "type is required (e.g. Bug, Task, User Story).", config);
			if (string.IsNullOrWhiteSpace(title)) return Failure("create_work_item", "title is required.", config);

			AdoOrg org = ResolveOrg(config);

			JsonPatch patch = new JsonPatch()
				.AddField("System.Title", title)
				.AddMarkdownField("System.Description", description)
				.AddField("System.AssignedTo", assignedTo)
				.AddField("System.AreaPath", areaPath)
				.AddField("System.IterationPath", iterationPath)
				.AddExtraFields(fieldsJson);

			if (parent is int parentId)
				patch.AddParent($"{OrgBase}/{Uri.EscapeDataString(org.OrgName)}", parentId);

			string path = $"{Uri.EscapeDataString(project)}/_apis/wit/workitems/${Uri.EscapeDataString(type)}";
			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Post, path, org,
				patch.Serialize(), contentType: "application/json-patch+json");

			if (!result.Ok)
				return ApiError("create_work_item", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			return Json(WorkItemProjection.Ack(doc.RootElement), config);
		}
		catch (Exception ex)
		{
			return Failure("create_work_item", ex.Message, config);
		}
	}
}
