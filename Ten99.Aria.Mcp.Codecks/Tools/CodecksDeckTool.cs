using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Linq;
using Ten99.Aria.Mcp.Codecks;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>Deck domain (<c>deck_*</c>) — decks within projects (ADO #437).</summary>
[McpServerToolType]
public static class CodecksDeckTool
{
	[McpServerTool(Name = "deck_get_detail")]
	[Description("✅ TESTED & READY - Get detailed information about a specific deck including preview of cards. Fixed field names and enhanced with comprehensive deck metadata.")]
	public static Task<CallToolResult> GetDeckDetail(IConfiguration config, HttpClient httpClient, string deckId, int offset = 0)
		=> new CodecksGetDeckDetailOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, new CodecksDetailRequest(deckId, offset));

	[McpServerTool(Name = "deck_list_by_project")]
	[Description("✅ TESTED & READY - List all decks within a specific project using trimmed payload optimization. Returns essential deck fields (id, title, createdAt, sortValue) with strategic count indicators (count:cards, count:guardians) for performance-optimized project overview. Includes minimal context relations (project, creator) only. Ordered by sortValue for explicit deck sequence, with createdAt fallback. Returns {count, decks:[…], project, user} — top-level count is 0 on empty.")]
	public static Task<CallToolResult> ListDecksByProject(IConfiguration config, HttpClient httpClient, string projectId, int offset = 0)
		=> new CodecksListDecksByProjectOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, new CodecksDetailRequest(projectId, offset));

	[McpServerTool(Name = "deck_create")]
	[Description("Create a new deck in a Codecks project. REQUIRED: projectId (string - use project_list to look up), title (string). OPTIONAL: description (string), spaceId (int - the project space to place the deck in; omit to use the project's default space). Returns the created deck's id. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> CreateDeck(IConfiguration config, HttpClient httpClient, string projectId, string title, string? description = null, int? spaceId = null)
		=> new CodecksCreateDeckOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksCreateDeckRequest { ProjectId = projectId, Title = title, Description = description, SpaceId = spaceId });

	[McpServerTool(Name = "deck_update")]
	[Description("Rename or re-describe an existing Codecks deck. REQUIRED: deckId (string). OPTIONAL: title (string - the new name), description (string). Provide at least one of title or description. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> UpdateDeck(IConfiguration config, HttpClient httpClient, string deckId, string? title = null, string? description = null)
		=> new CodecksUpdateDeckOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksUpdateDeckRequest { DeckId = deckId, Title = title, Description = description });

	[McpServerTool(Name = "deck_delete")]
	[Description("Delete a Codecks deck by id (dispatch/decks/delete). REQUIRED: deckId (string). This is permanent — move or delete the deck's cards first if you want to keep them. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> DeleteDeck(IConfiguration config, HttpClient httpClient, string deckId)
		=> new CodecksDeleteDeckOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, deckId);

	[McpServerTool(Name = "deck_set_cover")]
	[Description("Set a deck's cover and/or overlay colour. REQUIRED: deckId. OPTIONAL (at least one): stockCover (a built-in cover name — call deck_list_stock_covers for the list), imagePath (path to a local image on the machine running the MCP — uploaded, max 10 MB), coverColor (hex, e.g. '#ff7800'). stockCover and imagePath are mutually exclusive. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> SetDeckCover(IConfiguration config, HttpClient httpClient, string deckId, string? imagePath = null, string? coverColor = null, string? stockCover = null)
		=> new CodecksSetDeckCoverOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, deckId, imagePath, coverColor, stockCover);

	[McpServerTool(Name = "deck_list_stock_covers")]
	[Description("List the built-in (stock) deck cover images by name. Returns {count, covers:[{name, url}]}. Use a name as deck_set_cover's stockCover.")]
	public static CallToolResult ListStockCovers()
	{
		var covers = CodecksStockCovers.ByName.Select(kv => new { name = kv.Key, url = kv.Value }).ToArray();
		string text = System.Text.Json.JsonSerializer.Serialize(new { count = covers.Length, covers });
		return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
	}

	[McpServerTool(Name = "deck_move")]
	[Description("Move a deck into a space (optionally positioned after a sibling deck). REQUIRED: deckId, targetProjectId (the destination project — same as the deck's project for an in-project move), targetSpaceId (int, the destination space). OPTIONAL: afterDeckId (place the deck right after this sibling deck in the space). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> MoveDeck(IConfiguration config, HttpClient httpClient, string deckId, string targetProjectId, int targetSpaceId, string? afterDeckId = null)
		=> new CodecksMoveDeckOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksMoveDeckRequest { DeckId = deckId, TargetProjectId = targetProjectId, TargetSpaceId = targetSpaceId, AfterDeckId = afterDeckId });
}
