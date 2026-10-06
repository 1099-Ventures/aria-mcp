using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>
/// Comment domain (<c>comment_*</c>). Comments on a card are "resolvables" (threads) made of
/// "resolvableEntries" (messages). A thread can be closed/reopened; an entry can be edited or reacted to.
/// </summary>
[McpServerToolType]
public static class CodecksCommentTool
{
	[McpServerTool(Name = "comment_list")]
	[Description("List a card's comments. REQUIRED: cardId. Returns {count, comments:[{entryId, content, createdAt, lastChangedAt, resolvableId, author}]} plus a 'resolvable' map of thread state (id, context, isClosed) — correlate a comment to its thread via resolvableId.")]
	public static Task<CallToolResult> ListComments(IConfiguration config, HttpClient httpClient, string cardId)
		=> new CodecksListCommentsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, cardId);

	[McpServerTool(Name = "comment_add")]
	[Description("Add a comment to a card. REQUIRED: cardId, content (markdown; mention a user with @[userId:<id>]). Creates a new comment thread. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> AddComment(IConfiguration config, HttpClient httpClient, string cardId, string content)
		=> new CodecksAddCommentOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksAddCommentRequest { CardId = cardId, Content = content });

	[McpServerTool(Name = "comment_edit")]
	[Description("Edit a comment. REQUIRED: entryId (the comment entry id from comment_list), content (new body). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> EditComment(IConfiguration config, HttpClient httpClient, string entryId, string content)
		=> new CodecksEditCommentOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksEditCommentRequest { EntryId = entryId, Content = content });

	[McpServerTool(Name = "comment_react")]
	[Description("Add an emoji reaction to a comment. REQUIRED: entryId (from comment_list), emoji (e.g. '👍'). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> ReactComment(IConfiguration config, HttpClient httpClient, string entryId, string emoji)
		=> new CodecksReactCommentOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksReactCommentRequest { EntryId = entryId, Emoji = emoji });

	[McpServerTool(Name = "comment_close")]
	[Description("Close (resolve) a comment thread. REQUIRED: resolvableId (the thread id from comment_list). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> CloseThread(IConfiguration config, HttpClient httpClient, string resolvableId)
		=> new CodecksCloseThreadOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksCloseResolvableRequest { ResolvableId = resolvableId });

	[McpServerTool(Name = "comment_reopen")]
	[Description("Reopen a closed comment thread. REQUIRED: resolvableId (the thread id from comment_list). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> ReopenThread(IConfiguration config, HttpClient httpClient, string resolvableId)
		=> new CodecksReopenThreadOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, resolvableId);
}
