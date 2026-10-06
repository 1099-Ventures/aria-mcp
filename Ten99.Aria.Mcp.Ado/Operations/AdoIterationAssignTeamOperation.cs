using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Adds an existing iteration to a team's backlog iterations:
/// POST <c>{project}/{team}/_apis/work/teamsettings/iterations</c> with a <c>{id}</c> body, where
/// <c>id</c> is the iteration's classification-node <c>identifier</c> (a GUID — see iteration_list).
/// Returns a lean ack <c>{id, name, path, url}</c> for the team iteration.
/// </summary>
internal sealed class AdoIterationAssignTeamOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string team,
		string identifier)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("iteration_assign_team", "project is required.", config);
			if (string.IsNullOrWhiteSpace(team)) return Failure("iteration_assign_team", "team is required.", config);
			if (string.IsNullOrWhiteSpace(identifier)) return Failure("iteration_assign_team", "identifier is required (the iteration's GUID identifier from iteration_list).", config);

			AdoOrg org = ResolveOrg(config);

			string body = JsonSerializer.Serialize(new { id = identifier }, JsonRequestOptions);
			string path = $"{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(team)}/_apis/work/teamsettings/iterations";

			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Post, path, org, body);

			if (!result.Ok)
				return ApiError("iteration_assign_team", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			JsonElement node = doc.RootElement;
			return Json(new
			{
				id = Str(node, "id"),
				name = Str(node, "name"),
				path = Str(node, "path"),
				url = Str(node, "url")
			}, config);
		}
		catch (Exception ex)
		{
			return Failure("iteration_assign_team", ex.Message, config);
		}
	}

	private static string? Str(JsonElement obj, string name)
		=> obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
