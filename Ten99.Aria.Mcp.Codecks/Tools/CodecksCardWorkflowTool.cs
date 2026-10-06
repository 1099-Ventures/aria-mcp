using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>
/// Card workflow states driven by resolvables: a card is "blocked" while it has an open block resolvable,
/// and "in review" while it has an open review resolvable (same mechanism as comments, US 71).
/// </summary>
[McpServerToolType]
public static class CodecksCardWorkflowTool
{
	[McpServerTool(Name = "card_block")]
	[Description("Block a card (marks it blocked) with a reason. REQUIRED: cardId, reason (why it's blocked; supports @[userId:<id>] mentions). Returns the block resolvable; unblock with card_unblock using its id. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> BlockCard(IConfiguration config, HttpClient httpClient, string cardId, string reason)
		=> new CodecksAddCommentOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksAddCommentRequest { CardId = cardId, Content = reason, Context = "block" });

	[McpServerTool(Name = "card_unblock")]
	[Description("Unblock a card by closing its block. REQUIRED: resolvableId (the block id, from comment_list / the card's resolvables). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> UnblockCard(IConfiguration config, HttpClient httpClient, string resolvableId)
		=> new CodecksCloseThreadOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksCloseResolvableRequest { ResolvableId = resolvableId });

	[McpServerTool(Name = "card_request_review")]
	[Description("Request a review on a card (marks it in review). REQUIRED: cardId, note (what to review; supports @[userId:<id>] mentions). Returns the review resolvable; close it with card_resolve_review. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> RequestReview(IConfiguration config, HttpClient httpClient, string cardId, string note)
		=> new CodecksAddCommentOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksAddCommentRequest { CardId = cardId, Content = note, Context = "review" });

	[McpServerTool(Name = "card_resolve_review")]
	[Description("Close a review on a card. REQUIRED: resolvableId (the review id). OPTIONAL: markCardDone (bool, default false) — also mark the card done. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> ResolveReview(IConfiguration config, HttpClient httpClient, string resolvableId, bool markCardDone = false)
		=> new CodecksCloseThreadOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksCloseResolvableRequest { ResolvableId = resolvableId, MarkCardDone = markCardDone });
}
