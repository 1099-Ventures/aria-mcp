using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Batch fetch work items by id, projected to the compact shape. Optional <paramref name="fields"/>
/// allowlist (comma-separated ref names) is unioned with the compact defaults.
/// </summary>
internal sealed class AdoGetWorkItemsBatchOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, int[] ids, string? fields = null, bool plainText = false)
	{
		try
		{
			if (ids is null || ids.Length == 0)
				return Failure("get_work_items_batch", "ids is required and must be non-empty.", config);
			if (ids.Length > 200)
				return Failure("get_work_items_batch", "Batch is limited to 200 ids per call.", config);

			AdoOrg org = ResolveOrg(config);
			List<string>? extra = WorkItemProjection.ParseFields(fields);

			ApiResult result = await BatchGetAsync(config, httpClient, org, ids, WorkItemProjection.FieldsToRequest(extra));
			if (!result.Ok)
				return ApiError("get_work_items_batch", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			List<Dictionary<string, object?>> items = ProjectList(doc.RootElement, extra, plainText);
			return Json(new { count = items.Count, workItems = items }, config);
		}
		catch (Exception ex)
		{
			return Failure("get_work_items_batch", ex.Message, config);
		}
	}
}
