using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>pipelines_build_log</c> — a build's logs. Actions: <c>list</c> (available logs),
/// <c>get_content</c> (text of one log, optional line range) (ADO #438).
/// </summary>
internal sealed class AdoBuildLogOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action, string project, int buildId, int? logId, int? startLine, int? endLine)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project))
				return Failure("pipelines_build_log", "project is required.", config);

			AdoOrg org = ResolveOrg(config);
			string logsBase = $"{Uri.EscapeDataString(project)}/_apis/build/builds/{buildId}/logs";

			switch (action)
			{
				case "list":
					ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get, logsBase, org);
					if (!l.Ok) return ApiError("pipelines_build_log", l, config);
					return Json(new { count = AdoBuildOperation.ProjectList(l.Body, PipelineProjection.CompactLog, out List<object> logs), logs }, config);

				case "get_content":
					if (logId is null) return Failure("pipelines_build_log", "logId is required for get_content.", config);
					List<KeyValuePair<string, string>> q = [];
					if (startLine is not null) q.Add(new("startLine", startLine.Value.ToString()));
					if (endLine is not null) q.Add(new("endLine", endLine.Value.ToString()));
					ApiResult c = await SendAsync(config, httpClient, HttpMethod.Get, $"{logsBase}/{logId}", org, query: q);
					if (!c.Ok) return ApiError("pipelines_build_log", c, config);
					return Json(new { buildId, logId, content = LogText(c.Body) }, config);

				default:
					return Failure("pipelines_build_log", $"Unknown action '{action}'. Use list|get_content.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("pipelines_build_log", ex.Message, config);
		}
	}

	// Log content comes back either as {value:[lines], count} or plain text.
	static string LogText(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
				return string.Join('\n', v.EnumerateArray().Select(e => e.GetString()));
		}
		catch (JsonException) { /* not JSON — plain text */ }
		return body;
	}
}
