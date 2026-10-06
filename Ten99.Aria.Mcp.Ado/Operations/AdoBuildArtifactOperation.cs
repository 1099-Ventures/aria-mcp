using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>pipelines_artifact</c> — a build's published artifacts. Action: <c>list</c>
/// (name + download url per artifact) (ADO #438). Binary download is a Phase 2 concern.
/// </summary>
internal sealed class AdoBuildArtifactOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(IConfiguration config, HttpClient httpClient, string action, string project, int buildId)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project))
				return Failure("pipelines_artifact", "project is required.", config);

			if (action != "list")
				return Failure("pipelines_artifact", $"Unknown action '{action}'. Only 'list' is available in Phase 1.", config);

			AdoOrg org = ResolveOrg(config);
			ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get, $"{Uri.EscapeDataString(project)}/_apis/build/builds/{buildId}/artifacts", org);
			if (!l.Ok) return ApiError("pipelines_artifact", l, config);
			return Json(new { count = AdoBuildOperation.ProjectList(l.Body, PipelineProjection.CompactArtifact, out List<object> artifacts), artifacts }, config);
		}
		catch (Exception ex)
		{
			return Failure("pipelines_artifact", ex.Message, config);
		}
	}
}
