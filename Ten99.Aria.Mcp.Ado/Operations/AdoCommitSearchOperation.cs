using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_search_commits</c> — keyword commit search via the Search service
/// (<c>almsearch.dev.azure.com</c>), filterable by project/repo/branch/author/date. Lean results
/// <c>{commitId, comment, author, date, repository}</c> (ADO #436).
/// </summary>
internal sealed class AdoCommitSearchOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient,
		string searchText, string? project, string? repository, string? branch, string? author,
		string? commitStartDate, string? commitEndDate, string? orderBy, int? top, int? skip)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(searchText))
				return Failure("repo_search_commits", "searchText is required.", config);

			AdoOrg org = ResolveOrg(config);

			Dictionary<string, string[]> filters = [];
			AddFilter(filters, "projectName", project);
			AddFilter(filters, "repositoryName", repository);
			AddFilter(filters, "branchName", branch);
			AddFilter(filters, "authorName", author);
			if (!string.IsNullOrWhiteSpace(commitStartDate)) filters["commitStartDate"] = [commitStartDate];
			if (!string.IsNullOrWhiteSpace(commitEndDate)) filters["commitEndDate"] = [commitEndDate];

			Dictionary<string, object?> body = new()
			{
				["searchText"] = searchText,
				["$skip"] = skip is >= 0 ? skip.Value : 0,
				["$top"] = top is > 0 ? top.Value : 10,
				["filters"] = filters,
			};
			if (!string.IsNullOrWhiteSpace(orderBy))
				body["$orderBy"] = new[] { new { field = "commitDate", sortOrder = orderBy.ToUpperInvariant() == "ASC" ? "ASC" : "DESC" } };

			string url = $"https://almsearch.dev.azure.com/{Uri.EscapeDataString(org.OrgName)}/_apis/search/commitSearchResults?api-version={ApiVersion}";
			ApiResult r = await SendAbsoluteAsync(config, httpClient, HttpMethod.Post, url, org, JsonSerializer.Serialize(body, JsonRequestOptions));
			if (!r.Ok) return ApiError("repo_search_commits", r, config);

			return Json(ProjectResults(r.Body), config);
		}
		catch (Exception ex)
		{
			return Failure("repo_search_commits", ex.Message, config);
		}
	}

	static void AddFilter(Dictionary<string, string[]> filters, string key, string? csv)
	{
		if (string.IsNullOrWhiteSpace(csv)) return;
		string[] vals = csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (vals.Length > 0) filters[key] = vals;
	}

	static object ProjectResults(string body)
	{
		List<object> results = [];
		using JsonDocument doc = JsonDocument.Parse(body);
		JsonElement root = doc.RootElement;
		int count = root.TryGetProperty("count", out JsonElement c) && c.TryGetInt32(out int n) ? n : 0;
		if (root.TryGetProperty("results", out JsonElement rs) && rs.ValueKind == JsonValueKind.Array)
			foreach (JsonElement res in rs.EnumerateArray())
			{
				JsonElement commit = res.TryGetProperty("commit", out JsonElement cm) ? cm : res;
				results.Add(new
				{
					commitId = Str(commit, "commitId"),
					comment = Str(commit, "comment"),
					author = commit.TryGetProperty("author", out JsonElement a) ? Str(a, "name") : null,
					date = commit.TryGetProperty("author", out JsonElement a2) ? Str(a2, "date") : null,
					repository = res.TryGetProperty("repository", out JsonElement repo) ? Str(repo, "name") : null,
				});
			}
		return new { count, results };
	}

	static string? Str(JsonElement o, string n) => o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
