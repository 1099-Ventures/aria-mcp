using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>pipelines_build</c> — builds/runs of a project. Actions: <c>list</c> (filter by definitions,
/// status, result, branch), <c>get_status</c> (a build's status/result), <c>get_changes</c> (commits
/// + work items in a build). Lean build projection (ADO #438).
/// </summary>
internal sealed class AdoBuildOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action, string project, int? buildId,
		string? definitions, string? statusFilter, string? resultFilter, string? branchName, int? top, string? queryOrder)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project))
				return Failure("pipelines_build", "project is required.", config);

			AdoOrg org = ResolveOrg(config);
			string prefix = $"{Uri.EscapeDataString(project)}/";

			switch (action)
			{
				case "list":
					List<KeyValuePair<string, string>> q = [];
					if (!string.IsNullOrWhiteSpace(definitions)) q.Add(new("definitions", definitions));
					if (!string.IsNullOrWhiteSpace(statusFilter)) q.Add(new("statusFilter", statusFilter));
					if (!string.IsNullOrWhiteSpace(resultFilter)) q.Add(new("resultFilter", resultFilter));
					if (!string.IsNullOrWhiteSpace(branchName)) q.Add(new("branchName", branchName));
					if (!string.IsNullOrWhiteSpace(queryOrder)) q.Add(new("queryOrder", queryOrder));
					q.Add(new("$top", (top is > 0 ? top.Value : 20).ToString()));
					ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/build/builds", org, query: q);
					if (!l.Ok) return ApiError("pipelines_build", l, config);
					return Json(new { count = ProjectList(l.Body, PipelineProjection.CompactBuild, out List<object> builds), builds }, config);

				case "get_status":
					if (buildId is null) return Failure("pipelines_build", "buildId is required for get_status.", config);
					ApiResult g = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/build/builds/{buildId}", org);
					if (!g.Ok) return ApiError("pipelines_build", g, config);
					using (JsonDocument doc = JsonDocument.Parse(g.Body))
						return Json(PipelineProjection.CompactBuild(doc.RootElement), config);

				case "get_changes":
					if (buildId is null) return Failure("pipelines_build", "buildId is required for get_changes.", config);
					List<KeyValuePair<string, string>> cq = [new("$top", (top is > 0 ? top.Value : 50).ToString())];
					ApiResult c = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/build/builds/{buildId}/changes", org, query: cq);
					if (!c.Ok) return ApiError("pipelines_build", c, config);
					return Json(new { changes = RawValueArray(c.Body) }, config);

				default:
					return Failure("pipelines_build", $"Unknown action '{action}'. Use list|get_status|get_changes.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("pipelines_build", ex.Message, config);
		}
	}

	internal static int ProjectList(string body, Func<JsonElement, Dictionary<string, object?>> project, out List<object> items)
	{
		items = [];
		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
			foreach (JsonElement e in v.EnumerateArray())
				items.Add(project(e));
		return items.Count;
	}

	static List<object> RawValueArray(string body)
	{
		List<object> list = [];
		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
			foreach (JsonElement e in v.EnumerateArray())
			{
				Dictionary<string, object?> row = new()
				{
					["id"] = e.TryGetProperty("id", out JsonElement id) ? id.ToString() : null,
					["message"] = e.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null,
					["author"] = e.TryGetProperty("author", out JsonElement a) && a.TryGetProperty("displayName", out JsonElement dn) ? dn.GetString() : null,
					["type"] = e.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
				};
				list.Add(row);
			}
		return list;
	}
}
