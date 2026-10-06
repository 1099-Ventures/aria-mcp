using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_pull_request_thread</c> — read PR review threads. Actions: <c>list</c> (threads on a PR),
/// <c>list_comments</c> (comments in a thread). Lean thread/comment projection (ADO #436).
/// </summary>
internal sealed class AdoPullRequestThreadOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action,
		string? repositoryId, int? pullRequestId, string? project, int? threadId)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(repositoryId) || pullRequestId is null)
				return Failure("repo_pull_request_thread", "repositoryId and pullRequestId are required.", config);

			AdoOrg org = ResolveOrg(config);
			string prefix = string.IsNullOrEmpty(project) ? "" : $"{Uri.EscapeDataString(project)}/";
			string prBase = $"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/pullRequests/{pullRequestId}";

			switch (action)
			{
				case "list":
					ApiResult t = await SendAsync(config, httpClient, HttpMethod.Get, $"{prBase}/threads", org);
					if (!t.Ok) return ApiError("repo_pull_request_thread", t, config);
					return Json(new { threads = ProjectThreads(t.Body) }, config);

				case "list_comments":
					if (threadId is null) return Failure("repo_pull_request_thread", "threadId is required for list_comments.", config);
					ApiResult c = await SendAsync(config, httpClient, HttpMethod.Get, $"{prBase}/threads/{threadId}/comments", org);
					if (!c.Ok) return ApiError("repo_pull_request_thread", c, config);
					return Json(new { comments = ProjectComments(c.Body) }, config);

				default:
					return Failure("repo_pull_request_thread", $"Unknown action '{action}'. Use list|list_comments.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("repo_pull_request_thread", ex.Message, config);
		}
	}

	static List<object> ProjectThreads(string body)
	{
		List<object> threads = [];
		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
			foreach (JsonElement th in v.EnumerateArray())
				threads.Add(RepoProjection.CompactThread(th));
		return threads;
	}

	static List<object> ProjectComments(string body)
	{
		List<object> comments = [];
		using JsonDocument doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
			foreach (JsonElement c in v.EnumerateArray())
				comments.Add(RepoProjection.CompactComment(c));
		return comments;
	}
}
