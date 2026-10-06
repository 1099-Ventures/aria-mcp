using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Removes a single link between two work items — the missing half of link management (the create/update
/// tools can ADD a parent link but never remove one, so an item that already has a parent cannot be
/// reparented through the API). ADO removes relations by array INDEX, so this reads the current relations,
/// resolves the index of the matching link, then PATCHes a <c>remove</c> guarded by a <c>test /rev</c>.
/// Reparent = <c>wit_remove_link(child, oldParent)</c> then <c>wit_update_work_item(child, parent: new)</c>.
/// </summary>
internal sealed class AdoRemoveLinkOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		int id,
		int targetId,
		string linkType = "parent")
	{
		try
		{
			AdoOrg org = ResolveOrg(config);
			string rel = ResolveRel(linkType);

			// Relations are only returned under $expand (fields and $expand are mutually exclusive).
			ApiResult get = await SendAsync(config, httpClient, HttpMethod.Get, $"_apis/wit/workitems/{id}", org,
				query: [new KeyValuePair<string, string>("$expand", "relations")]);
			if (!get.Ok)
				return ApiError("remove_link", get, config);

			using JsonDocument doc = JsonDocument.Parse(get.Body);
			JsonElement root = doc.RootElement;
			int? rev = root.TryGetProperty("rev", out JsonElement revEl) && revEl.ValueKind == JsonValueKind.Number
				? revEl.GetInt32()
				: null;

			int index = -1;
			if (root.TryGetProperty("relations", out JsonElement rels) && rels.ValueKind == JsonValueKind.Array)
			{
				int i = 0;
				foreach (JsonElement r in rels.EnumerateArray())
				{
					string? rRel = r.TryGetProperty("rel", out JsonElement rk) ? rk.GetString() : null;
					string? rUrl = r.TryGetProperty("url", out JsonElement ru) ? ru.GetString() : null;
					if (string.Equals(rRel, rel, StringComparison.OrdinalIgnoreCase) && TailId(rUrl) == targetId)
					{
						index = i;
						break;
					}
					i++;
				}
			}

			if (index < 0)
				return Failure("remove_link",
					$"No '{rel}' link from work item {id} to {targetId} was found (linkType '{linkType}').", config);

			JsonPatch patch = new();
			if (rev is int currentRev)
				patch.TestRev(currentRev);
			patch.RemoveRelationAt(index);

			ApiResult res = await SendAsync(config, httpClient, HttpMethod.Patch, $"_apis/wit/workitems/{id}", org,
				patch.Serialize(), contentType: "application/json-patch+json");
			if (!res.Ok)
				return ApiError("remove_link", res, config);

			using JsonDocument resDoc = JsonDocument.Parse(res.Body);
			return Json(WorkItemProjection.Ack(resDoc.RootElement), config);
		}
		catch (Exception ex)
		{
			return Failure("remove_link", ex.Message, config);
		}
	}

	// Friendly link-type names → ADO rel reference names. A value already containing a '.' is treated as a
	// full rel ref name and passed through, so any link type ADO supports is reachable.
	private static string ResolveRel(string linkType)
	{
		string key = linkType.Trim();
		if (key.Contains('.'))
			return key;

		return key.ToLowerInvariant() switch
		{
			"parent" => "System.LinkTypes.Hierarchy-Reverse",
			"child" => "System.LinkTypes.Hierarchy-Forward",
			"related" => "System.LinkTypes.Related",
			"successor" => "System.LinkTypes.Dependency-Forward",
			"predecessor" => "System.LinkTypes.Dependency-Reverse",
			"duplicate" => "System.LinkTypes.Duplicate-Forward",
			"duplicate-of" or "duplicateof" => "System.LinkTypes.Duplicate-Reverse",
			_ => throw new ArgumentException(
				$"Unknown linkType '{linkType}'. Use parent|child|related|successor|predecessor|duplicate|duplicate-of, "
				+ "or a full rel reference name (e.g. System.LinkTypes.Related)."),
		};
	}

	// A relation url ends in the target work item id: .../_apis/wit/workItems/{id}.
	private static int? TailId(string? url)
	{
		if (string.IsNullOrEmpty(url))
			return null;
		int slash = url.LastIndexOf('/');
		return slash >= 0 && int.TryParse(url.AsSpan(slash + 1), out int n) ? n : null;
	}
}
