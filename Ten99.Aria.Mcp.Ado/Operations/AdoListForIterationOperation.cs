using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Lists the work items scheduled in a team's iteration (the sprint backlog contents):
/// GET <c>{project}/{team}/_apis/work/teamsettings/iterations/{iterationId}/workitems</c> yields the
/// member ids, which are then hydrated via <c>_apis/wit/workitemsbatch</c> and projected to the compact
/// shape. Returns <c>{count, workItems[]}</c>. The iteration workitems endpoint returns ids only, so this
/// is a two-hop read (relations, then a batch fetch) with the batch chunked to the 200-id cap.
/// </summary>
internal sealed class AdoListForIterationOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string team,
		string iterationId,
		string? fields = null,
		bool plainText = false)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("list_for_iteration", "project is required.", config);
			if (string.IsNullOrWhiteSpace(team)) return Failure("list_for_iteration", "team is required.", config);
			if (string.IsNullOrWhiteSpace(iterationId)) return Failure("list_for_iteration", "iterationId is required (the team-iteration GUID from work_iteration_list_team).", config);

			AdoOrg org = ResolveOrg(config);

			string path = $"{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(team)}/_apis/work/teamsettings/iterations/{Uri.EscapeDataString(iterationId)}/workitems";
			ApiResult relations = await SendAsync(config, httpClient, HttpMethod.Get, path, org);
			if (!relations.Ok)
				return ApiError("list_for_iteration", relations, config);

			int[] ids = ExtractTargetIds(relations.Body);
			if (ids.Length == 0)
				return Json(new { count = 0, workItems = Array.Empty<object>() }, config);

			List<string>? extra = WorkItemProjection.ParseFields(fields);
			string[] request = WorkItemProjection.FieldsToRequest(extra);

			List<Dictionary<string, object?>> items = [];
			foreach (int[] chunk in Chunk(ids, 200))
			{
				ApiResult batch = await BatchGetAsync(config, httpClient, org, chunk, request);
				if (!batch.Ok)
					return ApiError("list_for_iteration", batch, config);
				using JsonDocument doc = JsonDocument.Parse(batch.Body);
				items.AddRange(ProjectList(doc.RootElement, extra, plainText));
			}

			return Json(new { count = items.Count, workItems = items }, config);
		}
		catch (Exception ex)
		{
			return Failure("list_for_iteration", ex.Message, config);
		}
	}

	/// <summary>Pulls the <c>target.id</c> of each entry in the <c>workItemRelations</c> array.</summary>
	private static int[] ExtractTargetIds(string body)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		if (!doc.RootElement.TryGetProperty("workItemRelations", out JsonElement rels) || rels.ValueKind != JsonValueKind.Array)
			return [];

		List<int> ids = [];
		foreach (JsonElement rel in rels.EnumerateArray())
			if (rel.TryGetProperty("target", out JsonElement target) && target.ValueKind == JsonValueKind.Object
				&& target.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number)
				ids.Add(id.GetInt32());
		return [.. ids];
	}

	private static IEnumerable<int[]> Chunk(int[] source, int size)
	{
		for (int i = 0; i < source.Length; i += size)
			yield return source[i..Math.Min(i + size, source.Length)];
	}
}
