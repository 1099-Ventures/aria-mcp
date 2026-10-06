using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Runs an ad-hoc WIQL query (POST <c>_apis/wit/wiql</c>) and, unless <c>responseType=ids</c>,
/// hydrates the resulting ids in one shot via the batch endpoint — killing the ID-only ->
/// get_batch two-step the Node MCP forced (see "One-shot query hydrate").
/// </summary>
internal sealed class AdoQueryWorkItemsOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		string wiql,
		string? project = null,
		int top = 50,
		string? fields = null,
		string responseType = "compact",
		bool plainText = false)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(wiql))
				return Failure("query_work_items", "wiql is required.", config);

			AdoOrg org = ResolveOrg(config);

			// WIQL can be scoped to a project (path prefix); $top caps the id set.
			string path = string.IsNullOrWhiteSpace(project)
				? "_apis/wit/wiql"
				: $"{Uri.EscapeDataString(project)}/_apis/wit/wiql";
			string body = JsonSerializer.Serialize(new { query = wiql }, JsonRequestOptions);
			KeyValuePair<string, string>[] query = [new("$top", Math.Clamp(top, 1, 200).ToString())];

			ApiResult wiqlResult = await SendAsync(config, httpClient, HttpMethod.Post, path, org, body, query: query);
			if (!wiqlResult.Ok)
				return ApiError("query_work_items", wiqlResult, config);

			using JsonDocument doc = JsonDocument.Parse(wiqlResult.Body);
			int[] ids = ExtractIds(doc.RootElement, top);

			if (ids.Length == 0)
				return Json(new { count = 0, workItems = Array.Empty<object>() }, config);

			if (string.Equals(responseType, "ids", StringComparison.OrdinalIgnoreCase))
				return Json(new { count = ids.Length, ids }, config);

			// compact | full -> one-shot hydrate. "full" simply widens the requested field set is left
			// to the caller's `fields` allowlist; the default projection stays lean either way.
			List<string>? extra = WorkItemProjection.ParseFields(fields);
			ApiResult batch = await BatchGetAsync(config, httpClient, org, ids, WorkItemProjection.FieldsToRequest(extra));
			if (!batch.Ok)
				return ApiError("query_work_items", batch, config);

			using JsonDocument batchDoc = JsonDocument.Parse(batch.Body);
			List<Dictionary<string, object?>> items = ProjectList(batchDoc.RootElement, extra, plainText);
			return Json(new { count = items.Count, workItems = items }, config);
		}
		catch (Exception ex)
		{
			return Failure("query_work_items", ex.Message, config);
		}
	}

	/// <summary>Pulls ids from either a flat WIQL result (workItems) or a tree/one-hop (workItemRelations).</summary>
	private static int[] ExtractIds(JsonElement root, int top)
	{
		List<int> ids = [];
		if (root.TryGetProperty("workItems", out JsonElement flat) && flat.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement wi in flat.EnumerateArray())
				if (wi.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number)
					ids.Add(id.GetInt32());
		}
		else if (root.TryGetProperty("workItemRelations", out JsonElement rel) && rel.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement r in rel.EnumerateArray())
				if (r.TryGetProperty("target", out JsonElement t) && t.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number)
					ids.Add(id.GetInt32());
		}
		return [.. ids.Distinct().Take(Math.Clamp(top, 1, 200))];
	}
}
