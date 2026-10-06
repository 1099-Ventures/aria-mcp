using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Ten99.Aria.Common.Secrets;
using Ten99.Aria.Integration.Graph.Email;
using Ten99.Aria.Mcp.Email.Imap.Configuration;
using Ten99.Aria.Mcp.Email.Imap.Operations;

//	Debug Cmd: --mcp "…/Ten99.Aria.Mcp.Email.Imap/bin/Debug/net10.0/Ten99.Aria.Mcp.Email.Imap.dll"
namespace Ten99.Aria.Mcp.Email.Imap;

/// <summary>
/// The IMAP email MCP host — a near-verbatim structural copy of <c>O365EmailTool</c> with the O365/Graph
/// backend swapped for <see cref="ImapEmailService"/> (MailKit). Same grouped tool NAMES, same lean
/// projection + <c>verbose</c>, same batched <c>messageIds</c> with per-item results, same <c>mailbox</c>
/// param and secret-scrubbed <c>bodyPreview</c>.
///
/// The P1/P2 tools O365 exposes but IMAP cannot back yet — <c>email_edit_categorize</c> (no IMAP
/// categories), <c>email_edit_property</c> (no extended-property ledger), <c>email_read_delta</c> (no
/// delta token) — are deliberately OMITTED here rather than stubbed. See the IMAP-Email-MCP-Scoping doc.
/// </summary>
[McpServerToolType]
public static class ImapEmailTool
{
	private static readonly SemaphoreSlim _rateLimiter = new(10, 10);
	private static DateTime _lastReset = DateTime.UtcNow;

	[McpServerTool(Name = "email_health")]
	[Description("Test the IMAP connection + mailbox access via the shared Integration.Graph.Email contract "
		+ "(MailKit backend). Also reports the NEGOTIATED IMAP capabilities (MOVE, SPECIAL-USE, SORT, "
		+ "CONDSTORE/QRESYNC, X-GM-EXT-1) and which substitutes are live — so the agent knows what this "
		+ "server can actually do.")]
	public static async Task<CallToolResult> Health(
		IConfiguration configuration,
		HttpClient httpClient,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		return await new ImapEmailHealthOperation(_rateLimiter, ref _lastReset)
			.Execute(configuration, httpClient, typedSecrets, genericSecrets, cancellationToken);
	}

	[McpServerTool(Name = "email_read_unread")]
	[Description("Get unread emails, newest first, across ALL folders EXCEPT Deleted Items and Junk Email, "
		+ "which are excluded by default as noise. Targets `mailbox` or the configured default when omitted "
		+ "(IMAP is single-account per credential — `mailbox` selects the configured account, it does not "
		+ "re-target arbitrary mailboxes like Graph). Optional filters: `fromContains`, `subjectContains`, "
		+ "and a received-date window `since`/`before` (ISO-8601, e.g. 2026-08-01 or 2026-08-01T00:00:00Z). "
		+ "Set includeSpamAndTrash to also include Junk/Deleted. Returns a lean list — metadata + a short "
		+ "bodyPreview, no full body (use email_read_body); bearer-shaped secrets in bodyPreview are masked. "
		+ "Set `includeBody:true` to widen each row with the full body (`bodyFormat` text|default or html) — use "
		+ "when you'd otherwise call email_read_body per message; lean by default (this costs one extra fetch per "
		+ "row on IMAP). `bodyMaxChars` caps an included body's length (0 = full). Page with maxResults + skip: "
		+ "when hasMore is true, call again with skip = nextSkip.")]
	public static async Task<CallToolResult> GetUnread(
		IConfiguration configuration,
		HttpClient httpClient,
		string? mailbox = null,
		int maxResults = 20,
		int skip = 0,
		string? fromContains = null,
		string? subjectContains = null,
		string? since = null,
		string? before = null,
		bool includeSpamAndTrash = false,
		bool includeCc = false,
		bool includeBody = false,
		string bodyFormat = "text",
		int bodyMaxChars = 0,
		bool verbose = false,
		bool includePreview = true,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (ParseCutoff(since, nameof(since), out var receivedAfter) is { } e1) return InputError(e1);
			if (ParseCutoff(before, nameof(before), out var receivedBefore) is { } e2) return InputError(e2);

			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var filter = new EmailFilter
			{
				FromContains = fromContains,
				SubjectContains = subjectContains,
				ReceivedAfter = receivedAfter,
				ReceivedBefore = receivedBefore,
				ExcludeDeletedAndJunk = !includeSpamAndTrash,
				IncludeCc = includeCc,
				IncludeBody = includeBody,
				BodyFormat = ParseBodyFormat(bodyFormat),
			};

			var filtersApplied = new Dictionary<string, object?>();
			if (!string.IsNullOrWhiteSpace(fromContains)) filtersApplied["fromContains"] = fromContains;
			if (!string.IsNullOrWhiteSpace(subjectContains)) filtersApplied["subjectContains"] = subjectContains;
			if (!string.IsNullOrWhiteSpace(since)) filtersApplied["since"] = since;
			if (!string.IsNullOrWhiteSpace(before)) filtersApplied["before"] = before;
			if (includeCc) filtersApplied["includeCc"] = true;
			if (includeBody) { filtersApplied["includeBody"] = true; filtersApplied["bodyFormat"] = bodyFormat; }
			if (bodyMaxChars > 0) filtersApplied["bodyMaxChars"] = bodyMaxChars;

			EchoScope(filtersApplied, includeSpamAndTrash, !includeSpamAndTrash);

			var messages = await emailService.GetUnreadAsync(resolvedMailbox, filter, maxResults, skip, cancellationToken);
			return LeanList(messages, maxResults, skip, RedactPreviews(configuration), resolvedMailbox, verbose, filtersApplied, bodyMaxChars, includePreview);
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				IsError = true,
				Content = [new TextContentBlock { Text = $"Error getting unread emails: {ex.Message}" }]
			};
		}
	}

	[McpServerTool(Name = "email_read_flagged")]
	[Description("Get flagged emails, newest first, from `mailbox` or the configured default when omitted. "
		+ "Optional received-date window `since`/`before` (ISO-8601). Returns a lean list (bodyPreview only, "
		+ "no full body — use email_read_body). Set `includeBody:true` to widen each row with the full body "
		+ "(`bodyFormat` text|default or html) — use when you'd otherwise call email_read_body per message; lean "
		+ "by default (this costs one extra fetch per row on IMAP). `bodyMaxChars` caps an included body's length "
		+ "(0 = full). Page with maxResults + skip: when hasMore is "
		+ "true, call again with skip = nextSkip. (Flagged spans all folders and is sorted newest-first in-process.)")]
	public static async Task<CallToolResult> GetFlagged(
		IConfiguration configuration,
		HttpClient httpClient,
		string? mailbox = null,
		int maxResults = 20,
		int skip = 0,
		string? since = null,
		string? before = null,
		bool includeSpamAndTrash = false,
		bool includeCc = false,
		bool includeBody = false,
		string bodyFormat = "text",
		int bodyMaxChars = 0,
		bool verbose = false,
		bool includePreview = true,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (ParseCutoff(since, nameof(since), out var receivedAfter) is { } e1) return InputError(e1);
			if (ParseCutoff(before, nameof(before), out var receivedBefore) is { } e2) return InputError(e2);

			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var filter = new EmailFilter
			{
				ReceivedAfter = receivedAfter,
				ReceivedBefore = receivedBefore,
				ExcludeDeletedAndJunk = !includeSpamAndTrash,
				IncludeCc = includeCc,
				IncludeBody = includeBody,
				BodyFormat = ParseBodyFormat(bodyFormat),
			};

			var filtersApplied = new Dictionary<string, object?>();
			if (!string.IsNullOrWhiteSpace(since)) filtersApplied["since"] = since;
			if (!string.IsNullOrWhiteSpace(before)) filtersApplied["before"] = before;
			if (includeCc) filtersApplied["includeCc"] = true;
			if (includeBody) { filtersApplied["includeBody"] = true; filtersApplied["bodyFormat"] = bodyFormat; }
			if (bodyMaxChars > 0) filtersApplied["bodyMaxChars"] = bodyMaxChars;

			EchoScope(filtersApplied, includeSpamAndTrash, !includeSpamAndTrash);

			var messages = await emailService.GetFlaggedAsync(resolvedMailbox, filter, maxResults, skip, cancellationToken);
			return LeanList(messages, maxResults, skip, RedactPreviews(configuration), resolvedMailbox, verbose, filtersApplied, bodyMaxChars, includePreview);
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				IsError = true,
				Content = [new TextContentBlock { Text = $"Error getting flagged emails: {ex.Message}" }]
			};
		}
	}

	[McpServerTool(Name = "email_read_folders")]
	[Description("List a mailbox's folders with unread/total counts — so you can see where rules auto-file "
		+ "mail and pick a folder to scope email_read_list to. By default returns TOP-LEVEL folders only; "
		+ "pass `parentFolderId` (a folder path/id) to list one folder's direct children, or `recursive:true` "
		+ "for the whole tree. Nesting is via each folder's `ParentFolderId` (the server path delimiter). "
		+ "childFolderCount is a 0/1 has-children flag on IMAP (not an exact count). Targets `mailbox` or the "
		+ "configured default.")]
	public static async Task<CallToolResult> ListFolders(
		IConfiguration configuration,
		HttpClient httpClient,
		string? mailbox = null,
		string? parentFolderId = null,
		bool recursive = false,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var folders = await emailService.GetFoldersAsync(resolvedMailbox, parentFolderId, recursive, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, count = folders.Count, folders };
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }]
			};
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				IsError = true,
				Content = [new TextContentBlock { Text = $"Error listing folders: {ex.Message}" }]
			};
		}
	}

	[McpServerTool(Name = "email_read_list")]
	[Description("General message list — the flexible read the sugar tools wrap. Scope to a `folder` "
		+ "(well-known name: inbox, junkemail, deleteditems, archive, sentitems, drafts; or a folder path) or "
		+ "omit for the whole mailbox. Filter by isRead, isFlagged, fromContains, subjectContains, toContains "
		+ "(To only), recipientContains (To or Cc), and a "
		+ "since/before date window (ISO-8601). Set oldestFirst for backlog order (oldest→newest), includeCc "
		+ "to add CC recipients. Set `includeBody:true` to widen each row with the full body (`bodyFormat` "
		+ "text|default or html) — use when you'd otherwise call email_read_body per message; lean by default "
		+ "(this costs one extra fetch per row on IMAP). `bodyMaxChars` caps an included body's length (0 = full). "
		+ "With no folder, Deleted Items + Junk are excluded unless "
		+ "includeSpamAndTrash. "
		+ "Same lean list + paging as email_read_unread. Unread-Junk audit = folder:\"junkemail\", isRead:false. "
		+ "(IMAP note: category filters, conversation threading and delta are P1/P2 substitutes and not exposed "
		+ "on this backend.)")]
	public static async Task<CallToolResult> ListMessages(
		IConfiguration configuration,
		HttpClient httpClient,
		string? mailbox = null,
		string? folder = null,
		bool? isRead = null,
		bool? isFlagged = null,
		string? fromContains = null,
		string? subjectContains = null,
		string? toContains = null,
		string? recipientContains = null,
		string? since = null,
		string? before = null,
		bool includeSpamAndTrash = false,
		bool includeCc = false,
		bool includeBody = false,
		string bodyFormat = "text",
		int bodyMaxChars = 0,
		bool oldestFirst = false,
		bool verbose = false,
		bool includePreview = true,
		int maxResults = 20,
		int skip = 0,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (ParseCutoff(since, nameof(since), out var receivedAfter) is { } e1) return InputError(e1);
			if (ParseCutoff(before, nameof(before), out var receivedBefore) is { } e2) return InputError(e2);

			var predicates = new List<EmailPredicate>();
			if (!string.IsNullOrWhiteSpace(fromContains))
				predicates.Add(new EmailPredicate(EmailField.From, EmailOperator.Contains, fromContains));
			if (!string.IsNullOrWhiteSpace(subjectContains))
				predicates.Add(new EmailPredicate(EmailField.Subject, EmailOperator.Contains, subjectContains));
			if (!string.IsNullOrWhiteSpace(toContains))
				predicates.Add(new EmailPredicate(EmailField.To, EmailOperator.Contains, toContains));
			if (!string.IsNullOrWhiteSpace(recipientContains))
				predicates.Add(new EmailPredicate(EmailField.Recipient, EmailOperator.Contains, recipientContains));

			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var query = new EmailQuery
			{
				Folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim(),
				IsRead = isRead,
				IsFlagged = isFlagged,
				Received = receivedAfter is not null || receivedBefore is not null
					? new DateRange { After = receivedAfter, Before = receivedBefore }
					: null,
				Predicates = predicates,
				ExcludeDeletedAndJunk = string.IsNullOrWhiteSpace(folder) && !includeSpamAndTrash,
				IncludeCc = includeCc,
				IncludeBody = includeBody,
				BodyFormat = ParseBodyFormat(bodyFormat),
				Sort = oldestFirst ? EmailSort.ReceivedAscending : EmailSort.ReceivedDescending,
				Top = maxResults,
				Skip = skip,
			};

			var filtersApplied = new Dictionary<string, object?>();
			if (!string.IsNullOrWhiteSpace(folder)) filtersApplied["folder"] = folder.Trim();
			if (isRead.HasValue) filtersApplied["isRead"] = isRead.Value;
			if (isFlagged.HasValue) filtersApplied["isFlagged"] = isFlagged.Value;
			if (!string.IsNullOrWhiteSpace(fromContains)) filtersApplied["fromContains"] = fromContains;
			if (!string.IsNullOrWhiteSpace(subjectContains)) filtersApplied["subjectContains"] = subjectContains;
			if (!string.IsNullOrWhiteSpace(toContains)) filtersApplied["toContains"] = toContains;
			if (!string.IsNullOrWhiteSpace(recipientContains)) filtersApplied["recipientContains"] = recipientContains;
			if (!string.IsNullOrWhiteSpace(since)) filtersApplied["since"] = since;
			if (!string.IsNullOrWhiteSpace(before)) filtersApplied["before"] = before;
			if (oldestFirst) filtersApplied["oldestFirst"] = true;
			if (includeCc) filtersApplied["includeCc"] = true;
			if (includeBody) { filtersApplied["includeBody"] = true; filtersApplied["bodyFormat"] = bodyFormat; }
			if (bodyMaxChars > 0) filtersApplied["bodyMaxChars"] = bodyMaxChars;

			EchoScope(filtersApplied, includeSpamAndTrash, string.IsNullOrWhiteSpace(folder) && !includeSpamAndTrash);

			var messages = await emailService.GetMessagesAsync(resolvedMailbox, query, cancellationToken);
			return LeanList(messages, maxResults, skip, RedactPreviews(configuration), resolvedMailbox, verbose, filtersApplied, bodyMaxChars, includePreview);
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				IsError = true,
				Content = [new TextContentBlock { Text = $"Error listing messages: {ex.Message}" }]
			};
		}
	}

	[McpServerTool(Name = "email_read_body")]
	[Description("Get one email in full — metadata (incl. ccRecipients), the complete message body — by its "
		+ "messageId (from a list tool). This is the only tool that returns the body; the list tools carry "
		+ "just a bodyPreview. Targets `mailbox` or the configured default — pass the same mailbox the message "
		+ "id came from. bodyFormat is 'text' (default, HTML stripped — best for reading/triage) or 'html'. "
		+ "(The extended-property ledger read Graph offers has no IMAP equivalent — a P2 substitute.)")]
	public static async Task<CallToolResult> GetEmail(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageId,
		string? mailbox = null,
		string bodyFormat = "text",
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var format = string.Equals(bodyFormat, "html", StringComparison.OrdinalIgnoreCase)
				? EmailBodyFormat.Html
				: EmailBodyFormat.Text;

			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var message = await emailService.GetMessageAsync(resolvedMailbox, messageId, format, null, null, cancellationToken);

			if (message is null)
			{
				return new CallToolResult
				{
					IsError = true,
					Content = [new TextContentBlock { Text = $"No message found with id '{messageId}' in mailbox {resolvedMailbox}." }]
				};
			}

			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(message, JsonOptions) }]
			};
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				IsError = true,
				Content = [new TextContentBlock { Text = $"Error getting email: {ex.Message}" }]
			};
		}
	}

	[McpServerTool(Name = "email_read_attachments")]
	[Description("List a message's attachments — metadata only (id, name, contentType, size, isInline), no "
		+ "content. Use the returned `id` (the MIME part specifier) with email_read_attachment to fetch the "
		+ "bytes. `messageId` is an id from a list/body call; targets `mailbox` or the configured default.")]
	public static async Task<CallToolResult> ListAttachments(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageId,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var attachments = await emailService.GetAttachmentsAsync(resolvedMailbox, messageId, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, messageId, count = attachments.Count, attachments };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error listing attachments: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_read_attachment")]
	[Description("Fetch ONE attachment's content by `messageId` + `attachmentId` (from email_read_attachments). "
		+ "By default returns the bytes as base64 (`contentBase64`), up to the configured inline cap "
		+ "(Imap:Auth:MaxInlineAttachmentBytes, default 30 MB) — for anything larger, pass `saveToDirectory` and "
		+ "the file is written to disk and its `savedPath` returned instead (avoids flooding the context). "
		+ "`saveToDirectory` always writes to disk regardless of size. Targets `mailbox` or the configured "
		+ "default. Returns null-not-found if the id isn't a fetchable part.")]
	public static async Task<CallToolResult> GetAttachment(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageId,
		string attachmentId,
		string? mailbox = null,
		string? saveToDirectory = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var att = await emailService.GetAttachmentAsync(resolvedMailbox, messageId, attachmentId, cancellationToken);
			if (att is null)
				return InputError($"Attachment '{attachmentId}' not found on the message (no MIME part with that specifier).");
			long maxInline = configuration.GetValue("Imap:Auth:MaxInlineAttachmentBytes", 30L * 1024 * 1024);
			return AttachmentResult(resolvedMailbox, messageId, att, saveToDirectory, maxInline);
		}
		catch (Exception ex)
		{
			return InputError($"Error getting attachment: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_create_folder")]
	[Description("Create a mail folder — top-level, or under `parentFolderId` (a folder path from "
		+ "email_read_folders) when given. Create-or-get: an existing same-named folder under the parent is "
		+ "returned rather than erroring, so it's safe to re-run. Returns the folder {id, displayName, "
		+ "parentFolderId}; pair with email_edit_move to file mail into it. Targets `mailbox` or the "
		+ "configured default.")]
	public static async Task<CallToolResult> CreateFolder(
		IConfiguration configuration,
		HttpClient httpClient,
		string name,
		string? parentFolderId = null,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(name))
				return InputError("`name` is required.");
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var folder = await emailService.CreateFolderAsync(resolvedMailbox, name.Trim(), parentFolderId, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, folder };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error creating folder: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_rename_folder")]
	[Description("Rename a mail folder (identified by `folderId`/path from email_read_folders) to `newName`, "
		+ "in place under the same parent. Returns the updated folder {id, displayName, parentFolderId}. "
		+ "Targets `mailbox` or the configured default.")]
	public static async Task<CallToolResult> RenameFolder(
		IConfiguration configuration,
		HttpClient httpClient,
		string folderId,
		string newName,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folderId)) return InputError("`folderId` is required.");
			if (string.IsNullOrWhiteSpace(newName)) return InputError("`newName` is required.");
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var folder = await emailService.RenameFolderAsync(resolvedMailbox, folderId, newName.Trim(), cancellationToken);
			var payload = new { mailbox = resolvedMailbox, folder };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error renaming folder: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_move_folder")]
	[Description("Reparent a mail folder (identified by `folderId`/path from email_read_folders) under "
		+ "`newParentFolderId` — the correction for a folder filed in the wrong place. Non-destructive: the "
		+ "mail inside travels with it. Refuses to move a folder into itself/a descendant, or to merge onto an "
		+ "existing same-name folder under the target (rename one first). Returns the moved folder "
		+ "{id, displayName, parentFolderId}. Targets `mailbox` or the configured default.")]
	public static async Task<CallToolResult> MoveFolder(
		IConfiguration configuration,
		HttpClient httpClient,
		string folderId,
		string newParentFolderId,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folderId)) return InputError("`folderId` is required.");
			if (string.IsNullOrWhiteSpace(newParentFolderId)) return InputError("`newParentFolderId` is required.");
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var folder = await emailService.MoveFolderAsync(resolvedMailbox, folderId, newParentFolderId, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, folder };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error moving folder: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_delete_folder")]
	[Description("Soft-delete a mail folder (identified by `folderId`/path from email_read_folders) by moving "
		+ "it to Trash / Deleted Items — recoverable, never a hard delete (errors if the server advertises no "
		+ "Trash rather than deleting destructively). Refuses well-known folders (Inbox, Sent, Trash, Drafts, "
		+ "Junk, Archive) and, unless `force` is true, a folder that still holds messages or subfolders. "
		+ "Returns {deletedFolderId, previousParentId, name} — the undo record. Targets `mailbox` or the "
		+ "configured default.")]
	public static async Task<CallToolResult> DeleteFolder(
		IConfiguration configuration,
		HttpClient httpClient,
		string folderId,
		bool force = false,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folderId)) return InputError("`folderId` is required.");
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var deleted = await emailService.DeleteFolderAsync(resolvedMailbox, folderId, force, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, deleted };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error deleting folder: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_move")]
	[Description("Move one or more messages to a folder — the FILE disposition. `messageIds` is "
		+ "comma-separated; `destinationFolderId` is a folder path (from email_read_folders) or a well-known "
		+ "name (inbox, archive, …). Batched: per-item result carries the message's NEW id (IMAP reassigns the "
		+ "UID on move — via the MOVE extension or COPY+expunge) and its previous fromFolderId — together the "
		+ "undo record. Targets `mailbox` or the configured default.")]
	public static async Task<CallToolResult> Move(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageIds,
		string destinationFolderId,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var results = await emailService.MoveAsync(resolvedMailbox, SplitCsv(messageIds), destinationFolderId, cancellationToken);
			return BatchResult(resolvedMailbox, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error moving messages: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_read")]
	[Description("Set read/unread state on one or more messages. `messageIds` is comma-separated; `isRead` "
		+ "true marks read, false marks unread. Both directions matter — unread-marking is the undo for a "
		+ "wrong call. Read state is the human's signal, so WHEN to touch it is the caller's policy, not this "
		+ "tool's. Batched: per-item {success, error}. Targets `mailbox` or the configured default.")]
	public static async Task<CallToolResult> SetReadState(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageIds,
		bool isRead,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var results = await emailService.SetReadStateAsync(resolvedMailbox, SplitCsv(messageIds), isRead, cancellationToken);
			return BatchResult(resolvedMailbox, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error setting read state: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_delete")]
	[Description("Soft-delete one or more messages — move them to the Trash/Deleted-Items folder. "
		+ "`messageIds` is comma-separated. There is NO hard-delete: deletion is always a move, by design "
		+ "(IMAP recovery window is the server's Trash retention, not a guaranteed 30 days). Batched: per-item "
		+ "result carries the message's NEW id and previous fromFolderId (so a wrongful delete can be moved "
		+ "back). Targets `mailbox` or the configured default.")]
	public static async Task<CallToolResult> Delete(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageIds,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var results = await emailService.DeleteAsync(resolvedMailbox, SplitCsv(messageIds), cancellationToken);
			return BatchResult(resolvedMailbox, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error deleting messages: {ex.Message}");
		}
	}

	// Blast-radius cap for a single bulk write — a bound even when the caller passes a large maxAffected.
	private const int BulkMaxCap = 500;

	[McpServerTool(Name = "email_bulk_move")]
	[Description("Bulk MOVE by filter — the apply-to-result-set form of email_edit_move: file every message "
		+ "matching a filter into `destinationFolderId`, with no id marshalling. Same filter grammar as "
		+ "email_read_list (fromContains, subjectContains, toContains, recipientContains, since, before, isRead, isFlagged, folder, "
		+ "includeSpamAndTrash) — so PREVIEW with email_read_list, then apply the identical filter. `excludeIds` "
		+ "(comma-separated) carves keepers out of the matched set: match the group, keep the ones you want, "
		+ "move the rest. `maxAffected` is REQUIRED and bounds the blast radius: if the filter matches more than "
		+ "that after exclusions, the call is REFUSED (never acts on a silent subset). `dryRun` DEFAULTS TO "
		+ "TRUE: returns {wouldAffect, sample of matched subjects}, writes nothing — pass dryRun:false to move. "
		+ "A filter needs a narrowing predicate beyond folder. Returns {matched, excluded, affected, "
		+ "filtersApplied, results}. Targets `mailbox` or the default.")]
	public static async Task<CallToolResult> BulkMove(
		IConfiguration configuration,
		HttpClient httpClient,
		string destinationFolderId,
		int maxAffected,
		string? fromContains = null,
		string? subjectContains = null,
		string? since = null,
		string? before = null,
		bool? isRead = null,
		bool? isFlagged = null,
		string? folder = null,
		bool includeSpamAndTrash = false,
		string? toContains = null,
		string? recipientContains = null,
		string? excludeIds = null,
		bool dryRun = true,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(destinationFolderId)) return InputError("`destinationFolderId` is required.");
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var (targets, matched, excluded, filtersApplied, error) = await ResolveBulkAsync(
				emailService, resolvedMailbox, fromContains, subjectContains, toContains, recipientContains, since, before, isRead, isFlagged, folder, includeSpamAndTrash, excludeIds, maxAffected, cancellationToken);
			if (error is not null) return error;
			if (dryRun) return BulkDryRun(resolvedMailbox, matched, excluded, targets!, filtersApplied);
			var ids = targets!.Select(m => m.Id).ToList();
			IReadOnlyList<BatchItemResult> results = ids.Count == 0
				? Array.Empty<BatchItemResult>()
				: await emailService.MoveAsync(resolvedMailbox, ids, destinationFolderId, cancellationToken);
			return BulkResult(resolvedMailbox, matched, excluded, filtersApplied, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error in bulk move: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_bulk_delete")]
	[Description("Bulk SOFT-DELETE by filter — the apply-to-result-set form of email_delete: move every message "
		+ "matching a filter to Trash / Deleted Items (recoverable; there is NO hard delete). Same filter grammar "
		+ "as email_read_list — PREVIEW with email_read_list, then apply the identical filter. `excludeIds` "
		+ "(comma-separated) carves keepers out: match the group, keep the ones you want, delete the rest. "
		+ "`maxAffected` is REQUIRED and bounds the blast radius: if the filter matches more than that after "
		+ "exclusions, the call is REFUSED. `dryRun` DEFAULTS TO TRUE: returns {wouldAffect, sample of matched "
		+ "subjects}, writes nothing — pass dryRun:false to delete. A filter needs a narrowing predicate beyond "
		+ "folder. Returns {matched, excluded, affected, filtersApplied, results}. Targets `mailbox` or the default.")]
	public static async Task<CallToolResult> BulkDelete(
		IConfiguration configuration,
		HttpClient httpClient,
		int maxAffected,
		string? fromContains = null,
		string? subjectContains = null,
		string? since = null,
		string? before = null,
		bool? isRead = null,
		bool? isFlagged = null,
		string? folder = null,
		bool includeSpamAndTrash = false,
		string? toContains = null,
		string? recipientContains = null,
		string? excludeIds = null,
		bool dryRun = true,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var (targets, matched, excluded, filtersApplied, error) = await ResolveBulkAsync(
				emailService, resolvedMailbox, fromContains, subjectContains, toContains, recipientContains, since, before, isRead, isFlagged, folder, includeSpamAndTrash, excludeIds, maxAffected, cancellationToken);
			if (error is not null) return error;
			if (dryRun) return BulkDryRun(resolvedMailbox, matched, excluded, targets!, filtersApplied);
			var ids = targets!.Select(m => m.Id).ToList();
			IReadOnlyList<BatchItemResult> results = ids.Count == 0
				? Array.Empty<BatchItemResult>()
				: await emailService.DeleteAsync(resolvedMailbox, ids, cancellationToken);
			return BulkResult(resolvedMailbox, matched, excluded, filtersApplied, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error in bulk delete: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_bulk_read")]
	[Description("Bulk MARK READ / UNREAD by filter — the apply-to-result-set form of email_edit_read: set "
		+ "read state on every message matching a filter. `isRead` true marks read, false marks unread. Same "
		+ "filter grammar as email_read_list; `excludeIds` carves keepers out; `maxAffected` REQUIRED and "
		+ "refuses on overflow. `dryRun` DEFAULTS TO TRUE (returns {wouldAffect, sample}, writes nothing) — "
		+ "pass dryRun:false to apply. A filter needs a narrowing predicate beyond folder. Targets `mailbox` "
		+ "or the default.")]
	public static async Task<CallToolResult> BulkSetRead(
		IConfiguration configuration,
		HttpClient httpClient,
		bool isRead,
		int maxAffected,
		string? fromContains = null,
		string? subjectContains = null,
		string? since = null,
		string? before = null,
		bool? isFlagged = null,
		string? folder = null,
		bool includeSpamAndTrash = false,
		string? toContains = null,
		string? recipientContains = null,
		string? excludeIds = null,
		bool dryRun = true,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			// Filter by the CURRENT state (the opposite of the target) so a re-run is a no-op, not churn.
			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var (targets, matched, excluded, filtersApplied, error) = await ResolveBulkAsync(
				emailService, resolvedMailbox, fromContains, subjectContains, toContains, recipientContains, since, before, !isRead, isFlagged, folder, includeSpamAndTrash, excludeIds, maxAffected, cancellationToken);
			if (error is not null) return error;
			if (dryRun) return BulkDryRun(resolvedMailbox, matched, excluded, targets!, filtersApplied);
			var ids = targets!.Select(m => m.Id).ToList();
			IReadOnlyList<BatchItemResult> results = ids.Count == 0
				? Array.Empty<BatchItemResult>()
				: await emailService.SetReadStateAsync(resolvedMailbox, ids, isRead, cancellationToken);
			return BulkResult(resolvedMailbox, matched, excluded, filtersApplied, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error in bulk set-read: {ex.Message}");
		}
	}

	// Resolve a bulk filter to the ids it should act on: run the same query email_read_list uses (bounded to
	// a window past maxAffected so overflow is detectable), drop excludeIds, and enforce the guardrails —
	// require a bound, require a filter, and refuse rather than act on a silent subset. Returns the id list
	// OR an error result, never both.
	private static async Task<(List<EmailMessage>? targets, int matched, int excluded, Dictionary<string, object?> filtersApplied, CallToolResult? error)> ResolveBulkAsync(
		IEmailService emailService, string mailbox,
		string? fromContains, string? subjectContains, string? toContains, string? recipientContains, string? since, string? before,
		bool? isRead, bool? isFlagged, string? folder, bool includeSpamAndTrash,
		string? excludeIds, int maxAffected, CancellationToken ct)
	{
		Dictionary<string, object?> empty = [];
		if (maxAffected <= 0)
			return (null, 0, 0, empty, InputError("`maxAffected` is required and must be > 0 — a bulk write must be bounded."));
		if (maxAffected > BulkMaxCap)
			return (null, 0, 0, empty, InputError($"`maxAffected` ({maxAffected}) exceeds the per-call cap of {BulkMaxCap}. Narrow the filter or run it in batches."));
		if (ParseCutoff(since, nameof(since), out var receivedAfter) is { } e1) return (null, 0, 0, empty, InputError(e1));
		if (ParseCutoff(before, nameof(before), out var receivedBefore) is { } e2) return (null, 0, 0, empty, InputError(e2));

		var predicates = new List<EmailPredicate>();
		if (!string.IsNullOrWhiteSpace(fromContains)) predicates.Add(new EmailPredicate(EmailField.From, EmailOperator.Contains, fromContains));
		if (!string.IsNullOrWhiteSpace(subjectContains)) predicates.Add(new EmailPredicate(EmailField.Subject, EmailOperator.Contains, subjectContains));
		if (!string.IsNullOrWhiteSpace(toContains)) predicates.Add(new EmailPredicate(EmailField.To, EmailOperator.Contains, toContains));
		if (!string.IsNullOrWhiteSpace(recipientContains)) predicates.Add(new EmailPredicate(EmailField.Recipient, EmailOperator.Contains, recipientContains));

		// Require a NARROWING predicate beyond folder — "everything in a folder" must not be expressible by
		// accident (the agent's guardrail after the 13 Aug incident). folder alone is scope, not a filter.
		bool anyNarrowing = predicates.Count > 0 || isRead.HasValue || isFlagged.HasValue
			|| receivedAfter is not null || receivedBefore is not null;
		if (!anyNarrowing)
			return (null, 0, 0, empty, InputError("Refusing a bulk write with no narrowing predicate beyond folder — specify at least one of fromContains, subjectContains, since, before, isRead, isFlagged."));

		var exclude = new HashSet<string>(SplitCsv(excludeIds), StringComparer.Ordinal);

		// Fetch one past the bound (plus room for the excluded ids) so a set larger than maxAffected is visible.
		var query = new EmailQuery
		{
			Folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim(),
			IsRead = isRead,
			IsFlagged = isFlagged,
			Received = receivedAfter is not null || receivedBefore is not null ? new DateRange { After = receivedAfter, Before = receivedBefore } : null,
			Predicates = predicates,
			ExcludeDeletedAndJunk = string.IsNullOrWhiteSpace(folder) && !includeSpamAndTrash,
			Sort = EmailSort.ReceivedDescending,
			Top = maxAffected + exclude.Count + 1,
			Skip = 0,
		};

		var matched = await emailService.GetMessagesAsync(mailbox, query, ct);
		var targets = matched.Where(m => !exclude.Contains(m.Id)).ToList();
		int excludedCount = matched.Count - targets.Count;

		var filtersApplied = new Dictionary<string, object?>();
		if (!string.IsNullOrWhiteSpace(fromContains)) filtersApplied["fromContains"] = fromContains;
		if (!string.IsNullOrWhiteSpace(subjectContains)) filtersApplied["subjectContains"] = subjectContains;
		if (!string.IsNullOrWhiteSpace(toContains)) filtersApplied["toContains"] = toContains;
		if (!string.IsNullOrWhiteSpace(recipientContains)) filtersApplied["recipientContains"] = recipientContains;
		if (!string.IsNullOrWhiteSpace(since)) filtersApplied["since"] = since;
		if (!string.IsNullOrWhiteSpace(before)) filtersApplied["before"] = before;
		if (isRead.HasValue) filtersApplied["isRead"] = isRead.Value;
		if (isFlagged.HasValue) filtersApplied["isFlagged"] = isFlagged.Value;
		if (!string.IsNullOrWhiteSpace(folder)) filtersApplied["folder"] = folder.Trim();
		filtersApplied["includeSpamAndTrash"] = includeSpamAndTrash;
		if (exclude.Count > 0) filtersApplied["excludeIds"] = exclude.Count;

		if (targets.Count > maxAffected)
			return (null, matched.Count, excludedCount, filtersApplied, InputError(
				$"Bulk write would affect at least {targets.Count} messages, over maxAffected ({maxAffected}). Narrow the filter or raise maxAffected (cap {BulkMaxCap}). Nothing was changed."));

		return (targets, matched.Count, excludedCount, filtersApplied, null);
	}

	// dryRun (default true): return what WOULD be affected — count + a sample of matched subjects — without
	// writing. The destructive call must be a second, deliberate dryRun:false. Same query path as the real
	// run, so preview and effect cannot diverge.
	private static CallToolResult BulkDryRun(string mailbox, int matched, int excluded, IReadOnlyList<EmailMessage> targets, IReadOnlyDictionary<string, object?> filtersApplied)
	{
		const int SampleSize = 10;
		var payload = new
		{
			mailbox,
			dryRun = true,
			matched,
			excluded,
			wouldAffect = targets.Count,
			filtersApplied,
			sample = targets.Take(SampleSize).Select(m => new { id = m.Id, subject = m.Subject, from = m.FromAddress, received = m.ReceivedDateTime }).ToArray(),
		};
		return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
	}

	private static CallToolResult BulkResult(string mailbox, int matched, int excluded, IReadOnlyDictionary<string, object?> filtersApplied, IReadOnlyList<BatchItemResult> results)
	{
		var payload = new
		{
			mailbox,
			dryRun = false,
			matched,
			excluded,
			affected = results.Count(r => r.Success),
			filtersApplied,
			results,
		};
		return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
	}

	[McpServerTool(Name = "email_send")]
	[Description("Send a NEW email from `mailbox` or the configured default (via SMTP). `to` (and optional "
		+ "`cc`/`bcc`) are comma- or semicolon-separated addresses; `subject` + `body`; `bodyFormat` `text` "
		+ "(default) or `html`. Named alone so it can be permission-gated separately (it sends outbound mail). "
		+ "The MCP is a mechanism — deciding WHETHER and WHAT to send is the caller's policy. Sending is OFF by "
		+ "default and must be enabled by the operator with Imap:Auth:AllowSend=true. "
		+ "`saveToSentItems` (default true) appends a copy to the Sent folder when one is resolvable.")]
	public static async Task<CallToolResult> Send(
		IConfiguration configuration,
		HttpClient httpClient,
		string to,
		string body,
		string? subject = null,
		string? cc = null,
		string? bcc = null,
		string bodyFormat = "text",
		bool saveToSentItems = true,
		string? mailbox = null,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			// Operator gate — outbound send is OFF unless an operator opts in (safe default for an
			// irreversible, outward action). Independent of what the caller asks.
			if (!configuration.GetValue("Imap:Auth:AllowSend", false))
				return InputError("Sending is disabled by default. An operator must set Imap:Auth:AllowSend=true to enable email_send.");

			var recipients = SplitAddresses(to);
			if (recipients.Count == 0)
				return InputError("`to` must contain at least one recipient address.");

			bool isHtml = string.Equals(bodyFormat?.Trim(), "html", StringComparison.OrdinalIgnoreCase);

			var (emailService, resolvedMailbox) = CreateServiceFromConfig(configuration, typedSecrets, genericSecrets, mailbox);
			var draft = new EmailDraft
			{
				To = recipients,
				Cc = SplitAddresses(cc),
				Bcc = SplitAddresses(bcc),
				Subject = subject,
				Body = body ?? string.Empty,
				BodyFormat = isHtml ? EmailBodyFormat.Html : EmailBodyFormat.Text,
				SaveToSentItems = saveToSentItems,
			};
			var result = await emailService.SendAsync(resolvedMailbox, draft, cancellationToken);
			if (!result.Success)
				return InputError($"Error sending email: {result.Error}");

			var payload = new { mailbox = resolvedMailbox, sent = true, to = recipients, cc = draft.Cc, bcc = draft.Bcc, messageId = result.MessageId };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error sending email: {ex.Message}");
		}
	}

	// Build an ImapEmailService from configuration + secrets and resolve the effective mailbox. IMAP is
	// single-account per credential: `mailbox` selects the configured account/default (it does NOT re-target
	// arbitrary mailboxes the way Graph's /users/{upn} does) and rides the response envelope + send From.
	private static (ImapEmailService service, string mailbox) CreateServiceFromConfig(
		IConfiguration configuration,
		UserSecretHelper<ImapEmailConfiguration>? typedSecrets,
		ISecretHelper? genericSecrets,
		string? mailboxOverride)
	{
		var s = typedSecrets?.Settings;
		var config = new ImapEmailConfiguration
		{
			Host = s?.Host ?? configuration["Imap:Auth:Host"],
			Port = configuration.GetValue("Imap:Auth:Port", s?.Port ?? 993),
			UseSsl = configuration.GetValue("Imap:Auth:UseSsl", s?.UseSsl ?? true),
			Username = s?.Username ?? configuration["Imap:Auth:Username"],
			Password = s?.Password ?? configuration["Imap:Auth:Password"],
			AuthMode = s?.AuthMode ?? configuration["Imap:Auth:AuthMode"] ?? "basic",
			DefaultMailbox = s?.DefaultMailbox ?? configuration["Imap:Auth:DefaultMailbox"],
			RedactBodyPreview = configuration.GetValue("Imap:Auth:RedactBodyPreview", s?.RedactBodyPreview ?? true),
			AllowSend = configuration.GetValue("Imap:Auth:AllowSend", s?.AllowSend ?? false),
			SmtpHost = configuration["Imap:Smtp:Host"] ?? s?.SmtpHost,
			SmtpPort = configuration.GetValue("Imap:Smtp:Port", s?.SmtpPort ?? 587),
			SmtpUseStartTls = configuration.GetValue("Imap:Smtp:UseStartTls", s?.SmtpUseStartTls ?? true),
		};

		string? mailbox = string.IsNullOrWhiteSpace(mailboxOverride) ? config.DefaultMailbox : mailboxOverride.Trim();
		mailbox ??= config.Username;
		if (string.IsNullOrEmpty(mailbox))
			throw new InvalidOperationException("No mailbox: pass a `mailbox` or configure Imap:Auth:DefaultMailbox.");

		return (new ImapEmailService(config), mailbox);
	}

	// Compact + null-omitting: lists carry no message body (BodyContent is null), so drop it from the wire.
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
	};

	// Operator toggle (default on): scrub BodyPreview secrets before they reach the model. Read from
	// configuration, not the per-call args — a model must not be able to turn its own safety net off.
	private static bool RedactPreviews(IConfiguration configuration)
		=> configuration.GetValue("Imap:Auth:RedactBodyPreview", true);

	// Parse an optional ISO-8601 date/datetime cutoff to UTC. Returns null on success (value set) or an
	// error message when the string is present but unparseable — so a bad date is reported, not ignored.
	private static string? ParseCutoff(string? s, string paramName, out DateTimeOffset? value)
	{
		value = null;
		if (string.IsNullOrWhiteSpace(s))
			return null;
		if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
		{
			value = dt;
			return null;
		}
		return $"Invalid {paramName} '{s}': expected an ISO-8601 date or datetime, e.g. 2026-08-01 or 2026-08-01T00:00:00Z.";
	}

	private static CallToolResult InputError(string message) => new()
	{
		IsError = true,
		Content = [new TextContentBlock { Text = message }]
	};

	// Parse the tool's bodyFormat arg: "html" → Html, anything else (incl. null/"text") → Text.
	private static EmailBodyFormat ParseBodyFormat(string? bodyFormat)
		=> string.Equals(bodyFormat?.Trim(), "html", StringComparison.OrdinalIgnoreCase)
			? EmailBodyFormat.Html
			: EmailBodyFormat.Text;

	// Split a comma-separated tool argument (e.g. id list) into a trimmed, non-empty list.
	private static IReadOnlyList<string> SplitCsv(string? csv)
		=> string.IsNullOrWhiteSpace(csv)
			? []
			: csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	// Split a recipient argument on comma OR semicolon (both are common in mail clients).
	private static IReadOnlyList<string> SplitAddresses(string? addresses)
		=> string.IsNullOrWhiteSpace(addresses)
			? []
			: addresses.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	// Shape an attachment fetch: write to disk when saveToDirectory is set (returns savedPath), else return
	// base64 under the caller-configured inline cap (Imap:Auth:MaxInlineAttachmentBytes).
	private static CallToolResult AttachmentResult(string mailbox, string messageId, EmailAttachmentContent att, string? saveToDirectory, long maxInlineBytes)
	{
		if (!string.IsNullOrWhiteSpace(saveToDirectory))
		{
			Directory.CreateDirectory(saveToDirectory);
			string path = Path.Combine(saveToDirectory, SafeFileName(att.Name));
			File.WriteAllBytes(path, att.Content);
			var saved = new { mailbox, messageId, id = att.Id, name = att.Name, contentType = att.ContentType, size = att.Size, isInline = att.IsInline, savedPath = path };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(saved, JsonOptions) }] };
		}

		if (att.Content.LongLength > maxInlineBytes)
			return InputError($"Attachment '{att.Name}' is {att.Content.LongLength:N0} bytes (over the {maxInlineBytes:N0}-byte inline cap, Imap:Auth:MaxInlineAttachmentBytes); pass saveToDirectory to write it to disk instead of returning base64.");

		var payload = new { mailbox, messageId, id = att.Id, name = att.Name, contentType = att.ContentType, size = att.Size, isInline = att.IsInline, contentBase64 = Convert.ToBase64String(att.Content) };
		return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
	}

	// Strip path separators / invalid filename chars so a caller-controlled attachment name can't escape
	// the target directory.
	private static string SafeFileName(string name)
	{
		string cleaned = new([.. name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c)]);
		cleaned = cleaned.Trim().TrimStart('.');
		return string.IsNullOrEmpty(cleaned) ? "attachment" : cleaned;
	}

	// Envelope for a batched write: totals + per-item results. Null fields (error on success; newId/
	// fromFolderId for non-move ops) are dropped by the serializer.
	private static CallToolResult BatchResult(string mailbox, IReadOnlyList<BatchItemResult> results)
	{
		var payload = new
		{
			mailbox,
			count = results.Count,
			succeeded = results.Count(r => r.Success),
			failed = results.Count(r => !r.Success),
			results,
		};
		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }]
		};
	}

	/// <summary>
	/// Wrap a page of messages in a lean, paging-aware envelope. <c>count</c> is this page's size,
	/// <c>hasMore</c> is a best-effort "there may be another page" (we got a full page), and
	/// <c>nextSkip</c> is the offset to pass back as <c>skip</c> to fetch it.
	/// </summary>
	// §13: echo the scope the query actually ran with. includeSpamAndTrash is scope-affecting, and the
	// Deleted/Junk exclusion is a default the caller never passed — both must be visible so a caller can
	// assert the scope it got (the same "echo the effective query" principle as filtersApplied itself).
	private static void EchoScope(Dictionary<string, object?> filtersApplied, bool includeSpamAndTrash, bool excludesDeletedAndJunk)
	{
		filtersApplied["includeSpamAndTrash"] = includeSpamAndTrash;
		if (excludesDeletedAndJunk)
			filtersApplied["excludesDeletedAndJunk"] = true;
	}

	private static CallToolResult LeanList(IReadOnlyList<EmailMessage> messages, int pageSize, int skip, bool redact, string mailbox, bool verbose, IReadOnlyDictionary<string, object?> filtersApplied, int bodyMaxChars, bool includePreview = true)
	{
		var payload = new
		{
			mailbox,
			count = messages.Count,
			// D6: echo the effective filter this call actually applied. An unrecognised/typo'd arg is silently
			// discarded upstream, so a caller can't tell a real unfiltered page from a dropped filter — here it
			// is detectable by ABSENCE: a key that's missing was NOT applied, so assert on what you asked for.
			filtersApplied,
			skip,
			hasMore = pageSize > 0 && messages.Count >= pageSize,
			nextSkip = skip + messages.Count,
			verbose,
			messages = ProjectRows(messages, redact, verbose, bodyMaxChars, includePreview),
		};
		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }]
		};
	}

	// Project rows for the wire. Default (lean) drops the long fields that dominate payload —
	// internetMessageId, parentFolderId — which an agent rarely reads at triage time; `verbose` keeps the
	// full shape for dedup/undo. BodyPreview is scrubbed of bearer-shaped secrets either way (#508). The
	// detail is always available via email_read_body.
	private static object ProjectRows(IReadOnlyList<EmailMessage> messages, bool redact, bool verbose, int bodyMaxChars, bool includePreview = true)
	{
		string Preview(EmailMessage m) => redact ? SecretScrubber.Scrub(m.BodyPreview) : m.BodyPreview;

		// H6: when bodyMaxChars > 0, cap an included body to that many chars (0 = full, the default). Only
		// bites when includeBody populated BodyContent; null bodies pass through untouched.
		string? Cap(string? body) => bodyMaxChars > 0 && body is { Length: var len } && len > bodyMaxChars ? body[..bodyMaxChars] : body;

		if (verbose)
			return messages.Select(m => m with { BodyPreview = includePreview ? Preview(m) : "", BodyContent = Cap(m.BodyContent) }).ToList();

		return messages.Select(m => new
		{
			m.Id,
			m.Subject,
			m.FromAddress,
			m.FromName,
			m.ReceivedDateTime,
			m.IsRead,
			m.IsFlagged,
			m.HasAttachments,
			m.ToRecipients,
			CcRecipients = m.CcRecipients.Count > 0 ? m.CcRecipients : null,
			// §19: drop BodyPreview (≈47% of a lean row) when includePreview is false — null → omitted.
			BodyPreview = includePreview ? Preview(m) : null,
			// Kept only when includeBody populated it — otherwise null and dropped by the serializer, so the
			// default row stays lean. Capped to bodyMaxChars when set.
			BodyContent = Cap(m.BodyContent),
			BodyContentType = m.BodyContent is not null ? m.BodyContentType : null,
		}).ToList<object>();
	}
}
