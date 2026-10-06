using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>
/// Dependency domain (<c>card_*_dependency</c> / <c>card_list_dependencies</c>). A card's inDeps are the
/// cards it depends on / is blocked by; outDeps is the inverse. Set on the dependent card; the inverse
/// reflects automatically.
/// </summary>
[McpServerToolType]
public static class CodecksDependencyTool
{
	[McpServerTool(Name = "card_list_dependencies")]
	[Description("List a card's dependencies. REQUIRED: cardId. Returns the card's inDeps (cards it depends on / is blocked by) and outDeps (cards that depend on it / it blocks), each {cardId, title, derivedStatus}, plus hasBlockingDeps / isBlockingDep flags.")]
	public static Task<CallToolResult> ListDependencies(IConfiguration config, HttpClient httpClient, string cardId)
		=> new CodecksListDependenciesOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, cardId);

	[McpServerTool(Name = "card_add_dependency")]
	[Description("Link two cards as a dependency. REQUIRED: cardId, otherCardId. OPTIONAL: direction — 'blockedBy' (default: cardId is blocked by / depends on otherCardId) or 'blocks' (cardId blocks otherCardId). A single call links both cards (the inverse relation reflects on the other automatically). Read-modify-writes so existing links are preserved. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> AddDependency(IConfiguration config, HttpClient httpClient, string cardId, string otherCardId, string direction = "blockedBy")
		=> new CodecksDependencyOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, CodecksDependencyOperation.Mode.Add, FieldFor(direction), cardId, otherCardId);

	[McpServerTool(Name = "card_remove_dependency")]
	[Description("Remove a dependency link between two cards. REQUIRED: cardId, otherCardId. OPTIONAL: direction — 'blockedBy' (default: remove otherCardId from cardId's blockers) or 'blocks' (remove otherCardId from the cards cardId blocks). A single call unlinks both cards. Read-modify-writes so other links are preserved. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> RemoveDependency(IConfiguration config, HttpClient httpClient, string cardId, string otherCardId, string direction = "blockedBy")
		=> new CodecksDependencyOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, CodecksDependencyOperation.Mode.Remove, FieldFor(direction), cardId, otherCardId);

	// 'blocks' edits cardId.outDeps; anything else ('blockedBy') edits cardId.inDeps.
	static string FieldFor(string direction)
		=> string.Equals(direction, "blocks", StringComparison.OrdinalIgnoreCase) ? "outDeps" : "inDeps";
}
