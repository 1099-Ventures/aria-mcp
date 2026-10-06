using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>
/// Space domain (<c>space_*</c>). Spaces group decks within a project. They are not a standalone entity:
/// each tool read-modify-writes the project's <c>spaces</c> array via <c>dispatch/projects/update</c>.
/// </summary>
[McpServerToolType]
public static class CodecksSpaceTool
{
	[McpServerTool(Name = "space_create")]
	[Description("Create a space in a Codecks project. Spaces group decks. REQUIRED: projectId (string), name (string). OPTIONAL: icon (string token, e.g. 'default', 'journey', 'robot', 'gdd', 'knowledge', 'qa', 'tasks'; null when omitted), defaultDeckType ('mixed' (default), 'task', 'doc', or 'hero'). Reads the project's current spaces, appends the new one (its id is a per-project integer), and writes the array back. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> CreateSpace(IConfiguration config, HttpClient httpClient, string projectId, string name, string? icon = null, string? defaultDeckType = null)
		=> new CodecksSpacesOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, CodecksSpacesOperation.Mode.Create, projectId, null, name, icon, defaultDeckType);

	[McpServerTool(Name = "space_update")]
	[Description("Rename or re-style a space in a Codecks project. REQUIRED: projectId (string), spaceId (int - from a project's spaces array, e.g. via project_get_detail). OPTIONAL: name (string - new name), icon (string token, e.g. 'default', 'journey', 'robot', 'gdd', 'knowledge', 'qa', 'tasks'), defaultDeckType ('mixed', 'task', 'doc', or 'hero'). Provide at least one. Read-modify-writes the project's spaces array. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> UpdateSpace(IConfiguration config, HttpClient httpClient, string projectId, int spaceId, string? name = null, string? icon = null, string? defaultDeckType = null)
		=> new CodecksSpacesOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, CodecksSpacesOperation.Mode.Update, projectId, spaceId, name, icon, defaultDeckType);

	[McpServerTool(Name = "space_reorder")]
	[Description("Reorder the spaces in a Codecks project (the array order is the display order). REQUIRED: projectId (string), orderedSpaceIds (int array - space ids in the desired order). Ids listed appear first in that order; any existing spaces you omit keep their current relative order after them, so none are dropped. Unknown ids are rejected. Read-modify-writes the project's spaces array. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> ReorderSpaces(IConfiguration config, HttpClient httpClient, string projectId, int[] orderedSpaceIds)
		=> new CodecksSpacesOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ReorderAsync(config, httpClient, projectId, orderedSpaceIds);

	[McpServerTool(Name = "space_delete")]
	[Description("Delete a space from a Codecks project by id. REQUIRED: projectId (string), spaceId (int). OPTIONAL: force (bool, default false). Deleting a space does NOT delete its decks — Codecks silently reassigns them to another space — so by default this refuses to delete a space that still contains decks; move or delete them first, or pass force=true to delete anyway and accept the reassignment. Read-modify-writes the project's spaces array. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> DeleteSpace(IConfiguration config, HttpClient httpClient, string projectId, int spaceId, bool force = false)
		=> new CodecksSpacesOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, CodecksSpacesOperation.Mode.Delete, projectId, spaceId, null, null, null, force);
}
