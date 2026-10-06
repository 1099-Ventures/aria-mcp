using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>pipelines_run</c> — runs of a pipeline (Pipelines API). Actions: <c>list</c> (runs for a
/// pipeline), <c>get</c> (a single run) (ADO #438).
/// </summary>
internal sealed class AdoPipelineRunOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action, string project, int pipelineId, int? runId)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project))
				return Failure("pipelines_run", "project is required.", config);

			AdoOrg org = ResolveOrg(config);
			string runsBase = $"{Uri.EscapeDataString(project)}/_apis/pipelines/{pipelineId}/runs";

			switch (action)
			{
				case "list":
					ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get, runsBase, org);
					if (!l.Ok) return ApiError("pipelines_run", l, config);
					return Json(new { count = AdoBuildOperation.ProjectList(l.Body, PipelineProjection.CompactRun, out List<object> runs), runs }, config);

				case "get":
					if (runId is null) return Failure("pipelines_run", "runId is required for get.", config);
					ApiResult g = await SendAsync(config, httpClient, HttpMethod.Get, $"{runsBase}/{runId}", org);
					if (!g.Ok) return ApiError("pipelines_run", g, config);
					using (JsonDocument doc = JsonDocument.Parse(g.Body))
						return Json(PipelineProjection.CompactRun(doc.RootElement), config);

				default:
					return Failure("pipelines_run", $"Unknown action '{action}'. Use list|get.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("pipelines_run", ex.Message, config);
		}
	}
}
