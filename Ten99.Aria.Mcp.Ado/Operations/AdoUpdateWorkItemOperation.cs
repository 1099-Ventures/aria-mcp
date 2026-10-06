using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Updates a work item: PATCH <c>_apis/wit/workitems/{id}</c> with a JSON-Patch body. Only the
/// supplied fields are changed. Returns the minimal ack <c>{id, rev, state, url}</c>.
/// NOTE: <paramref name="parent"/> ADDS a Hierarchy-Reverse link; it does not reparent an item that
/// already has a parent (ADO rejects a second parent). To reparent: remove the existing parent link with
/// <see cref="AdoRemoveLinkOperation"/> (wit_remove_link), then call this with the new parent.
/// </summary>
/// <remarks>
/// Cross-project move (same org): pass <c>System.TeamProject</c>, <c>System.AreaPath</c> and
/// <c>System.IterationPath</c> for the TARGET project via <paramref name="fieldsJson"/> in a single patch.
/// The typed <paramref name="areaPath"/> / <paramref name="iterationPath"/> params validate against the
/// source project and cannot move an item (they 400). Confirmed: id, body, and any cross-project parent
/// link are preserved.
/// </remarks>
internal sealed class AdoUpdateWorkItemOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
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
	{
		try
		{
			AdoOrg org = ResolveOrg(config);

			JsonPatch patch = new JsonPatch()
				.AddField("System.Title", title)
				.AddField("System.State", state)
				.AddMarkdownField("System.Description", description)
				.AddField("System.AssignedTo", assignedTo)
				.AddField("System.AreaPath", areaPath)
				.AddField("System.IterationPath", iterationPath)
				.AddExtraFields(fieldsJson);

			if (parent is int parentId)
				patch.AddParent($"{OrgBase}/{Uri.EscapeDataString(org.OrgName)}", parentId);

			if (patch.IsEmpty)
				return Failure("update_work_item", "Nothing to update — supply at least one field.", config);

			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Patch, $"_apis/wit/workitems/{id}", org,
				patch.Serialize(), contentType: "application/json-patch+json");

			if (!result.Ok)
				return ApiError("update_work_item", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			return Json(WorkItemProjection.Ack(doc.RootElement), config);
		}
		catch (Exception ex)
		{
			return Failure("update_work_item", ex.Message, config);
		}
	}
}
