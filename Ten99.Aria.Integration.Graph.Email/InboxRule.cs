namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>
/// A server-side inbox rule (Exchange/Graph messageRule). Rules fire <b>on delivery only</b> — they are
/// the steady-state disposition of triage, not a mechanism for existing backlog (that is the sweep:
/// list + batch writes). The MCP is the mechanism: the calling agent decides what rule to create; this
/// contract carries no classification vocabulary. Server-side rules have no IMAP equivalent, so the IMAP
/// backend reports NotSupported.
/// </summary>
public record InboxRuleInfo
{
	public required string Id { get; init; }
	public required string DisplayName { get; init; }
	/// <summary>Evaluation order (lower runs first). Graph assigns one when omitted on create.</summary>
	public int Sequence { get; init; }
	public bool IsEnabled { get; init; }
	public InboxRuleConditions? Conditions { get; init; }
	public InboxRuleActions? Actions { get; init; }
	public InboxRuleConditions? Exceptions { get; init; }
}

/// <summary>The rule to create. <see cref="Conditions"/> AND-combine; a rule with no conditions matches
/// every delivered message (rely on <see cref="Actions"/> to be deliberate).</summary>
public record InboxRuleDraft
{
	public required string DisplayName { get; init; }
	public int? Sequence { get; init; }
	public bool IsEnabled { get; init; } = true;
	public InboxRuleConditions? Conditions { get; init; }
	public required InboxRuleActions Actions { get; init; }
	public InboxRuleConditions? Exceptions { get; init; }
}

/// <summary>Full parity with Graph's messageRulePredicates. Multiple set fields AND-combine; values within a
/// list OR-combine (Graph semantics). Unset fields are ignored. Modelled completely so a rule READ back is
/// never under-reported — a rule that scopes on, say, <see cref="SentOnlyToMe"/> must not vanish on read.</summary>
public record InboxRuleConditions
{
	// -- text/address contains --
	/// <summary>Match when the subject contains any of these strings.</summary>
	public IReadOnlyList<string>? SubjectContains { get; init; }
	/// <summary>Match when the body contains any of these strings.</summary>
	public IReadOnlyList<string>? BodyContains { get; init; }
	/// <summary>Match when the subject OR body contains any of these strings.</summary>
	public IReadOnlyList<string>? BodyOrSubjectContains { get; init; }
	/// <summary>Match when the sender's name or address contains any of these strings.</summary>
	public IReadOnlyList<string>? SenderContains { get; init; }
	/// <summary>Match when a recipient's name or address contains any of these strings.</summary>
	public IReadOnlyList<string>? RecipientContains { get; init; }
	/// <summary>Match when an internet message header contains any of these strings.</summary>
	public IReadOnlyList<string>? HeaderContains { get; init; }
	/// <summary>Match when the message is from any of these exact addresses.</summary>
	public IReadOnlyList<string>? FromAddresses { get; init; }
	/// <summary>Match when the message was sent to any of these exact addresses.</summary>
	public IReadOnlyList<string>? SentToAddresses { get; init; }

	// -- categorisation / importance / size --
	/// <summary>Match when the message carries any of these categories.</summary>
	public IReadOnlyList<string>? Categories { get; init; }
	/// <summary>Match on importance: "low" | "normal" | "high".</summary>
	public string? Importance { get; init; }
	/// <summary>Match on the message action flag (e.g. "followUp", "review", "reply").</summary>
	public string? MessageActionFlag { get; init; }
	/// <summary>Match when the message size falls within this range (kilobytes).</summary>
	public InboxRuleSizeRange? WithinSizeRange { get; init; }

	// -- attributes / recipient scope (booleans) --
	public bool? HasAttachments { get; init; }
	public bool? SentToMe { get; init; }
	public bool? SentOnlyToMe { get; init; }
	public bool? SentCcMe { get; init; }
	public bool? SentToOrCcMe { get; init; }
	public bool? NotSentToMe { get; init; }

	// -- message-type flags --
	public bool? IsApprovalRequest { get; init; }
	public bool? IsAutomaticForward { get; init; }
	public bool? IsAutomaticReply { get; init; }
	public bool? IsEncrypted { get; init; }
	public bool? IsMeetingRequest { get; init; }
	public bool? IsMeetingResponse { get; init; }
	public bool? IsNonDeliveryReport { get; init; }
	public bool? IsPermissionControlled { get; init; }
	public bool? IsReadReceipt { get; init; }
	public bool? IsSigned { get; init; }
	public bool? IsVoicemail { get; init; }
}

/// <summary>A message-size window in kilobytes (Graph's sizeRange). Either bound may be null (open-ended).</summary>
public record InboxRuleSizeRange
{
	public int? MinimumSizeKb { get; init; }
	public int? MaximumSizeKb { get; init; }
}

/// <summary>Full parity with Graph's messageRuleActions. Folder ids come from the folder tools;
/// categories/addresses are the caller's. At least one action should be set.</summary>
public record InboxRuleActions
{
	/// <summary>Move matching mail to this folder id.</summary>
	public string? MoveToFolder { get; init; }
	/// <summary>Copy matching mail to this folder id.</summary>
	public string? CopyToFolder { get; init; }
	/// <summary>Forward matching mail to these addresses.</summary>
	public IReadOnlyList<string>? ForwardTo { get; init; }
	/// <summary>Forward matching mail to these addresses as an attachment.</summary>
	public IReadOnlyList<string>? ForwardAsAttachmentTo { get; init; }
	/// <summary>Redirect matching mail to these addresses (no "on behalf of" wrapping).</summary>
	public IReadOnlyList<string>? RedirectTo { get; init; }
	/// <summary>Mark matching mail read.</summary>
	public bool? MarkAsRead { get; init; }
	/// <summary>Set importance on matching mail: "low" | "normal" | "high".</summary>
	public string? MarkImportance { get; init; }
	/// <summary>Soft-delete matching mail (Exchange moves it to Deleted Items — recoverable).</summary>
	public bool? Delete { get; init; }
	/// <summary>Permanently delete matching mail (skips Deleted Items — NOT recoverable). Distinct from Delete.</summary>
	public bool? PermanentDelete { get; init; }
	/// <summary>Assign these categories to matching mail.</summary>
	public IReadOnlyList<string>? AssignCategories { get; init; }
	/// <summary>Stop evaluating later rules once this one matches.</summary>
	public bool? StopProcessingRules { get; init; }
}
