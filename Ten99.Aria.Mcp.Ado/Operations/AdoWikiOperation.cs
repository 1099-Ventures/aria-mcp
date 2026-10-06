using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Wiki reads. <c>wiki_wiki</c>: list | get. <c>wiki_page</c>: list | get | get_content.
/// Lean projections (ADO #439).
/// </summary>
internal sealed class AdoWikiOperation : AdoOperationBase
{
	public async Task<CallToolResult> Wikis(IConfiguration config, HttpClient httpClient, string action, string project, string? wikiId)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("wiki_wiki", "project is required.", config);
			AdoOrg org = ResolveOrg(config);
			string prefix = $"{Uri.EscapeDataString(project)}/";

			switch (action)
			{
				case "list":
					ApiResult l = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/wiki/wikis", org);
					if (!l.Ok) return ApiError("wiki_wiki", l, config);
					List<object> wikis = [];
					using (JsonDocument doc = JsonDocument.Parse(l.Body))
						if (doc.RootElement.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Array)
							foreach (JsonElement w in v.EnumerateArray())
								wikis.Add(CompactWiki(w));
					return Json(new { count = wikis.Count, wikis }, config);

				case "get":
					if (string.IsNullOrWhiteSpace(wikiId)) return Failure("wiki_wiki", "wikiId is required for get.", config);
					ApiResult g = await SendAsync(config, httpClient, HttpMethod.Get, $"{prefix}_apis/wiki/wikis/{Uri.EscapeDataString(wikiId)}", org);
					if (!g.Ok) return ApiError("wiki_wiki", g, config);
					using (JsonDocument doc = JsonDocument.Parse(g.Body))
						return Json(CompactWiki(doc.RootElement), config);

				default:
					return Failure("wiki_wiki", $"Unknown action '{action}'. Use list|get.", config);
			}
		}
		catch (Exception ex) { return Failure("wiki_wiki", ex.Message, config); }
	}

	public async Task<CallToolResult> Pages(IConfiguration config, HttpClient httpClient, string action, string project, string wikiId, string? path)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(wikiId))
				return Failure("wiki_page", "project and wikiId are required.", config);
			AdoOrg org = ResolveOrg(config);
			string pagesBase = $"{Uri.EscapeDataString(project)}/_apis/wiki/wikis/{Uri.EscapeDataString(wikiId)}/pages";
			string p = string.IsNullOrWhiteSpace(path) ? "/" : path;

			switch (action)
			{
				case "list":
				case "get":
					List<KeyValuePair<string, string>> q = [new("path", p), new("recursionLevel", action == "list" ? "oneLevel" : "none")];
					ApiResult r = await SendAsync(config, httpClient, HttpMethod.Get, pagesBase, org, query: q);
					if (!r.Ok) return ApiError("wiki_page", r, config);
					using (JsonDocument doc = JsonDocument.Parse(r.Body))
						return Json(CompactPage(doc.RootElement, includeSubPages: true), config);

				case "get_content":
					List<KeyValuePair<string, string>> qc = [new("path", p), new("includeContent", "true")];
					ApiResult c = await SendAsync(config, httpClient, HttpMethod.Get, pagesBase, org, query: qc);
					if (!c.Ok) return ApiError("wiki_page", c, config);
					using (JsonDocument doc = JsonDocument.Parse(c.Body))
						return Json(new { path = Str(doc.RootElement, "path") ?? p, content = Str(doc.RootElement, "content") }, config);

				default:
					return Failure("wiki_page", $"Unknown action '{action}'. Use list|get|get_content.", config);
			}
		}
		catch (Exception ex) { return Failure("wiki_page", ex.Message, config); }
	}

	static Dictionary<string, object?> CompactWiki(JsonElement w) => new()
	{
		["id"] = Str(w, "id"),
		["name"] = Str(w, "name"),
		["type"] = Str(w, "type"),
		["mappedPath"] = Str(w, "mappedPath"),
		["remoteUrl"] = Str(w, "remoteUrl"),
	};

	static Dictionary<string, object?> CompactPage(JsonElement pg, bool includeSubPages)
	{
		Dictionary<string, object?> d = new() { ["path"] = Str(pg, "path"), ["order"] = pg.TryGetProperty("order", out JsonElement o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : null };
		if (includeSubPages && pg.TryGetProperty("subPages", out JsonElement sp) && sp.ValueKind == JsonValueKind.Array)
			d["subPages"] = sp.EnumerateArray().Select(s => Str(s, "path")).Where(x => x is not null).ToList();
		return d;
	}

	static string? Str(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
