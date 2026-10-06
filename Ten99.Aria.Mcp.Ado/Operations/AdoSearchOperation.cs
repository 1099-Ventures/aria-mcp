using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>search_*</c> — text search via the Search service (<c>almsearch.dev.azure.com</c>, reusing
/// <see cref="AdoOperationBase.SendAbsoluteAsync"/>): code, wiki, and work items. Lean, trimmed
/// results (ADO #441).
/// </summary>
internal sealed class AdoSearchOperation : AdoOperationBase
{
	public Task<CallToolResult> Code(IConfiguration config, HttpClient httpClient, string searchText, string? project, string? repository, string? path, string? branch, int? top, int? skip)
	{
		Dictionary<string, string[]> f = [];
		Add(f, "Project", project); Add(f, "Repository", repository); Add(f, "Path", path); Add(f, "Branch", branch);
		return SearchAsync(config, httpClient, "codesearchresults", searchText, f, top, skip, ProjectCode, "search_code");
	}

	public Task<CallToolResult> Wiki(IConfiguration config, HttpClient httpClient, string searchText, string? project, string? wiki, int? top, int? skip)
	{
		Dictionary<string, string[]> f = [];
		Add(f, "Project", project); Add(f, "Wiki", wiki);
		return SearchAsync(config, httpClient, "wikisearchresults", searchText, f, top, skip, ProjectWiki, "search_wiki");
	}

	public Task<CallToolResult> WorkItem(IConfiguration config, HttpClient httpClient, string searchText, string? project, string? workItemType, string? state, string? assignedTo, int? top, int? skip)
	{
		Dictionary<string, string[]> f = [];
		Add(f, "System.TeamProject", project); Add(f, "System.WorkItemType", workItemType); Add(f, "System.State", state); Add(f, "System.AssignedTo", assignedTo);
		return SearchAsync(config, httpClient, "workitemsearchresults", searchText, f, top, skip, ProjectWorkItem, "search_workitem");
	}

	async Task<CallToolResult> SearchAsync(
		IConfiguration config, HttpClient httpClient, string endpoint, string searchText,
		Dictionary<string, string[]> filters, int? top, int? skip,
		Func<JsonElement, object> project, string op)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(searchText))
				return Failure(op, "searchText is required.", config);

			AdoOrg org = ResolveOrg(config);
			Dictionary<string, object?> body = new()
			{
				["searchText"] = searchText,
				["$skip"] = skip is >= 0 ? skip.Value : 0,
				["$top"] = top is > 0 ? top.Value : 15,
			};
			if (filters.Count > 0) body["filters"] = filters;

			string url = $"https://almsearch.dev.azure.com/{Uri.EscapeDataString(org.OrgName)}/_apis/search/{endpoint}?api-version={ApiVersion}";
			ApiResult r = await SendAbsoluteAsync(config, httpClient, HttpMethod.Post, url, org, JsonSerializer.Serialize(body, JsonRequestOptions));
			if (!r.Ok) return ApiError(op, r, config);

			List<object> results = [];
			using JsonDocument doc = JsonDocument.Parse(r.Body);
			JsonElement root = doc.RootElement;
			int count = root.TryGetProperty("count", out JsonElement c) && c.TryGetInt32(out int n) ? n : 0;
			if (root.TryGetProperty("results", out JsonElement rs) && rs.ValueKind == JsonValueKind.Array)
				foreach (JsonElement e in rs.EnumerateArray())
					results.Add(project(e));

			return Json(new { count, results }, config);
		}
		catch (Exception ex)
		{
			return Failure(op, ex.Message, config);
		}
	}

	static object ProjectCode(JsonElement e) => new
	{
		fileName = Str(e, "fileName"),
		path = Str(e, "path"),
		repository = e.TryGetProperty("repository", out JsonElement r) ? Str(r, "name") : null,
		project = e.TryGetProperty("project", out JsonElement p) ? Str(p, "name") : null,
	};

	static object ProjectWiki(JsonElement e) => new
	{
		fileName = Str(e, "fileName"),
		path = Str(e, "path"),
		wiki = e.TryGetProperty("wiki", out JsonElement w) ? Str(w, "name") : null,
		project = e.TryGetProperty("project", out JsonElement p) ? Str(p, "name") : null,
	};

	static object ProjectWorkItem(JsonElement e)
	{
		string? Field(string key) => e.TryGetProperty("fields", out JsonElement f) && f.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
		return new
		{
			id = Field("system.id"),
			title = Field("system.title"),
			type = Field("system.workitemtype"),
			state = Field("system.state"),
			project = e.TryGetProperty("project", out JsonElement p) ? Str(p, "name") : null,
		};
	}

	static void Add(Dictionary<string, string[]> f, string key, string? csv)
	{
		if (string.IsNullOrWhiteSpace(csv)) return;
		string[] vals = csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (vals.Length > 0) f[key] = vals;
	}

	static string? Str(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
