using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_pull_request_thread_write</c> — write PR review threads. Actions: <c>create</c> (new thread,
/// optionally anchored to a file/line), <c>reply</c> (comment in a thread), <c>update_status</c>
/// (active|fixed|wontFix|closed|byDesign|pending). Mirrors the MS fork (ADO #436).
/// </summary>
internal sealed class AdoPullRequestThreadWriteOperation : AdoOperationBase
{
	static readonly JsonSerializerOptions BodyOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
	};

	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action,
		string? repositoryId, int? pullRequestId, string? project, int? threadId,
		string? content, string? status, string? filePath, int? rightFileStartLine, int? rightFileEndLine)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(repositoryId) || pullRequestId is null)
				return Failure("repo_pull_request_thread_write", "repositoryId and pullRequestId are required.", config);

			AdoOrg org = ResolveOrg(config);
			string prefix = string.IsNullOrEmpty(project) ? "" : $"{Uri.EscapeDataString(project)}/";
			string prBase = $"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/pullRequests/{pullRequestId}";

			switch (action)
			{
				case "create":
				{
					if (string.IsNullOrWhiteSpace(content))
						return Failure("repo_pull_request_thread_write", "content is required for create.", config);

					object? threadContext = null;
					if (!string.IsNullOrWhiteSpace(filePath))
					{
						string normalized = filePath.StartsWith('/') ? filePath : $"/{filePath}";
						threadContext = new Dictionary<string, object?>
						{
							["filePath"] = normalized,
							["rightFileStart"] = rightFileStartLine is > 0 ? new { line = rightFileStartLine } : null,
							["rightFileEnd"] = rightFileEndLine is > 0 ? new { line = rightFileEndLine } : null,
						};
					}

					var body = new
					{
						comments = new[] { new { content, commentType = "text" } },
						status = NormalizeStatus(status) ?? "active",
						threadContext,
					};
					ApiResult r = await SendAsync(config, httpClient, HttpMethod.Post, $"{prBase}/threads", org, JsonSerializer.Serialize(body, BodyOptions));
					if (!r.Ok) return ApiError("repo_pull_request_thread_write", r, config);
					using (JsonDocument doc = JsonDocument.Parse(r.Body))
						return Json(RepoProjection.CompactThread(doc.RootElement), config);
				}

				case "reply":
				{
					if (threadId is null) return Failure("repo_pull_request_thread_write", "threadId is required for reply.", config);
					if (string.IsNullOrWhiteSpace(content)) return Failure("repo_pull_request_thread_write", "content is required for reply.", config);
					var body = new { content, commentType = "text" };
					ApiResult r = await SendAsync(config, httpClient, HttpMethod.Post, $"{prBase}/threads/{threadId}/comments", org, JsonSerializer.Serialize(body, BodyOptions));
					if (!r.Ok) return ApiError("repo_pull_request_thread_write", r, config);
					using (JsonDocument doc = JsonDocument.Parse(r.Body))
						return Json(RepoProjection.CompactComment(doc.RootElement), config);
				}

				case "update_status":
				{
					if (threadId is null) return Failure("repo_pull_request_thread_write", "threadId is required for update_status.", config);
					string? s = NormalizeStatus(status);
					if (s is null) return Failure("repo_pull_request_thread_write", "status is required for update_status (active|fixed|wontFix|closed|byDesign|pending).", config);
					var body = new { status = s };
					ApiResult r = await SendAsync(config, httpClient, HttpMethod.Patch, $"{prBase}/threads/{threadId}", org, JsonSerializer.Serialize(body, BodyOptions));
					if (!r.Ok) return ApiError("repo_pull_request_thread_write", r, config);
					using (JsonDocument doc = JsonDocument.Parse(r.Body))
						return Json(RepoProjection.CompactThread(doc.RootElement), config);
				}

				default:
					return Failure("repo_pull_request_thread_write", $"Unknown action '{action}'. Use create|reply|update_status.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("repo_pull_request_thread_write", ex.Message, config);
		}
	}

	// ADO CommentThreadStatus enum names (camelCase for REST). First char lowered to be forgiving.
	static string? NormalizeStatus(string? status)
	{
		if (string.IsNullOrWhiteSpace(status)) return null;
		string s = status.Trim();
		return char.ToLowerInvariant(s[0]) + s[1..];
	}
}
