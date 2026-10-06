using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_create_branch</c> — create a branch from a source branch (default <c>main</c>) or an explicit
/// commit. Resolves the source commit via refs, then a zero→sha ref update (ADO #436).
/// </summary>
internal sealed class AdoCreateBranchOperation : AdoOperationBase
{
	const string ZeroSha = "0000000000000000000000000000000000000000";

	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient,
		string? repositoryId, string? branchName, string? sourceBranchName, string? sourceCommitId, string? project)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(branchName))
				return Failure("repo_create_branch", "repositoryId and branchName are required.", config);

			AdoOrg org = ResolveOrg(config);
			string prefix = string.IsNullOrEmpty(project) ? "" : $"{Uri.EscapeDataString(project)}/";
			string refsBase = $"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/refs";

			string? commitId = sourceCommitId;
			string source = string.IsNullOrWhiteSpace(sourceBranchName) ? "main" : sourceBranchName;
			if (string.IsNullOrWhiteSpace(commitId))
			{
				ApiResult sref = await SendAsync(config, httpClient, HttpMethod.Get, refsBase, org, query: [new("filter", $"heads/{source}")]);
				if (!sref.Ok) return ApiError("repo_create_branch", sref, config);
				commitId = ResolveObjectId(sref.Body, source);
				if (string.IsNullOrEmpty(commitId))
					return Failure("repo_create_branch", $"Source branch '{source}' not found in repository {repositoryId}.", config);
			}

			var body = new[] { new { name = $"refs/heads/{branchName}", newObjectId = commitId, oldObjectId = ZeroSha } };
			ApiResult r = await SendAsync(config, httpClient, HttpMethod.Post, refsBase, org, JsonSerializer.Serialize(body, JsonRequestOptions));
			if (!r.Ok) return ApiError("repo_create_branch", r, config);

			// updateRefs returns {value:[{success, name, ...}]}.
			using JsonDocument doc = JsonDocument.Parse(r.Body);
			bool success = doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array
				&& v.EnumerateArray().FirstOrDefault() is JsonElement first
				&& first.TryGetProperty("success", out JsonElement s) && s.ValueKind == JsonValueKind.True;

			return Json(new { success, branch = $"refs/heads/{branchName}", from = source, commitId }, config);
		}
		catch (Exception ex)
		{
			return Failure("repo_create_branch", ex.Message, config);
		}
	}

	static string? ResolveObjectId(string body, string branch)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
			foreach (JsonElement r in v.EnumerateArray())
				if (r.TryGetProperty("name", out JsonElement n) && n.GetString() == $"refs/heads/{branch}"
					&& r.TryGetProperty("objectId", out JsonElement o) && o.ValueKind == JsonValueKind.String)
					return o.GetString();
		return null;
	}
}
