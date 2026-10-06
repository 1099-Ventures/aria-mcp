using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Ado.Operations;

namespace Ten99.Aria.Mcp.Ado.Tools;

/// <summary>
/// Wiki domain (<c>wiki_*</c>) — read wikis/pages and upsert page content (ADO #439). Project-scoped.
/// </summary>
[McpServerToolType]
public static class AdoWikiTool
{
	[McpServerTool(Name = "wiki_wiki")]
	[Description("Wikis in a project. action: list | get (by wikiId). Lean {id, name, type, mappedPath, remoteUrl}.")]
	public static Task<CallToolResult> Wiki(
		IConfiguration config, HttpClient httpClient,
		[Description("list | get")] string action,
		[Description("Project id or name.")] string project,
		[Description("Wiki id or name. Required for get.")] string? wikiId = null)
		=> new AdoWikiOperation().Wikis(config, httpClient, action, project, wikiId);

	[McpServerTool(Name = "wiki_page")]
	[Description("Wiki pages. action: list (subpages at path) | get (page metadata) | get_content (page text). path defaults to /.")]
	public static Task<CallToolResult> Page(
		IConfiguration config, HttpClient httpClient,
		[Description("list | get | get_content")] string action,
		[Description("Project id or name.")] string project,
		[Description("Wiki id or name.")] string wikiId,
		[Description("Page path, e.g. /Decisions/My-ADR. Defaults to /.")] string? path = null)
		=> new AdoWikiOperation().Pages(config, httpClient, action, project, wikiId, path);

	[McpServerTool(Name = "wiki_page_write")]
	[Description("Create or update a wiki page (upsert). Fetches the current ETag for updates. Returns {success, action: created|updated, path}.")]
	public static Task<CallToolResult> PageWrite(
		IConfiguration config, HttpClient httpClient,
		[Description("Project id or name.")] string project,
		[Description("Wiki id or name.")] string wikiId,
		[Description("Page path, e.g. /Decisions/My-ADR.")] string path,
		[Description("Page content (Markdown).")] string content)
		=> new AdoWikiUpsertOperation().Execute(config, httpClient, project, wikiId, path, content);
}
