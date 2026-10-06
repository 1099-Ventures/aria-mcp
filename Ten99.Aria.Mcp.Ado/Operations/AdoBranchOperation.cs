using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_branch</c> — read branches via Git refs. Actions: <c>get</c> (by name), <c>list</c>,
/// <c>list_mine</c> (branches the current user has pushed to). Lean <c>{name, objectId}</c> (ADO #436).
/// </summary>
internal sealed class AdoBranchOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action,
		string? repositoryId, string? project, string? branchName, int? top, string? filterContains)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(repositoryId))
				return Failure("repo_branch", "repositoryId is required.", config);

			AdoOrg org = ResolveOrg(config);
			string prefix = string.IsNullOrEmpty(project) ? "" : $"{Uri.EscapeDataString(project)}/";
			string refsBase = $"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/refs";
			int max = top is > 0 ? top.Value : 100;

			switch (action)
			{
				case "get":
					if (string.IsNullOrWhiteSpace(branchName))
						return Failure("repo_branch", "branchName is required for get.", config);
					ApiResult g = await SendAsync(config, httpClient, HttpMethod.Get, refsBase, org,
						query: [new("filter", $"heads/{branchName}")]);
					if (!g.Ok) return ApiError("repo_branch", g, config);
					object? match = FindBranch(g.Body, branchName);
					return match is null
						? Failure("repo_branch", $"Branch '{branchName}' not found in repository {repositoryId}.", config)
						: Json(match, config);

				case "list":
				case "list_mine":
					List<KeyValuePair<string, string>> q = [new("$top", max.ToString())];
					if (action == "list") q.Add(new("filter", "heads/"));
					else q.Add(new("includeMyBranches", "true"));
					if (!string.IsNullOrWhiteSpace(filterContains)) q.Add(new("filterContains", filterContains));
					ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get, refsBase, org, query: q);
					if (!l.Ok) return ApiError("repo_branch", l, config);
					List<object> branches = ProjectRefs(l.Body, max);
					return Json(new { count = branches.Count, branches }, config);

				default:
					return Failure("repo_branch", $"Unknown action '{action}'. Use get|list|list_mine.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("repo_branch", ex.Message, config);
		}
	}

	static object? FindBranch(string body, string branchName)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
			foreach (JsonElement r in v.EnumerateArray())
			{
				string? name = Str(r, "name");
				if (name == $"refs/heads/{branchName}" || name == branchName)
					return new { name, objectId = Str(r, "objectId") };
			}
		return null;
	}

	static List<object> ProjectRefs(string body, int max)
	{
		List<object> refs = [];
		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
			foreach (JsonElement r in v.EnumerateArray())
			{
				if (refs.Count >= max) break;
				refs.Add(new { name = Str(r, "name"), objectId = Str(r, "objectId") });
			}
		return refs;
	}

	static string? Str(JsonElement o, string n) => o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
