using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// GET a single work item, projected to the compact shape by default. Relations are opt-in via
/// <paramref name="expand"/> (<c>none</c>|<c>relations</c>|<c>all</c>) and returned trimmed to
/// <c>{rel, url}</c> — never the full <c>_links</c>/attributes blob.
/// </summary>
internal sealed class AdoGetWorkItemOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		int id,
		string? fields = null,
		string expand = "none",
		bool plainText = false)
	{
		try
		{
			AdoOrg org = ResolveOrg(config);
			List<string>? extra = WorkItemProjection.ParseFields(fields);
			bool wantRelations = expand is "relations" or "all";

			// fields and $expand are mutually exclusive on the ADO API: when expanding we drop the
			// field allowlist (all fields come back) and project from what's returned.
			List<KeyValuePair<string, string>> query = [];
			if (wantRelations)
				query.Add(new("$expand", expand == "all" ? "all" : "relations"));
			else
				query.Add(new("fields", string.Join(",", WorkItemProjection.FieldsToRequest(extra))));

			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Get, $"_apis/wit/workitems/{id}", org, query: query);
			if (!result.Ok)
				return ApiError("get_work_item", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			Dictionary<string, object?> item = WorkItemProjection.Compact(doc.RootElement, extra, plainText);

			if (wantRelations && doc.RootElement.TryGetProperty("relations", out JsonElement rels) && rels.ValueKind == JsonValueKind.Array)
			{
				item["relations"] = rels.EnumerateArray().Select(r => new
				{
					rel = r.TryGetProperty("rel", out JsonElement rk) ? rk.GetString() : null,
					url = r.TryGetProperty("url", out JsonElement ru) ? ru.GetString() : null
				}).ToArray();
			}

			return Json(item, config);
		}
		catch (Exception ex)
		{
			return Failure("get_work_item", ex.Message, config);
		}
	}
}
