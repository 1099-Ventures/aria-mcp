using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Lists the iterations a team has selected onto its backlog (sprint planning view), NOT the project's
/// full iteration tree (that is <see cref="AdoIterationListOperation"/>):
/// GET <c>{project}/{team}/_apis/work/teamsettings/iterations</c>, optionally filtered to the current
/// sprint with <c>$timeframe=current</c>. Returns <c>{count, items[]}</c> where each item is
/// <c>{id, name, path, startDate, finishDate, timeFrame}</c>. Use an item's <c>id</c> (the team-iteration
/// GUID) with wit_list_for_iteration to read the work items scheduled in that sprint.
/// </summary>
internal sealed class AdoIterationListTeamOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string team,
		string? timeframe = null)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("iteration_list_team", "project is required.", config);
			if (string.IsNullOrWhiteSpace(team)) return Failure("iteration_list_team", "team is required.", config);

			AdoOrg org = ResolveOrg(config);

			string path = $"{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(team)}/_apis/work/teamsettings/iterations";
			IEnumerable<KeyValuePair<string, string>>? query = string.IsNullOrWhiteSpace(timeframe)
				? null
				: [new("$timeframe", timeframe)];

			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Get, path, org, query: query);

			if (!result.Ok)
				return ApiError("iteration_list_team", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			JsonElement root = doc.RootElement;

			List<object> items = [];
			if (root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array)
				foreach (JsonElement node in value.EnumerateArray())
					items.Add(Project(node));

			return Json(new { count = items.Count, items }, config);
		}
		catch (Exception ex)
		{
			return Failure("iteration_list_team", ex.Message, config);
		}
	}

	/// <summary>Projects a team iteration to the lean shape, flattening the date/timeframe attributes.</summary>
	private static Dictionary<string, object?> Project(JsonElement node)
	{
		Dictionary<string, object?> item = new()
		{
			["id"] = Str(node, "id"),
			["name"] = Str(node, "name"),
			["path"] = Str(node, "path")
		};
		if (node.TryGetProperty("attributes", out JsonElement attrs) && attrs.ValueKind == JsonValueKind.Object)
		{
			if (Str(attrs, "startDate") is string start) item["startDate"] = start;
			if (Str(attrs, "finishDate") is string finish) item["finishDate"] = finish;
			if (Str(attrs, "timeFrame") is string tf) item["timeFrame"] = tf;
		}
		return item;
	}

	private static string? Str(JsonElement obj, string name)
		=> obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
