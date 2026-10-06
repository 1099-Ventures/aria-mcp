using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>repo_file</c> — read repository items. Actions: <c>get_content</c> (text of a file at a
/// branch/tag/commit), <c>list_directory</c> (files/folders at a path). Optional version descriptor
/// (ADO #436).
/// </summary>
internal sealed class AdoFileOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config, HttpClient httpClient, string action,
		string? repositoryId, string? path, string? project, string? version, string? versionType, bool recursive)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(repositoryId))
				return Failure("repo_file", "repositoryId is required.", config);

			AdoOrg org = ResolveOrg(config);
			string prefix = string.IsNullOrEmpty(project) ? "" : $"{Uri.EscapeDataString(project)}/";
			string itemsBase = $"{prefix}_apis/git/repositories/{Uri.EscapeDataString(repositoryId)}/items";

			List<KeyValuePair<string, string>> version_ = [];
			if (!string.IsNullOrWhiteSpace(version))
			{
				version_.Add(new("versionDescriptor.version", version));
				version_.Add(new("versionDescriptor.versionType", NormalizeVersionType(versionType)));
			}

			switch (action)
			{
				case "get_content":
				{
					if (string.IsNullOrWhiteSpace(path))
						return Failure("repo_file", "path is required for get_content.", config);
					List<KeyValuePair<string, string>> q = [new("path", path), new("includeContent", "true")];
					q.AddRange(version_);
					ApiResult r = await SendAsync(config, httpClient, HttpMethod.Get, itemsBase, org, query: q);
					if (!r.Ok) return ApiError("repo_file", r, config);
					using JsonDocument doc = JsonDocument.Parse(r.Body);
					string? contentText = doc.RootElement.TryGetProperty("content", out JsonElement ce) && ce.ValueKind == JsonValueKind.String ? ce.GetString() : null;
					return Json(new { path, content = contentText ?? r.Body }, config);
				}

				case "list_directory":
				{
					string scope = string.IsNullOrWhiteSpace(path) ? "/" : path;
					List<KeyValuePair<string, string>> q = [new("scopePath", scope), new("recursionLevel", recursive ? "full" : "oneLevel")];
					q.AddRange(version_);
					ApiResult r = await SendAsync(config, httpClient, HttpMethod.Get, itemsBase, org, query: q);
					if (!r.Ok) return ApiError("repo_file", r, config);
					List<object> items = [];
					using JsonDocument doc = JsonDocument.Parse(r.Body);
					if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
						foreach (JsonElement it in v.EnumerateArray())
							items.Add(new
							{
								path = Str(it, "path"),
								isFolder = it.TryGetProperty("isFolder", out JsonElement f) && f.ValueKind == JsonValueKind.True,
								gitObjectType = Str(it, "gitObjectType"),
								commitId = Str(it, "commitId"),
							});
					return Json(new { path = scope, count = items.Count, items }, config);
				}

				default:
					return Failure("repo_file", $"Unknown action '{action}'. Use get_content|list_directory.", config);
			}
		}
		catch (Exception ex)
		{
			return Failure("repo_file", ex.Message, config);
		}
	}

	static string NormalizeVersionType(string? versionType) => versionType?.Trim().ToLowerInvariant() switch
	{
		"branch" => "branch",
		"tag" => "tag",
		_ => "commit",
	};

	static string? Str(JsonElement o, string n) => o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
