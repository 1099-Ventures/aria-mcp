namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>
/// Parameters for <c>codecks_search_cards</c>. Scope is account-wide by default; supply
/// <see cref="ProjectId"/> (recommended) or <see cref="DeckId"/> to narrow. The remaining
/// fields are optional card filters.
/// </summary>
internal class CodecksSearchCardsRequest
{
	public string AccountId { get; set; } = string.Empty;
	public string? ProjectId { get; set; }
	public string? DeckId { get; set; }
	public int? AccountSeq { get; set; }
	public string? TitleContains { get; set; }
	public string? Status { get; set; }
	public string? AssigneeId { get; set; }
	public int Offset { get; set; }
	public int? Limit { get; set; }

	/// <summary>Doc-card filter: <c>all</c> (default) | <c>exclude</c> (work cards only) |
	/// <c>only</c> (docs only). A card is a doc when <c>isDoc</c> is true or <c>derivedStatus</c> is "doc".</summary>
	public string? DocFilter { get; set; }

	/// <summary>Comma-separated <c>derivedStatus</c> include-filter (assigned | unassigned | hero |
	/// archived | done | doc). Keeps only cards whose derivedStatus is in the set. This is the real
	/// triage signal — <c>status</c> alone only holds not_started/done/snoozing.</summary>
	public string? DerivedStatus { get; set; }

	/// <summary>Blocked-by-dependencies filter: <c>all</c> (default) | <c>only</c> (blocked) |
	/// <c>exclude</c> (unblocked). "Blocked" is the dependency state <c>hasBlockingDeps</c> — not a
	/// status or a tag.</summary>
	public string? Blocked { get; set; }

	/// <summary>Tag include-filter — keeps only cards whose <c>tags</c> array contains this value.
	/// A Codecks tag value is a <c>projectTag</c> GUID (from <c>tag_list</c>), a hero-card accountSeq
	/// (numeric string — hero cards act as tags/epics), or the literal <c>"default"</c> (deck default
	/// tag). Applied client-side.</summary>
	public string? Tag { get; set; }
}
