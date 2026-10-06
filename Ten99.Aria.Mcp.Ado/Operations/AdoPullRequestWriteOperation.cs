using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_pull_request_write</c> — write operations on pull requests. Actions:
/// <c>create</c> (with optional work-item links + one-call autocomplete), <c>update</c> (fields,
/// status, autocomplete on/off), <c>update_reviewers</c> (add/remove), <c>vote</c> (approve/reject).
/// Returns a lean ack. Mirrors the MS fork's <c>repo_pull_request_write</c> semantics (ADO #435).
/// </summary>
internal sealed class AdoPullRequestWriteOperation : AdoOperationBase
{
	// Update bodies must be able to send explicit nulls (to clear autocomplete), so no null-ignore here.
	static readonly JsonSerializerOptions PatchOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	static readonly Dictionary<string, int> VoteMap = new(StringComparer.OrdinalIgnoreCase)
	{
		["Approved"] = 10,
		["ApprovedWithSuggestions"] = 5,
		["NoVote"] = 0,
		["WaitingForAuthor"] = -5,
		["Rejected"] = -10,
	};

	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action,
		string? repositoryId, int? pullRequestId, string? project,
		string? sourceRefName, string? targetRefName, string? title, string? description, bool isDraft,
		string? workItems, bool? autoComplete, string? mergeStrategy, bool deleteSourceBranch, bool transitionWorkItems,
		string? status, string[]? reviewerIds, string? reviewerAction, string? vote)
	{
		try
		{
			AdoOrg org = ResolveOrg(config);
			string prefix = string.IsNullOrEmpty(project) ? "" : $"{Uri.EscapeDataString(project)}/";
			string prBase = $"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId ?? "")}/pullrequests";

			switch (action)
			{
				case "create":
					return await CreateAsync(config, httpClient, org, prBase, repositoryId, sourceRefName, targetRefName, title,
						description, isDraft, workItems, autoComplete, mergeStrategy, deleteSourceBranch, transitionWorkItems);

				case "update":
					return await UpdateAsync(config, httpClient, org, prBase, repositoryId, pullRequestId, title, description,
						isDraft, targetRefName, status, autoComplete, mergeStrategy, deleteSourceBranch, transitionWorkItems);

				case "update_reviewers":
					return await UpdateReviewersAsync(config, httpClient, org, prBase, repositoryId, pullRequestId, reviewerIds, reviewerAction);

				case "vote":
					return await VoteAsync(config, httpClient, org, prBase, repositoryId, pullRequestId, vote);

				default:
					return Failure("repo_pull_request_write", $"Unknown action '{action}'. Use create|update|update_reviewers|vote.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("repo_pull_request_write", ex.Message, config);
		}
	}

	async Task<CallToolResult> CreateAsync(
		IConfiguration config, HttpClient httpClient, AdoOrg org, string prBase, string? repositoryId,
		string? sourceRefName, string? targetRefName, string? title, string? description, bool isDraft,
		string? workItems, bool? autoComplete, string? mergeStrategy, bool deleteSourceBranch, bool transitionWorkItems)
	{
		if (string.IsNullOrWhiteSpace(repositoryId)) return Failure("repo_pull_request_write", "repositoryId is required for create.", config);
		if (string.IsNullOrWhiteSpace(sourceRefName)) return Failure("repo_pull_request_write", "sourceRefName is required for create (e.g. refs/heads/my-branch).", config);
		if (string.IsNullOrWhiteSpace(targetRefName)) return Failure("repo_pull_request_write", "targetRefName is required for create (e.g. refs/heads/main).", config);
		if (string.IsNullOrWhiteSpace(title)) return Failure("repo_pull_request_write", "title is required for create.", config);

		var workItemRefs = string.IsNullOrWhiteSpace(workItems)
			? []
			: workItems.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(id => new { id }).ToArray();

		string createBody = JsonSerializer.Serialize(new { sourceRefName, targetRefName, title, description, isDraft, workItemRefs }, JsonRequestOptions);
		ApiResult created = await SendAsync(config, httpClient, HttpMethod.Post, prBase, org, createBody);
		if (!created.Ok) return ApiError("repo_pull_request_write", created, config);

		using JsonDocument doc = JsonDocument.Parse(created.Body);
		int? prId = doc.RootElement.TryGetProperty("pullRequestId", out JsonElement idEl) && idEl.TryGetInt32(out int n) ? n : null;

		// One-call convenience: set autocomplete after creation (the REST API only accepts it on update).
		if (autoComplete == true && prId is not null)
		{
			ApiResult ac = await SetAutoCompleteAsync(config, httpClient, org, $"{prBase}/{prId}", true, mergeStrategy, deleteSourceBranch, transitionWorkItems);
			if (ac.Ok)
				using (JsonDocument acDoc = JsonDocument.Parse(ac.Body))
					return Json(RepoProjection.Ack(acDoc.RootElement), config);
		}

		return Json(RepoProjection.Ack(doc.RootElement), config);
	}

	async Task<CallToolResult> UpdateAsync(
		IConfiguration config, HttpClient httpClient, AdoOrg org, string prBase, string? repositoryId, int? pullRequestId,
		string? title, string? description, bool isDraft, string? targetRefName, string? status,
		bool? autoComplete, string? mergeStrategy, bool deleteSourceBranch, bool transitionWorkItems)
	{
		if (string.IsNullOrWhiteSpace(repositoryId) || pullRequestId is null)
			return Failure("repo_pull_request_write", "repositoryId and pullRequestId are required for update.", config);

		// Autocomplete on/off is its own PATCH (needs the current user's id when enabling).
		if (autoComplete is not null)
		{
			ApiResult ac = await SetAutoCompleteAsync(config, httpClient, org, $"{prBase}/{pullRequestId}", autoComplete.Value, mergeStrategy, deleteSourceBranch, transitionWorkItems);
			if (!ac.Ok) return ApiError("repo_pull_request_write", ac, config);
		}

		Dictionary<string, object?> update = [];
		if (title is not null) update["title"] = title;
		if (description is not null) update["description"] = description;
		if (targetRefName is not null) update["targetRefName"] = targetRefName;
		if (!string.IsNullOrWhiteSpace(status)) update["status"] = status.Trim().ToLowerInvariant();
		// isDraft only when explicitly toggling — callers set it via a separate path; included when true/false differs from default is ambiguous, so send when title/desc present is avoided. Keep explicit:
		update["isDraft"] = isDraft;

		ApiResult upd = await SendAsync(config, httpClient, HttpMethod.Patch, $"{prBase}/{pullRequestId}", org,
			JsonSerializer.Serialize(update, PatchOptions));
		if (!upd.Ok) return ApiError("repo_pull_request_write", upd, config);

		using JsonDocument doc = JsonDocument.Parse(upd.Body);
		return Json(RepoProjection.Ack(doc.RootElement), config);
	}

	async Task<ApiResult> SetAutoCompleteAsync(
		IConfiguration config, HttpClient httpClient, AdoOrg org, string prUrl,
		bool enable, string? mergeStrategy, bool deleteSourceBranch, bool transitionWorkItems)
	{
		object body;
		if (enable)
		{
			string? userId = await GetAuthenticatedUserIdAsync(config, httpClient, org);
			Dictionary<string, object?> completion = new()
			{
				["deleteSourceBranch"] = deleteSourceBranch,
				["transitionWorkItems"] = transitionWorkItems,
				["bypassPolicy"] = false,
			};
			if (!string.IsNullOrWhiteSpace(mergeStrategy)) completion["mergeStrategy"] = mergeStrategy;
			body = new Dictionary<string, object?> { ["autoCompleteSetBy"] = new { id = userId }, ["completionOptions"] = completion };
		}
		else
		{
			body = new Dictionary<string, object?> { ["autoCompleteSetBy"] = null, ["completionOptions"] = null };
		}

		return await SendAsync(config, httpClient, HttpMethod.Patch, prUrl, org, JsonSerializer.Serialize(body, PatchOptions));
	}

	async Task<CallToolResult> UpdateReviewersAsync(
		IConfiguration config, HttpClient httpClient, AdoOrg org, string prBase, string? repositoryId, int? pullRequestId,
		string[]? reviewerIds, string? reviewerAction)
	{
		if (string.IsNullOrWhiteSpace(repositoryId) || pullRequestId is null)
			return Failure("repo_pull_request_write", "repositoryId and pullRequestId are required for update_reviewers.", config);
		if (reviewerIds is null || reviewerIds.Length == 0)
			return Failure("repo_pull_request_write", "reviewerIds is required for update_reviewers.", config);
		bool add = string.Equals(reviewerAction, "add", StringComparison.OrdinalIgnoreCase);
		bool remove = string.Equals(reviewerAction, "remove", StringComparison.OrdinalIgnoreCase);
		if (!add && !remove)
			return Failure("repo_pull_request_write", "reviewerAction must be 'add' or 'remove'.", config);

		foreach (string id in reviewerIds)
		{
			string url = $"{prBase}/{pullRequestId}/reviewers/{Uri.EscapeDataString(id)}";
			ApiResult r = add
				? await SendAsync(config, httpClient, HttpMethod.Put, url, org, JsonSerializer.Serialize(new { id }, JsonRequestOptions))
				: await SendAsync(config, httpClient, HttpMethod.Delete, url, org);
			if (!r.Ok) return ApiError("repo_pull_request_write", r, config);
		}

		return Json(new { success = true, action = add ? "add" : "remove", pullRequestId, reviewers = reviewerIds }, config);
	}

	async Task<CallToolResult> VoteAsync(
		IConfiguration config, HttpClient httpClient, AdoOrg org, string prBase, string? repositoryId, int? pullRequestId, string? vote)
	{
		if (string.IsNullOrWhiteSpace(repositoryId) || pullRequestId is null)
			return Failure("repo_pull_request_write", "repositoryId and pullRequestId are required for vote.", config);
		if (string.IsNullOrWhiteSpace(vote) || !VoteMap.TryGetValue(vote, out int voteValue))
			return Failure("repo_pull_request_write", "vote must be one of Approved|ApprovedWithSuggestions|NoVote|WaitingForAuthor|Rejected.", config);

		string? userId = await GetAuthenticatedUserIdAsync(config, httpClient, org);
		if (string.IsNullOrEmpty(userId))
			return Failure("repo_pull_request_write", "Could not resolve the authenticated user id to cast a vote.", config);

		ApiResult r = await SendAsync(config, httpClient, HttpMethod.Put,
			$"{prBase}/{pullRequestId}/reviewers/{userId}", org, JsonSerializer.Serialize(new { vote = voteValue, id = userId }, JsonRequestOptions));
		if (!r.Ok) return ApiError("repo_pull_request_write", r, config);

		return Json(new { success = true, vote, pullRequestId }, config);
	}
}
