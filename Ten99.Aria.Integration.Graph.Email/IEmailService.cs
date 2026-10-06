namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>
/// Stateless service for email operations via Microsoft Graph.
/// The calling agent provides the mailbox; the GraphServiceClient comes
/// pre-authenticated for the appropriate tenant via Integration.Graph.
/// </summary>
public interface IEmailService
{
	/// <summary>
	/// The primitive fetch: return messages matching a provider-neutral <see cref="EmailQuery"/>,
	/// regardless of read state. GetUnread/GetFlagged are thin wrappers over this.
	/// </summary>
	Task<IReadOnlyList<EmailMessage>> GetMessagesAsync(
		string mailbox,
		EmailQuery query,
		CancellationToken ct = default);

	/// <summary>Full-text search across the mailbox (or a folder) — the provider's relevance-ranked search
	/// (Graph <c>$search</c>) over from/subject/body/recipients. For "has this sender ever written about X"
	/// and body-scanning without paging the whole mailbox. Distinct from <see cref="GetMessagesAsync"/>: the
	/// backend's search cannot combine with the structured filters, so this is its own path. Backend-specific:
	/// NotSupported on IMAP for now.</summary>
	Task<IReadOnlyList<EmailMessage>> SearchAsync(
		string mailbox,
		string search,
		string? folder = null,
		int maxResults = 25,
		CancellationToken ct = default);

	/// <summary>Count messages matching <paramref name="query"/>, optionally grouped by <paramref name="groupBy"/>
	/// (senderDomain, senderAddress, year, month, folder, isRead) — census work without paging the corpus back
	/// to the caller. The provider has no server-side aggregation, so this pages the matching set internally,
	/// bounded by <paramref name="maxScan"/>, and returns only the counts. groupBy null returns just the total.
	/// Backend-specific: NotSupported on IMAP for now.</summary>
	Task<FacetResult> GetFacetsAsync(
		string mailbox,
		EmailQuery query,
		string? groupBy,
		int maxScan = 5000,
		CancellationToken ct = default);

	/// <summary>
	/// Return messages in any of the given conversations (the debt-collector watch-list key).
	/// Read-state agnostic; does not exclude ARIA-processed.
	/// </summary>
	Task<IReadOnlyList<EmailMessage>> GetByConversationAsync(
		string mailbox,
		IReadOnlyList<string> conversationIds,
		int maxResults = 50,
		CancellationToken ct = default);

	Task<IReadOnlyList<EmailMessage>> GetUnreadAsync(
		string mailbox,
		EmailFilter? filter = null,
		int maxResults = 50,
		int skip = 0,
		CancellationToken ct = default);

	Task<IReadOnlyList<EmailMessage>> GetFlaggedAsync(
		string mailbox,
		EmailFilter? filter = null,
		int maxResults = 50,
		int skip = 0,
		CancellationToken ct = default);

	/// <summary>
	/// Fetch a single message with its full body — the one place bodies are loaded, since the list
	/// tools (#496) deliberately carry only <see cref="EmailMessage.BodyPreview"/>. Defaults to plain
	/// text (cheaper on agent context); pass <see cref="EmailBodyFormat.Html"/> only when markup is needed.
	/// Returns null if no message with that id exists in the mailbox.
	/// </summary>
	Task<EmailMessage?> GetMessageAsync(
		string mailbox,
		string messageId,
		EmailBodyFormat bodyFormat = EmailBodyFormat.Text,
		string? propertySetGuid = null,
		string? propertyName = null,
		CancellationToken ct = default);

	// --- Batched writes: accept an id list, return a per-item result (partial failure, not all-or-nothing). ---

	/// <summary>Write a named single-value extended property (the caller's structured ledger record) onto
	/// each message. GUID/name/payload are the caller's — the service holds no vocabulary. Requires Mail.ReadWrite.</summary>
	Task<IReadOnlyList<BatchItemResult>> SetExtendedPropertyAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		string propertySetGuid,
		string name,
		string value,
		CancellationToken ct = default);

	/// <summary>Merge the given categories onto each message (never overwrites the human's own).</summary>
	Task<IReadOnlyList<BatchItemResult>> CategorizeAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		IReadOnlyList<string> categories,
		CancellationToken ct = default);

	/// <summary>Set read/unread on each message. Both directions — unread-marking is the undo. Policy about
	/// WHEN to touch read state belongs to the caller, not this tool.</summary>
	Task<IReadOnlyList<BatchItemResult>> SetReadStateAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		bool isRead,
		CancellationToken ct = default);

	/// <summary>Move each message to a folder. Returns the reassigned <see cref="BatchItemResult.NewId"/>
	/// (Graph changes it on move) and the <see cref="BatchItemResult.FromFolderId"/> — together the undo record.</summary>
	Task<IReadOnlyList<BatchItemResult>> MoveAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		string destinationFolderId,
		CancellationToken ct = default);

	/// <summary>Soft-delete each message (move to Deleted Items, 30-day recovery). There is deliberately no
	/// hard-delete path — its absence is a guarantee.</summary>
	Task<IReadOnlyList<BatchItemResult>> DeleteAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		CancellationToken ct = default);

	/// <summary>Create a DRAFT reply to <paramref name="messageId"/>, saved in Drafts.
	/// <paramref name="replyAll"/> addresses all original recipients (else the sender only);
	/// <paramref name="comment"/> is the reply text, prepended above the quoted original. Composing a draft is
	/// ungated and does not send; sending it is the separate <see cref="SendDraftAsync"/>, governed by the
	/// operator's send flag (off by default). Backend-specific: NotSupported on IMAP (no createReply action).
	/// Returns the created draft.</summary>
	Task<DraftResult> CreateReplyDraftAsync(
		string mailbox,
		string messageId,
		string? comment,
		bool replyAll = false,
		CancellationToken ct = default);

	/// <summary>Send an existing draft (from <see cref="CreateReplyDraftAsync"/> or the Drafts folder) by its
	/// id — Graph <c>POST /messages/{id}/send</c>. The composition step is separate and ungated; whether an
	/// agent may reach this at all is the operator's send flag, off by default (the tool layer enforces it).
	/// Backend-specific: NotSupported on IMAP for now. Returns success plus, where exposed, the Message-ID.</summary>
	Task<SendResult> SendDraftAsync(
		string mailbox,
		string draftId,
		CancellationToken ct = default);

	/// <summary>Send a newly composed message from <paramref name="mailbox"/>. Mechanism only — the caller
	/// (agent) owns all policy about whether and what to send; the service just delivers. Requires send
	/// permission (delegated <c>Mail.Send</c> / application send role for Graph; SMTP submit for IMAP).
	/// Returns success plus, where the backend exposes it, the sent Message-ID.</summary>
	Task<SendResult> SendAsync(
		string mailbox,
		EmailDraft draft,
		CancellationToken ct = default);

	/// <summary>List a message's attachments — metadata only (name, content type, size, inline flag, and the
	/// backend id needed to fetch the bytes). No content is loaded. Returns an empty list when the message
	/// has none.</summary>
	Task<IReadOnlyList<EmailAttachmentInfo>> GetAttachmentsAsync(
		string mailbox,
		string messageId,
		CancellationToken ct = default);

	/// <summary>Fetch one attachment's bytes + metadata by its id (from <see cref="GetAttachmentsAsync"/>).
	/// Returns null when the id doesn't resolve to a byte-bearing (file) attachment on the message — e.g. a
	/// Graph item/reference attachment, which has metadata but no inline content here.</summary>
	Task<EmailAttachmentContent?> GetAttachmentAsync(
		string mailbox,
		string messageId,
		string attachmentId,
		CancellationToken ct = default);

	Task<MailboxInfo> GetMailboxInfoAsync(
		string mailbox,
		CancellationToken ct = default);

	/// <summary>Enumerate the mailbox's folders (flat; nesting via <see cref="MailFolderInfo.ParentFolderId"/>),
	/// each with unread/total counts — so an agent can see where rules auto-file mail and scope reads.</summary>
	Task<IReadOnlyList<MailFolderInfo>> GetFoldersAsync(
		string mailbox,
		string? parentFolderId = null,
		bool recursive = false,
		CancellationToken ct = default);

	/// <summary>Create a mail folder — top-level, or a child of <paramref name="parentFolderId"/> when given.
	/// Create-or-get: if a folder with that name already exists under the parent, returns the existing one
	/// rather than erroring, so re-runs are safe. Returns the created/found folder.</summary>
	Task<MailFolderInfo> CreateFolderAsync(
		string mailbox,
		string name,
		string? parentFolderId = null,
		CancellationToken ct = default);

	/// <summary>Rename the folder identified by <paramref name="folderId"/> to <paramref name="newName"/>
	/// (in place — same parent). Returns the updated folder.</summary>
	Task<MailFolderInfo> RenameFolderAsync(
		string mailbox,
		string folderId,
		string newName,
		CancellationToken ct = default);

	/// <summary>Reparent the folder identified by <paramref name="folderId"/> under
	/// <paramref name="newParentFolderId"/> — the correction a misclassification actually needs (name was
	/// right, parent was wrong). Non-destructive: the mail inside travels with the folder. Rejects moving a
	/// folder into itself or one of its own descendants, and refuses to merge onto an existing same-name
	/// sibling under the target (a silent merge is worse than a duplicate — create-or-get does NOT apply
	/// here). Returns the moved folder.</summary>
	Task<MailFolderInfo> MoveFolderAsync(
		string mailbox,
		string folderId,
		string newParentFolderId,
		CancellationToken ct = default);

	/// <summary>Soft-delete the folder identified by <paramref name="folderId"/> by moving it under Deleted
	/// Items / Trash — recoverable, and consistent with <see cref="DeleteAsync"/>. There is deliberately no
	/// hard-delete path. Refuses well-known folders (Inbox, Sent, Deleted Items, Drafts, Junk, Archive) and,
	/// unless <paramref name="force"/> is set, a folder that still holds messages or child folders. Returns
	/// the removed folder's id, its previous parent, and name — the undo record.</summary>
	Task<FolderDeletionResult> DeleteFolderAsync(
		string mailbox,
		string folderId,
		bool force = false,
		CancellationToken ct = default);

	/// <summary>List the mailbox's server-side inbox rules (Exchange/Graph messageRules). Read-only
	/// inspection. Rules fire on delivery only — the steady-state disposition, not a backlog mechanism.
	/// Backend-specific: server-side rules have no IMAP equivalent (NotSupported there).</summary>
	Task<IReadOnlyList<InboxRuleInfo>> GetInboxRulesAsync(
		string mailbox,
		CancellationToken ct = default);

	/// <summary>Create a server-side inbox rule. Mechanism only — the caller (agent) decides what the rule
	/// should do; the service just writes it. Needs mailbox-settings write permission (delegated
	/// <c>MailboxSettings.ReadWrite</c>). Backend-specific: NotSupported on IMAP. Returns the created rule.</summary>
	Task<InboxRuleInfo> CreateInboxRuleAsync(
		string mailbox,
		InboxRuleDraft rule,
		CancellationToken ct = default);

	/// <summary>Update a server-side inbox rule — enable/disable (<paramref name="isEnabled"/>), rename
	/// (<paramref name="displayName"/>), reorder (<paramref name="sequence"/>), or amend its
	/// <paramref name="conditions"/>/<paramref name="exceptions"/>/<paramref name="actions"/>. Each supplied
	/// field changes; nulls are left untouched. Conditions/exceptions/actions REPLACE the rule's set wholesale
	/// (read the rule, extend the array, send it back). Backend-specific: NotSupported on IMAP. Returns the
	/// updated rule.</summary>
	Task<InboxRuleInfo> UpdateInboxRuleAsync(
		string mailbox,
		string ruleId,
		bool? isEnabled = null,
		string? displayName = null,
		int? sequence = null,
		InboxRuleConditions? conditions = null,
		InboxRuleConditions? exceptions = null,
		InboxRuleActions? actions = null,
		CancellationToken ct = default);

	/// <summary>Delete a server-side inbox rule by id, returning the definition it removed. There is no soft
	/// path — a rule is configuration, not content, and Graph has no rule recycle bin — so the deleted
	/// definition IS the undo record (recreate it with <see cref="CreateInboxRuleAsync"/>). Null if the rule
	/// could not be read back before deletion. Backend-specific: NotSupported on IMAP.</summary>
	Task<InboxRuleInfo?> DeleteInboxRuleAsync(
		string mailbox,
		string ruleId,
		CancellationToken ct = default);

	/// <summary>Folder-scoped delta sync: changes (added/updated/removed) since <paramref name="deltaToken"/>,
	/// plus the next cursor. First call (null token) baselines the folder. The service does not persist the
	/// token — the caller owns it, keyed by mailbox+folder. A stale token (Graph 410) transparently resyncs
	/// from a fresh baseline (<see cref="DeltaResult.Resynced"/>).</summary>
	Task<DeltaResult> GetDeltaAsync(
		string mailbox,
		string folder,
		string? deltaToken,
		int maxPageSize = 50,
		CancellationToken ct = default);
}
