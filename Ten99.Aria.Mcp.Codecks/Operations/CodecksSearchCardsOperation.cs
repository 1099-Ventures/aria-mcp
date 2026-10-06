using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

internal class CodecksSearchCardsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksSearchCardsOperation, CodecksSearchCardsRequest>(rateLimiter, ref lastReset)
{
	// Trimmed, caller-relevant card shape — consistent with the other trimmed list tools.
	// derivedStatus + isDoc are the "what kind of card is this" signals (field-report N1/N8): status
	// alone can't tell open-and-assigned from abandoned, and doc cards masquerade as not_started work.
	private static readonly string[] CardFields = ["cardId", "accountSeq", "title", "deckId", "status", "derivedStatus", "isDoc", "hasBlockingDeps", "tags"];

	// Captured from the request so FormatResponse can apply the doc filter client-side (safe — no risk
	// of a Codecks 500 on an unsupported server-side filter).
	private string? _docFilter;
	private HashSet<string>? _derivedStatuses;
	private string? _blocked;
	private string? _tag;

	protected override object BuildRequest(CodecksSearchCardsRequest request)
	{
		_docFilter = request.DocFilter;
		_derivedStatuses = string.IsNullOrWhiteSpace(request.DerivedStatus)
			? null
			: request.DerivedStatus.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(s => s.ToLowerInvariant()).ToHashSet();
		_blocked = request.Blocked;
		_tag = string.IsNullOrWhiteSpace(request.Tag) ? null : request.Tag.Trim();

		Dictionary<string, object> filter = new()
		{
			["$limit"] = request.Limit ?? 30,
			["$offset"] = request.Offset,
			["$order"] = new[] { "accountSeq" },
		};

		if (request.AccountSeq is int seq)
			filter["accountSeq"] = seq;
		if (!string.IsNullOrWhiteSpace(request.TitleContains))
			filter["title"] = new Dictionary<string, object> { ["op"] = "contains", ["value"] = request.TitleContains };
		if (!string.IsNullOrWhiteSpace(request.Status))
			filter["status"] = request.Status;
		if (!string.IsNullOrWhiteSpace(request.AssigneeId))
			filter["assigneeId"] = request.AssigneeId;
		if (!string.IsNullOrWhiteSpace(request.DeckId))
			filter["deckId"] = request.DeckId;

		Dictionary<object, object> cardsLevel = new()
		{
			[new CodecksFilterKey("cards", filter)] = CardFields,
		};

		// Scope: deck > project > account-wide. Cards aren't a direct child of project,
		// so project scope nests through decks.
		Dictionary<string, object> query;
		if (!string.IsNullOrWhiteSpace(request.DeckId))
			query = new() { [$"deck({request.DeckId})"] = new object[] { cardsLevel } };
		else if (!string.IsNullOrWhiteSpace(request.ProjectId))
			query = new() { [$"project({request.ProjectId})"] = new object[] { new Dictionary<string, object> { ["decks"] = new object[] { cardsLevel } } } };
		else
			query = new() { [$"account({request.AccountId})"] = new object[] { cardsLevel } };

		return new { query };
	}

	// #3b: a project-scoped search's raw Codecks response echoes every deck (most with no matching
	// cards). The `card` entity map already holds ONLY the matched cards in the trimmed shape, so
	// project that into a flat {count, cards[]} and drop the deck echo + _root entirely.
	protected override CallToolResult FormatResponse(string response)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			JsonElement root = doc.RootElement;
			// exclude → drop doc cards (work only); only → keep only docs; all/null → keep everything.
			bool? wantDoc = _docFilter?.Trim().ToLowerInvariant() switch { "only" => true, "exclude" => false, _ => (bool?)null };
				// blocked is a dependency state (hasBlockingDeps), not a status or tag. only → only blocked; exclude → only unblocked.
				bool? wantBlocked = _blocked?.Trim().ToLowerInvariant() switch { "only" => true, "exclude" => false, _ => (bool?)null };

			List<object> cards = [];
			if (root.TryGetProperty("card", out JsonElement cardMap) && cardMap.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty entry in cardMap.EnumerateObject())
				{
					JsonElement c = entry.Value;
					bool isDocCard = Bool(c, "isDoc") == true || Str(c, "derivedStatus") == "doc";
					if (wantDoc is bool wd && isDocCard != wd)
						continue;
					if (_derivedStatuses is { Count: > 0 })
					{
						string? ds = Str(c, "derivedStatus")?.ToLowerInvariant();
						if (ds is null || !_derivedStatuses.Contains(ds))
							continue;
					}
					if (wantBlocked is bool wb && (Bool(c, "hasBlockingDeps") == true) != wb)
							continue;
						string[] tags = Tags(c);
						if (_tag is not null && !tags.Contains(_tag, StringComparer.Ordinal))
							continue;
						cards.Add(new
					{
						cardId = Str(c, "cardId") ?? entry.Name,
						accountSeq = Num(c, "accountSeq"),
						title = Str(c, "title"),
						deckId = Str(c, "deckId"),
						status = Str(c, "status"),
						derivedStatus = Str(c, "derivedStatus"),
						isDoc = Bool(c, "isDoc"),
							hasBlockingDeps = Bool(c, "hasBlockingDeps"),
							tags,
					});
				}
			string text = JsonSerializer.Serialize(new { count = cards.Count, cards }, JsonResponseOptions);
			return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
		}
		catch (JsonException)
		{
			return base.FormatResponse(response); // unexpected shape — fall back to the raw payload
		}
	}

	static string? Str(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
	static int? Num(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int v) ? v : null;
	static bool? Bool(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out JsonElement e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) ? e.GetBoolean() : null;

	// card.tags is a scalar string array: projectTag GUIDs, hero-card accountSeqs, and/or "default".
	static string[] Tags(JsonElement o) =>
		o.ValueKind == JsonValueKind.Object && o.TryGetProperty("tags", out JsonElement e) && e.ValueKind == JsonValueKind.Array
			? [.. e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : x.ToString())]
			: [];
}
