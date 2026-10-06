namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>The outcome of a facet/count query: how many matched, how many were scanned, whether the scan
/// hit its bound, and (when grouped) the per-key counts. Census work — "how many, by whom, in which
/// period" — without paging the whole corpus back to the caller.</summary>
public record FacetResult
{
	/// <summary>Messages that matched the filter within the scan window.</summary>
	public required int Matched { get; init; }
	/// <summary>Messages examined (the filter-matched set may be smaller when client-side predicates apply).</summary>
	public required int Scanned { get; init; }
	/// <summary>True when the scan stopped at <c>maxScan</c> before exhausting the set — counts are a floor, not final.</summary>
	public bool Truncated { get; init; }
	/// <summary>Per-key counts (descending), or null when no <c>groupBy</c> was requested.</summary>
	public IReadOnlyList<FacetBucket>? Facets { get; init; }
}

public record FacetBucket
{
	public required string Key { get; init; }
	public required int Count { get; init; }
}
