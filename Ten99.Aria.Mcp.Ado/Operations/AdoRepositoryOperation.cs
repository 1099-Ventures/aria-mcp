using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_repository</c> — read Git repositories in the active org. Actions: <c>get</c> (by id/name)
/// and <c>list</c> (optionally scoped to a project). Lean <c>{id, name, project, defaultBranch, …}</c>.
/// </summary>
internal sealed class AdoRepositoryOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(IConfiguration config, HttpClient httpClient, string action, string? project, string? repositoryId)
	{
		try
		{
			AdoOrg org = ResolveOrg(config);
			string prefix = string.IsNullOrEmpty(project) ? "" : $"{Uri.EscapeDataString(project)}/";

			switch (action)
			{
				case "get":
					if (string.IsNullOrWhiteSpace(repositoryId))
						return Failure("repo_repository", "repositoryId is required for action 'get'.", config);
					ApiResult g = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}", org);
					if (!g.Ok) return ApiError("repo_repository", g, config);
					using (JsonDocument doc = JsonDocument.Parse(g.Body))
						return Json(RepoProjection.CompactRepo(doc.RootElement), config);

				case "list":
					ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/git/repositories", org);
					if (!l.Ok) return ApiError("repo_repository", l, config);
					List<object> repos = [];
					using (JsonDocument doc = JsonDocument.Parse(l.Body))
						if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
							foreach (JsonElement e in v.EnumerateArray())
								repos.Add(RepoProjection.CompactRepo(e));
					return Json(new { count = repos.Count, repositories = repos }, config);

				default:
					return Failure("repo_repository", $"Unknown action '{action}'. Use get|list.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("repo_repository", ex.Message, config);
		}
	}
}
