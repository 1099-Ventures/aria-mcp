using System.Text.Json;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Lean projections for the repositories domain — mirrors <see cref="WorkItemProjection"/>'s
/// philosophy: never return avatars, identity descriptors, or <c>_links</c> unless asked.
/// </summary>
internal static class RepoProjection
{
	/// <summary>Compact repository: <c>{id, name, project, defaultBranch, isDisabled, webUrl}</c>.</summary>
	public static Dictionary<string, object?> CompactRepo(JsonElement r) => new()
	{
		["id"] = Str(r, "id"),
		["name"] = Str(r, "name"),
		["project"] = r.TryGetProperty("project", out JsonElement p) ? Str(p, "name") : null,
		["defaultBranch"] = Str(r, "defaultBranch"),
		["isDisabled"] = Bool(r, "isDisabled"),
		["webUrl"] = r.TryGetProperty("webUrl", out JsonElement w) && w.ValueKind == JsonValueKind.String ? w.GetString() : Str(r, "remoteUrl"),
	};

	/// <summary>Compact pull request:
	/// <c>{id, title, status, isDraft, source, target, repository, createdBy, creationDate, url}</c>.</summary>
	public static Dictionary<string, object?> CompactPullRequest(JsonElement pr) => new()
	{
		["id"] = Num(pr, "pullRequestId"),
		["title"] = Str(pr, "title"),
		["status"] = Str(pr, "status"),
		["isDraft"] = Bool(pr, "isDraft"),
		["source"] = Str(pr, "sourceRefName"),
		["target"] = Str(pr, "targetRefName"),
		["repository"] = pr.TryGetProperty("repository", out JsonElement repo) ? Str(repo, "name") : null,
		["createdBy"] = pr.TryGetProperty("createdBy", out JsonElement cb) ? Str(cb, "displayName") : null,
		["creationDate"] = Str(pr, "creationDate"),
		["mergeStatus"] = Str(pr, "mergeStatus"),
		["url"] = WebUrl(pr) ?? Str(pr, "url"),
	};

	/// <summary>Write-op ack: <c>{id, status, isDraft, url}</c> — the lean confirmation shape.</summary>
	public static Dictionary<string, object?> Ack(JsonElement pr) => new()
	{
		["id"] = Num(pr, "pullRequestId"),
		["status"] = Str(pr, "status"),
		["isDraft"] = Bool(pr, "isDraft"),
		["autoComplete"] = pr.TryGetProperty("autoCompleteSetBy", out JsonElement ac) && ac.ValueKind == JsonValueKind.Object,
		["url"] = WebUrl(pr) ?? Str(pr, "url"),
	};

	/// <summary>Compact review thread: <c>{id, status, filePath, comments:[…]}</c>.</summary>
	public static Dictionary<string, object?> CompactThread(JsonElement t)
	{
		List<object> comments = [];
		if (t.TryGetProperty("comments", out JsonElement cs) && cs.ValueKind == JsonValueKind.Array)
			foreach (JsonElement c in cs.EnumerateArray())
				comments.Add(CompactComment(c));

		return new()
		{
			["id"] = Num(t, "id"),
			["status"] = Str(t, "status"),
			["isDeleted"] = Bool(t, "isDeleted"),
			["filePath"] = t.TryGetProperty("threadContext", out JsonElement tc) && tc.ValueKind == JsonValueKind.Object ? Str(tc, "filePath") : null,
			["comments"] = comments,
		};
	}

	/// <summary>Compact comment: <c>{id, author, content, commentType, publishedDate}</c>.</summary>
	public static Dictionary<string, object?> CompactComment(JsonElement c) => new()
	{
		["id"] = Num(c, "id"),
		["author"] = c.TryGetProperty("author", out JsonElement a) ? Str(a, "displayName") : null,
		["content"] = Str(c, "content"),
		["commentType"] = Str(c, "commentType"),
		["publishedDate"] = Str(c, "publishedDate"),
	};

	static string? WebUrl(JsonElement pr)
		=> pr.TryGetProperty("_links", out JsonElement links)
			&& links.TryGetProperty("web", out JsonElement web)
			&& web.TryGetProperty("href", out JsonElement href) && href.ValueKind == JsonValueKind.String
			? href.GetString()
			: null;

	static string? Str(JsonElement obj, string name)
		=> obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

	static bool? Bool(JsonElement obj, string name)
		=> obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) ? e.GetBoolean() : null;

	static int? Num(JsonElement obj, string name)
		=> obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int n) ? n : null;
}
