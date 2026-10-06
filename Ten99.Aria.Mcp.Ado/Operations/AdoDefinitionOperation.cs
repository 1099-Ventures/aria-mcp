using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>pipelines_definition</c> — pipeline (build) definitions. Actions: <c>list</c> (filter by name),
/// <c>list_revisions</c> (revision history) (ADO #438).
/// </summary>
internal sealed class AdoDefinitionOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action, string project, int? definitionId, string? name, int? top, string? queryOrder)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project))
				return Failure("pipelines_definition", "project is required.", config);

			AdoOrg org = ResolveOrg(config);
			string prefix = $"{Uri.EscapeDataString(project)}/";

			switch (action)
			{
				case "list":
					List<KeyValuePair<string, string>> q = [];
					if (!string.IsNullOrWhiteSpace(name)) q.Add(new("name", name));
					if (!string.IsNullOrWhiteSpace(queryOrder)) q.Add(new("queryOrder", queryOrder));
					q.Add(new("$top", (top is > 0 ? top.Value : 50).ToString()));
					ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/build/definitions", org, query: q);
					if (!l.Ok) return ApiError("pipelines_definition", l, config);
					return Json(new { count = AdoBuildOperation.ProjectList(l.Body, PipelineProjection.CompactDefinition, out List<object> defs), definitions = defs }, config);

				case "list_revisions":
					if (definitionId is null) return Failure("pipelines_definition", "definitionId is required for list_revisions.", config);
					ApiResult r = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/build/definitions/{definitionId}/revisions", org);
					if (!r.Ok) return ApiError("pipelines_definition", r, config);
					return Json(new { count = AdoBuildOperation.ProjectList(r.Body, PipelineProjection.CompactDefinition, out List<object> revs), revisions = revs }, config);

				default:
					return Failure("pipelines_definition", $"Unknown action '{action}'. Use list|list_revisions.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("pipelines_definition", ex.Message, config);
		}
	}
}
