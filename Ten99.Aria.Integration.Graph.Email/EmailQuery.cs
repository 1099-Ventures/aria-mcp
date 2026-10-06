namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>
/// Provider-neutral query for fetching mail. The caller expresses intent; each
/// <see cref="IEmailService"/> implementation (Graph, MAPI, …) translates this to
/// its own native query. Deliberately carries NO provider syntax (no OData) — that
/// translation lives inside the provider (see GraphEmailService).
/// </summary>
public record EmailQuery
{
    /// <summary>Restrict to a single folder — a well-known name (inbox, junkemail, deleteditems,
    /// archive, sentitems, drafts) or a folder id. Null = whole mailbox (all folders).</summary>
    public string? Folder { get; init; }

    /// <summary>Filter by read state. Null = either.</summary>
    public bool? IsRead { get; init; }

    /// <summary>Filter by follow-up flag. Null = either.</summary>
    public bool? IsFlagged { get; init; }

    /// <summary>Match any of these conversation ids (the debt-collector watch-list key).</summary>
    public IReadOnlyList<string> ConversationIds { get; init; } = [];

    /// <summary>Received-time window.</summary>
    public DateRange? Received { get; init; }

    /// <summary>Filter by attachment presence. Null = either.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Require any of these Outlook categories (OR). Empty = no positive category filter.</summary>
    public IReadOnlyList<string> IncludeCategories { get; init; } = [];

    /// <summary>Exclude messages carrying any of these Outlook categories.</summary>
    public IReadOnlyList<string> ExcludeCategories { get; init; } = [];

    /// <summary>Include CC recipients in list results (off by default to keep lists lean).</summary>
    public bool IncludeCc { get; init; }

    /// <summary>Include the full message body in list results (off by default to keep lists lean) — cuts a
    /// per-message round-trip when a caller is working a set. The body is rendered per <see cref="BodyFormat"/>.</summary>
    public bool IncludeBody { get; init; }

    /// <summary>Body render format when <see cref="IncludeBody"/> is set. Text (default) strips HTML markup;
    /// Html returns the raw markup.</summary>
    public EmailBodyFormat BodyFormat { get; init; } = EmailBodyFormat.Text;

    /// <summary>Exclude the Deleted Items and Junk Email folders — unread trash/spam is noise for triage.
    /// The provider resolves the well-known folders and filters them out by <c>parentFolderId</c>.</summary>
    public bool ExcludeDeletedAndJunk { get; init; }

    /// <summary>Field predicates (Subject/From/…) combined with AND.</summary>
    public IReadOnlyList<EmailPredicate> Predicates { get; init; } = [];

    /// <summary>Max results to return (page size).</summary>
    public int Top { get; init; } = 50;

    /// <summary>Number of results to skip — offset paging (page N = Skip N*Top).</summary>
    public int Skip { get; init; } = 0;

    /// <summary>Result ordering.</summary>
    public EmailSort Sort { get; init; } = EmailSort.ReceivedDescending;

    /// <summary>Upper bound on rows the provider will scan when a predicate can only be applied client-side
    /// (To/Recipient/Body on Graph — Graph can't push a recipient/body <c>contains</c> into the query). The
    /// filter reaches at most this many of the newest messages; a match older than that is not found. Caller-
    /// tunable (operator default + per-call override) so it is never an arbitrary ceiling. Ignored by backends
    /// that filter server-side (IMAP SEARCH). 0 or negative means "use the provider default".</summary>
    public int MaxClientScan { get; init; } = 3000;
}

/// <summary>A single field predicate, e.g. From Domain "acme.com".</summary>
public record EmailPredicate(EmailField Field, EmailOperator Op, string Value);

/// <summary>Received-time window (inclusive bounds).</summary>
public record DateRange
{
    public DateTimeOffset? After { get; init; }
    public DateTimeOffset? Before { get; init; }
}

/// <summary>Queryable mail fields. Providers may map some to server-side vs client-side filtering.</summary>
public enum EmailField
{
    Subject,
    From,
    To,
    /// <summary>Any recipient — To OR Cc (matches Graph messageRulePredicates' recipientContains).</summary>
    Recipient,
    Body,
}

/// <summary>Predicate operators. Not every provider supports every op server-side; providers fall back to client-side filtering where needed.</summary>
public enum EmailOperator
{
    Equals,
    Contains,
    StartsWith,

    /// <summary>Address-domain match (From/To only), e.g. "acme.com" matches anyone@acme.com.</summary>
    Domain,
}

public enum EmailSort
{
    ReceivedDescending,
    ReceivedAscending,
}

/// <summary>Requested body format for full-message fetches. Text is the default — cheaper on context and
/// what agents reason over; Html only for callers that genuinely need markup.</summary>
public enum EmailBodyFormat
{
    Text,
    Html,
}
