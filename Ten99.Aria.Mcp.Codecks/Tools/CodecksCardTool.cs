using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>Card domain (<c>card_*</c>) — search, detail, list-by-deck, create, update (ADO #437).</summary>
[McpServerToolType]
public static class CodecksCardTool
{
	// The MCP owns the status rules so callers never hit a raw Codecks 500 for a bad value.
	// Codecks card workflow statuses; 'blocked' is NOT one (it's the derived hasBlockingDeps).
	private static readonly HashSet<string> ValidCardStatuses = new(StringComparer.OrdinalIgnoreCase)
	{
		"not_started", "started", "snoozing", "done"
	};

	private static CallToolResult ErrorResult(string message)
		=> new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { success = false, error = message }) }] };

	[McpServerTool(Name = "card_search")]
	[Description("✅ TESTED & READY - Search cards across the Codecks workspace. RECOMMENDED: pass projectId to scope to the project you're working in (so you don't surface a card from an unrelated project). Optional deckId narrows further (deck > project > account-wide). accountId is OPTIONAL: when omitted and no projectId/deckId is given, the active account (resolved from the token) is used for an account-wide search. Optional filters: accountSeq (the human card number), titleContains (substring), status, assigneeId, docFilter ('all' default | 'exclude' for work cards only | 'only' for docs), derivedStatus (comma-separated include-filter over the real triage signal: assigned|unassigned|hero|archived|done|doc — e.g. 'assigned' for open+assigned), blocked ('all' default | 'only' for blocked | 'exclude' for unblocked — 'blocked' is the dependency state hasBlockingDeps, NOT a status or tag), tag (keep only cards carrying this tag value — a projectTag GUID from tag_list, a hero-card accountSeq as a string since hero cards act as tags/epics, or the literal 'default' for a deck's default tag). Returns a trimmed shape {cardId, accountSeq, title, deckId, status, derivedStatus, isDoc, hasBlockingDeps, tags} and a top-level count - use card_get_detail for full card data. Supports limit (default 30) and offset.")]
	public static async Task<CallToolResult> SearchCards(
		IConfiguration config,
		HttpClient httpClient,
		string? accountId = null,
		string? projectId = null,
		string? deckId = null,
		int? accountSeq = null,
		string? titleContains = null,
		string? status = null,
		string? assigneeId = null,
		string? docFilter = null,
		string? derivedStatus = null,
		string? blocked = null,
		string? tag = null,
		int offset = 0,
		int limit = 30)
	{
		// Scope is deck > project > account-wide. Only the account-wide branch needs an account id,
		// so resolve it from the token when the caller omits all three (card_get_detail does the same).
		if (string.IsNullOrWhiteSpace(accountId) && string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(deckId))
		{
			accountId = await ResolveAccountIdAsync(config, httpClient);
			if (string.IsNullOrEmpty(accountId))
				return ErrorResult("Could not resolve the active account for an account-wide search. Pass accountId, projectId, or deckId.");
		}

		var request = new CodecksSearchCardsRequest
		{
			AccountId = accountId,
			ProjectId = projectId,
			DeckId = deckId,
			AccountSeq = accountSeq,
			TitleContains = titleContains,
			Status = status,
			AssigneeId = assigneeId,
			DocFilter = docFilter,
			DerivedStatus = derivedStatus,
			Blocked = blocked,
			Tag = tag,
			Offset = offset,
			Limit = limit
		};

		return await new CodecksSearchCardsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, request);
	}

	[McpServerTool(Name = "card_get_detail")]
	[Description("✅ TESTED & READY - Get comprehensive detailed information about a specific card. Accepts EITHER the card UUID OR the human card number (accountSeq, e.g. 1141) - a numeric id is resolved to the UUID internally before loading (Codecks returns an opaque HTTP 500 if handed a card number directly). Returns card metadata with deck context, assignee/creator, parent card, milestone, tags, and count fields.")]
	public static async Task<CallToolResult> GetCardDetail(IConfiguration config, HttpClient httpClient, string cardId, int offset = 0)
	{
		string resolvedId = cardId;

		// A purely numeric id is a card number (accountSeq), not a UUID. Resolve it first so we
		// never hand Codecks a bad id (which it answers with an unhandled 500). Resolution needs
		// the account GUID (cards nest under account(<id>)), so fetch it via get_account_info.
		if (!string.IsNullOrWhiteSpace(cardId) && cardId.All(char.IsDigit) && int.TryParse(cardId, out int seq))
		{
			string? accountId = await ResolveAccountIdAsync(config, httpClient);
			if (string.IsNullOrEmpty(accountId))
				return ErrorResult("Could not resolve the active account to look up the card number.");

			// Reuse the proven search operation (account(<id>)->cards with $order/$offset) rather than
			// a bespoke query — a hand-rolled resolve query that omitted $order came back empty.
			CallToolResult resolveResult = await new CodecksSearchCardsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
				.Execute(config, httpClient, new CodecksSearchCardsRequest { AccountId = accountId, AccountSeq = seq, Limit = 1 });
			string? uuid = ExtractFirstCardId(resolveResult);
			if (string.IsNullOrEmpty(uuid))
				return ErrorResult($"No card found with number #{seq} in the active account.");

			resolvedId = uuid;
		}

		return await new CodecksGetCardDetailOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, new CodecksDetailRequest(resolvedId, offset));
	}

	/// <summary>Pulls the active account GUID (_root.account) out of a get_account_info response.</summary>
	/// <summary>Resolves the active account GUID from the token via <c>org_get_account_info</c> (used to scope account-wide card queries).</summary>
	private static async Task<string?> ResolveAccountIdAsync(IConfiguration config, HttpClient httpClient)
	{
		CallToolResult accountResult = await new CodecksGetAccountInfoOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient);
		return ExtractAccountId(accountResult);
	}

	private static string? ExtractAccountId(CallToolResult result)
	{
		if (result.Content.FirstOrDefault() is not TextContentBlock block || string.IsNullOrEmpty(block.Text))
			return null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(block.Text);
			if (doc.RootElement.TryGetProperty("_root", out JsonElement root)
				&& root.TryGetProperty("account", out JsonElement account)
				&& account.ValueKind == JsonValueKind.String)
				return account.GetString();
		}
		catch (JsonException) { /* fall through to null */ }
		return null;
	}

	/// <summary>
	/// Pulls the first card UUID out of a <see cref="CodecksSearchCardsOperation"/> result. That operation
	/// reshapes the raw Codecks response into the flat <c>{count, cards:[{cardId,…}]}</c> form, so read the
	/// first element's <c>cardId</c> — not a "card" entity map (which the formatted shape no longer carries).
	/// </summary>
	private static string? ExtractFirstCardId(CallToolResult result)
	{
		if (result.Content.FirstOrDefault() is not TextContentBlock block || string.IsNullOrEmpty(block.Text))
			return null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(block.Text);
			if (doc.RootElement.TryGetProperty("cards", out JsonElement cards)
				&& cards.ValueKind == JsonValueKind.Array)
				foreach (JsonElement entry in cards.EnumerateArray())
					if (entry.TryGetProperty("cardId", out JsonElement id) && id.ValueKind == JsonValueKind.String)
						return id.GetString();
		}
		catch (JsonException) { /* fall through to null — caller returns a clean not-found */ }
		return null;
	}

	[McpServerTool(Name = "card_list_by_deck")]
	[Description("✅ TESTED & READY - List all cards within a specific deck with title + content + strategic count hints. Exception to trimmed payload strategy: returns card title and content for deck-focused card browsing, plus strategic count fields (count:comments, count:attachments, count:activities, count:childCards, count:blockedCards, count:blockingCards) to indicate available detail depth. Includes context relations (assignee, creator, deck) and uses chronological ordering with reasonable deck-size limits. Integrates with card_get_detail through count indicators for list → detail workflow. Returns {count, cards:[…], deck, …relation maps} — top-level count is 0 on empty.")]
	public static Task<CallToolResult> ListCardsByDeck(IConfiguration config, HttpClient httpClient, string deckId, int offset = 0)
		=> new CodecksListCardsByDeckOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, new CodecksDetailRequest(deckId, offset));

	[McpServerTool(Name = "card_create")]
	[Description("✅ TESTED & READY - Create a new card in a Codecks deck. REQUIRED: deckId (string) and content (string - supports Markdown formatting). OPTIONAL: effort (int - MUST be Fibonacci number: 0,1,2,3,5,8,13,21... warns if >8), priority (string - MUST be 'a' (high), 'b' (medium), or 'c' (low)), assigneeId (string - user GUID, use user_list to lookup), dueDate (string - ISO 8601 format, e.g., '2024-12-31T23:59:59Z'), subscribeCreator (bool), putInQueue (bool), addAsBookmark (bool), isDoc (bool), masterTags (string array of projectTag ids from tag_list — sets the card's tags), milestoneId (string — assign to a milestone), sprintId (string — assign to a run/sprint). Returns the created card details with validation warnings if applicable.")]
	public static Task<CallToolResult> CreateCard(
		IConfiguration config,
		HttpClient httpClient,
		string deckId,
		string content,
		int? effort = null,
		string? priority = null,
		string? assigneeId = null,
		string? dueDate = null,
		bool? subscribeCreator = null,
		bool? putInQueue = null,
		bool? addAsBookmark = null,
		bool? isDoc = null,
		string[]? masterTags = null,
		string? milestoneId = null,
		string? sprintId = null)
	{
		var request = new CodecksCreateCardRequest
		{
			DeckId = deckId,
			Content = content,
			Effort = effort,
			Priority = priority,
			AssigneeId = assigneeId,
			DueDate = dueDate,
			SubscribeCreator = subscribeCreator,
			PutInQueue = putInQueue,
			AddAsBookmark = addAsBookmark,
			IsDoc = isDoc,
			MasterTags = masterTags,
			MilestoneId = milestoneId,
			SprintId = sprintId
		};

		return new CodecksCreateCardOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, request);
	}

	[McpServerTool(Name = "card_update")]
	[Description("✅ TESTED & READY - Update an existing card in Codecks. REQUIRED: cardId (string - card GUID to update). OPTIONAL: title (string), content (string - supports Markdown), effort (int - MUST be Fibonacci number: 0,1,2,3,5,8,13,21... warns if >8), priority (string - MUST be 'a' (high), 'b' (medium), or 'c' (low)), assigneeId (string - user GUID), dueDate (string - ISO 8601 format), deckId (string - move to different deck), milestoneId (string), sprintId (string), visibility (string - 'normal' or 'archived' for soft delete), status (string), masterTags (string array of projectTag ids from tag_list — replaces the card's tags). All fields except cardId are optional - only provide the fields you want to update. Returns updated card confirmation with validation warnings if applicable.")]
	public static Task<CallToolResult> UpdateCard(
		IConfiguration config,
		HttpClient httpClient,
		string cardId,
		string? title = null,
		string? content = null,
		int? effort = null,
		string? priority = null,
		string? assigneeId = null,
		string? dueDate = null,
		string? deckId = null,
		string? milestoneId = null,
		string? sprintId = null,
		string? visibility = null,
		string? status = null,
		string[]? masterTags = null)
	{
		// Purely client-side guard: reject an invalid status before any API call (no opaque 500).
		if (!string.IsNullOrWhiteSpace(status) && !ValidCardStatuses.Contains(status.Trim()))
			return Task.FromResult(ErrorResult(
				$"Invalid status '{status}'. Codecks card status must be one of: not_started, started, snoozing, done. " +
				"('blocked' is a derived property (hasBlockingDeps), not a settable status.)"));

		var request = new CodecksUpdateCardRequest
		{
			CardId = cardId,
			Title = title,
			Content = content,
			Effort = effort,
			Priority = priority,
			AssigneeId = assigneeId,
			DueDate = dueDate,
			DeckId = deckId,
			MilestoneId = milestoneId,
			SprintId = sprintId,
			Visibility = visibility,
			Status = status,
			MasterTags = masterTags
		};

		return new CodecksUpdateCardOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, request);
	}
}
