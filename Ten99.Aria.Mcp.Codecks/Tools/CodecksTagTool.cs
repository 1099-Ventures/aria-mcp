using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>Tag domain (<c>tag_*</c>) — project-scoped tags (ADO #83).</summary>
[McpServerToolType]
public static class CodecksTagTool
{
	[McpServerTool(Name = "tag_list")]
	[Description("List tags in a Codecks project. Tags are project-scoped; returns {count, tags:[{id, name, color}]}. Use a tag id to filter card_search by tag (tagId).")]
	public static Task<CallToolResult> ListTags(IConfiguration config, HttpClient httpClient, string projectId)
		=> new CodecksListTagsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, new CodecksDetailRequest(projectId));

	[McpServerTool(Name = "tag_add")]
	[Description("Add a tag to a Codecks project. REQUIRED: projectId (string), tag (string - the tag label/value). Tags are project-level (use tag_list to see existing ones). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> AddTag(IConfiguration config, HttpClient httpClient, string projectId, string tag)
		=> new CodecksAddTagOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksAddTagRequest { ProjectId = projectId, Tag = tag });

	[McpServerTool(Name = "tag_update")]
	[Description("Edit a project tag by id. REQUIRED: id (tag id from tag_list). OPTIONAL (provide at least one): tag (the label), description, color (hex, e.g. '#C13642'), emoji. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> UpdateTag(IConfiguration config, HttpClient httpClient, string id, string? tag = null, string? description = null, string? color = null, string? emoji = null)
		=> new CodecksUpdateTagOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksUpdateTagRequest { Id = id, Tag = tag, Description = description, Color = color, Emoji = emoji });

	[McpServerTool(Name = "tag_remove")]
	[Description("Delete project tag(s) by id. REQUIRED: tagIds (string array - tag ids from tag_list). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> RemoveTags(IConfiguration config, HttpClient httpClient, string[] tagIds)
		=> new CodecksDeleteTagsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, tagIds ?? []);
}
