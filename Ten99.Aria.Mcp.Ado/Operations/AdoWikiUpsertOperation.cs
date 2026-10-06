using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// <c>wiki_page_write</c> — create or update a wiki page. Fetches the current ETag (if the page
/// exists) and PUTs with <c>If-Match</c> for updates; creates without it (ADO #439).
/// </summary>
internal sealed class AdoWikiUpsertOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(IConfiguration config, HttpClient httpClient, string project, string wikiId, string path, string content)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(wikiId) || string.IsNullOrWhiteSpace(path))
				return Failure("wiki_page_write", "project, wikiId and path are required.", config);
			if (content is null)
				return Failure("wiki_page_write", "content is required.", config);

			AdoOrg org = ResolveOrg(config);
			string pagesBase = $"{Uri.EscapeDataString(project)}/_apis/wiki/wikis/{Uri.EscapeDataString(wikiId)}/pages";
			List<KeyValuePair<string, string>> q = [new("path", path)];

			// Look up the current page to decide create vs update and capture its ETag.
			var probe = await SendWithETagAsync(config, httpClient, HttpMethod.Get, pagesBase, org, query: q);
			string? ifMatch = probe.Ok ? probe.ETag : null;
			bool exists = probe.Ok;

			string body = JsonSerializer.Serialize(new { content }, JsonRequestOptions);
			var put = await SendWithETagAsync(config, httpClient, HttpMethod.Put, pagesBase, org, jsonBody: body, ifMatch: ifMatch, query: q);
			if (!put.Ok)
				return Json(new { success = false, operation = "wiki_page_write", statusCode = (int)put.Status, error = Summarize(put.Body) }, config);

			return Json(new { success = true, action = exists ? "updated" : "created", path }, config);
		}
		catch (Exception ex) { return Failure("wiki_page_write", ex.Message, config); }
	}

	static string Summarize(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String)
				return m.GetString() ?? body;
		}
		catch (JsonException) { }
		return body.Length > 400 ? body[..400] : body;
	}
}
