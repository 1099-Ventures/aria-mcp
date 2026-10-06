using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_pull_request</c> — read pull requests. Actions: <c>get</c> (by id), <c>list</c> (by repo,
/// filterable by status/source/target), <c>list_by_commits</c> (PRs containing given commit shas).
/// Lean PR projection throughout.
/// </summary>
internal sealed class AdoPullRequestReadOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action,
		string? repositoryId, int? pullRequestId, string? project,
		string? status, string? sourceRefName, string? targetRefName, int? top, string? commitIds)
	{
		try
		{
			AdoOrg org = ResolveOrg(config);
			string prefix = string.IsNullOrEmpty(project) ? "" : $"{Uri.EscapeDataString(project)}/";

			switch (action)
			{
				case "get":
					if (string.IsNullOrWhiteSpace(repositoryId) || pullRequestId is null)
						return Failure("repo_pull_request", "repositoryId and pullRequestId are required for action 'get'.", config);
					ApiResult g = await SendAsync(config, httpClient, HttpMethod.Get,
						$"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/pullrequests/{pullRequestId}", org);
					if (!g.Ok) return ApiError("repo_pull_request", g, config);
					using (JsonDocument doc = JsonDocument.Parse(g.Body))
						return Json(RepoProjection.CompactPullRequest(doc.RootElement), config);

				case "list":
					if (string.IsNullOrWhiteSpace(repositoryId))
						return Failure("repo_pull_request", "repositoryId is required for action 'list'.", config);
					List<KeyValuePair<string, string>> q = [];
					q.Add(new("searchCriteria.status", MapStatus(status)));
					if (!string.IsNullOrWhiteSpace(sourceRefName)) q.Add(new("searchCriteria.sourceRefName", sourceRefName));
					if (!string.IsNullOrWhiteSpace(targetRefName)) q.Add(new("searchCriteria.targetRefName", targetRefName));
					q.Add(new("$top", (top is > 0 ? top.Value : 25).ToString()));
					ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get,
						$"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/pullrequests", org, query: q);
					if (!l.Ok) return ApiError("repo_pull_request", l, config);
					return Json(new { count = ProjectPrCount(l.Body, out List<object> prs), pullRequests = prs }, config);

				case "list_by_commits":
					if (string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(commitIds))
						return Failure("repo_pull_request", "repositoryId and commitIds are required for action 'list_by_commits'.", config);
					string[] ids = commitIds.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
					string body = JsonSerializer.Serialize(new { queries = new[] { new { items = ids, type = "commit" } } }, JsonRequestOptions);
					ApiResult c = await SendAsync(config, httpClient, HttpMethod.Post,
						$"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/pullrequestquery", org, body);
					if (!c.Ok) return ApiError("repo_pull_request", c, config);
					return Json(ProjectCommitQuery(c.Body), config);

				default:
					return Failure("repo_pull_request", $"Unknown action '{action}'. Use get|list|list_by_commits.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("repo_pull_request", ex.Message, config);
		}
	}

	static string MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
	{
		"active" => "active",
		"abandoned" => "abandoned",
		"completed" => "completed",
		"all" => "all",
		_ => "active",
	};

	static int ProjectPrCount(string bodyJson, out List<object> prs)
	{
		prs = [];
		using JsonDocument doc = JsonDocument.Parse(bodyJson);
		if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
			foreach (JsonElement e in v.EnumerateArray())
				prs.Add(RepoProjection.CompactPullRequest(e));
		return prs.Count;
	}

	static object ProjectCommitQuery(string bodyJson)
	{
		Dictionary<string, List<object>> byCommit = [];
		using JsonDocument doc = JsonDocument.Parse(bodyJson);
		if (doc.RootElement.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array)
			foreach (JsonElement entry in results.EnumerateArray())
				if (entry.ValueKind == JsonValueKind.Object)
					foreach (JsonProperty commit in entry.EnumerateObject())
					{
						List<object> list = [];
						if (commit.Value.ValueKind == JsonValueKind.Array)
							foreach (JsonElement pr in commit.Value.EnumerateArray())
								list.Add(RepoProjection.CompactPullRequest(pr));
						byCommit[commit.Name] = list;
					}
		return new { byCommit };
	}
}
