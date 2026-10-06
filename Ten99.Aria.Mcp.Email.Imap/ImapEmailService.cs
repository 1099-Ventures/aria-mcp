using System.Net;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using Ten99.Aria.Integration.Graph.Email;
using Ten99.Aria.Mcp.Email.Imap.Configuration;
using Ten99.Aria.Mcp.Email.Imap.Imap;

namespace Ten99.Aria.Mcp.Email.Imap;

/// <summary>
/// <see cref="IEmailService"/> over IMAP/SMTP (MailKit) — the second provider behind the shared,
/// provider-neutral contract in <c>Ten99.Aria.Integration.Graph.Email</c>. Translates
/// <see cref="EmailQuery"/>/<see cref="EmailFilter"/> into IMAP verbs (SELECT/SEARCH/SORT/FETCH/STORE/
/// MOVE) and projects IMAP envelope/flag/bodystructure data into <see cref="EmailMessage"/>, matching
/// <c>GraphEmailService</c>'s behaviour (lean projection, newest-first, junk/deleted exclusion, batched
/// writes with per-item results, move returning NewId+FromFolderId, delete = soft move to Trash).
///
/// P0 implements read + flags + move + delete + send. The category/ledger/delta/threading members throw
/// <see cref="NotSupportedException"/> with a clear reason — IMAP has no native equivalent and the
/// substitutes (keywords/X-GM-LABELS, sidecar store, QRESYNC, THREAD) are deferred to P1/P2 per the
/// IMAP-Email-MCP-Scoping design doc. They are never faked.
///
/// Message identity: tool ids are encoded <c>folder:UIDVALIDITY:UID</c> — a bare IMAP UID is meaningless
/// without its folder and UIDVALIDITY, and UIDs change on move. The stable RFC5322 Message-ID maps to
/// <see cref="EmailMessage.InternetMessageId"/>.
/// </summary>
public sealed class ImapEmailService(ImapEmailConfiguration config) : IEmailService
{
	private readonly ImapEmailConfiguration _config = config;

	// Per-folder scan ceiling when the server lacks SORT (we sort client-side, so bound the fetch).
	private const int MaxScanPerFolder = 500;
	// Backstop against a pathological folder tree when sweeping the whole mailbox.
	private const int MaxFolders = 200;

	// ---- Well-known folder aliases used when SPECIAL-USE isn't advertised (provider owns this table). ----
	private static readonly IReadOnlyDictionary<string, string[]> WellKnownAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["sentitems"] = ["Sent", "Sent Items", "Sent Mail", "Sent Messages"],
		["deleteditems"] = ["Trash", "Deleted", "Deleted Items", "Deleted Messages"],
		["junkemail"] = ["Junk", "Spam", "Junk E-mail", "Junk Email", "Bulk Mail"],
		["archive"] = ["Archive", "Archives", "All Mail"],
		["drafts"] = ["Drafts", "Draft"],
	};

	private static readonly IReadOnlyDictionary<string, SpecialFolder> WellKnownSpecial = new Dictionary<string, SpecialFolder>(StringComparer.OrdinalIgnoreCase)
	{
		["sentitems"] = SpecialFolder.Sent,
		["deleteditems"] = SpecialFolder.Trash,
		["junkemail"] = SpecialFolder.Junk,
		["archive"] = SpecialFolder.Archive,
		["drafts"] = SpecialFolder.Drafts,
	};

	// =====================================================================================
	//  P0: reads
	// =====================================================================================

	public async Task<IReadOnlyList<EmailMessage>> GetMessagesAsync(string mailbox, EmailQuery query, CancellationToken ct = default)
	{
		if (query.ConversationIds.Count > 0)
			throw new NotSupportedException(
				"conversationId thread-walking is not supported on the IMAP backend. IMAP has no native cross-folder "
				+ "conversation id; the substitutes (Gmail X-GM-THRID, the THREAD extension, or References/In-Reply-To "
				+ "header walking) are a P2 item per the IMAP-Email-MCP-Scoping doc.");

		using var client = await ImapConnection.OpenImapAsync(_config, ct);

		var scope = await ResolveScopeAsync(client, query, ct);
		bool ascending = query.Sort == EmailSort.ReceivedAscending;

		var hits = new List<Hit>();
		int need = Math.Max(query.Skip + query.Top, query.Top);

		foreach (var folder in scope)
		{
			ct.ThrowIfCancellationRequested();
			await folder.OpenAsync(FolderAccess.ReadOnly, ct);

			var search = BuildSearchQuery(query);
			IList<UniqueId> uids;
			if (client.Capabilities.HasFlag(ImapCapabilities.Sort))
			{
				var orderBy = ascending ? OrderBy.Date : OrderBy.ReverseDate;
				uids = await folder.SortAsync(search, [orderBy], ct);
				if (need > 0 && uids.Count > need)
					uids = [.. uids.Take(need)]; // already ordered — the newest/oldest `need` suffice for the merge
			}
			else
			{
				uids = await folder.SearchAsync(search, ct);
				// SEARCH returns ascending UIDs (~arrival); the newest sit at the high end. Bound the fetch.
				if (uids.Count > MaxScanPerFolder)
					uids = [.. uids.Skip(uids.Count - MaxScanPerFolder)];
			}

			if (uids.Count == 0)
				continue;

			var summaries = await folder.FetchAsync(
				uids,
				new FetchRequest(MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.Size
					| MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate | MessageSummaryItems.UniqueId),
				ct);

			foreach (var s in summaries)
				hits.Add(new Hit(folder, s, ReceivedOf(s)));
		}

		// Refine the coarse (date-granular) IMAP SEARCH window to the exact DateTimeOffset bounds.
		if (query.Received?.After is { } after)
			hits = [.. hits.Where(h => h.Received >= after)];
		if (query.Received?.Before is { } before)
			hits = [.. hits.Where(h => h.Received <= before)];

		hits = ascending
			? [.. hits.OrderBy(h => h.Received)]
			: [.. hits.OrderByDescending(h => h.Received)];

		var page = hits.Skip(query.Skip).Take(query.Top).ToList();

		// Synthesize bodyPreview only for the final page (a per-row part fetch) — never for the whole hit set.
		var result = new List<EmailMessage>(page.Count);
		foreach (var group in page.GroupBy(h => h.Folder))
		{
			await group.Key.OpenAsync(FolderAccess.ReadOnly, ct);
			foreach (var h in group)
			{
				string preview = await SynthesizePreviewAsync(group.Key, h.Summary, ct);
				var row = Map(h.Folder, h.Summary, query.IncludeCc, preview);

				// Opt-in: widen the row with the full body. IMAP has no bulk body-in-list, so this is one extra
				// GetMessage fetch per row — only done when includeBody is set; the default path stays lean.
				if (query.IncludeBody)
				{
					var mime = await group.Key.GetMessageAsync(h.Summary.UniqueId, ct);
					string? bodyContent;
					string bodyType;
					if (query.BodyFormat == EmailBodyFormat.Html)
					{
						bodyContent = mime.HtmlBody ?? mime.TextBody;
						bodyType = mime.HtmlBody is not null ? "html" : "text";
					}
					else
					{
						bodyContent = mime.TextBody ?? StripHtml(mime.HtmlBody);
						bodyType = "text";
					}
					row = row with
					{
						BodyContent = bodyContent,
						BodyContentType = bodyContent is null ? null : bodyType,
					};
				}

				result.Add(row);
			}
		}

		// Preserve the global ordering the page had before we regrouped by folder.
		var order = page.Select(h => Encode(h.Folder.FullName, h.Folder.UidValidity, h.Summary.UniqueId.Id)).ToList();
		return [.. result.OrderBy(m => order.IndexOf(m.Id))];
	}

	public Task<IReadOnlyList<EmailMessage>> GetUnreadAsync(string mailbox, EmailFilter? filter = null, int maxResults = 50, int skip = 0, CancellationToken ct = default)
		=> GetMessagesAsync(mailbox, ToQuery(filter, maxResults, skip) with { IsRead = false }, ct);

	public Task<IReadOnlyList<EmailMessage>> GetFlaggedAsync(string mailbox, EmailFilter? filter = null, int maxResults = 50, int skip = 0, CancellationToken ct = default)
		=> GetMessagesAsync(mailbox, ToQuery(filter, maxResults, skip) with { IsRead = null, IsFlagged = true }, ct);

	public async Task<EmailMessage?> GetMessageAsync(string mailbox, string messageId, EmailBodyFormat bodyFormat = EmailBodyFormat.Text,
		string? propertySetGuid = null, string? propertyName = null, CancellationToken ct = default)
	{
		// propertySetGuid/propertyName (the extended-property ledger) has no IMAP equivalent — it's a P2
		// substitute (agent-side sidecar). We don't fail the read; PropertyValue simply stays null.
		if (!TryDecode(messageId, out var pid))
			throw new ArgumentException($"Malformed message id '{messageId}'. Expected 'folder:UIDVALIDITY:UID'.");

		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		var folder = await GetFolderByPathAsync(client, pid.Folder, ct);
		if (folder is null)
			return null;

		await folder.OpenAsync(FolderAccess.ReadOnly, ct);
		if (folder.UidValidity != pid.Validity)
			return null; // UIDVALIDITY changed — the id is stale; caller should re-list.

		var summaries = await folder.FetchAsync(
			[pid.Uid],
			new FetchRequest(MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.InternalDate
				| MessageSummaryItems.BodyStructure | MessageSummaryItems.UniqueId),
			ct);
		var summary = summaries.FirstOrDefault();
		if (summary is null)
			return null;

		var mime = await folder.GetMessageAsync(pid.Uid, ct);

		string? bodyContent;
		string bodyType;
		if (bodyFormat == EmailBodyFormat.Html)
		{
			bodyContent = mime.HtmlBody ?? mime.TextBody;
			bodyType = mime.HtmlBody is not null ? "html" : "text";
		}
		else
		{
			bodyContent = mime.TextBody ?? StripHtml(mime.HtmlBody);
			bodyType = "text";
		}

		var mapped = Map(folder, summary, includeCc: true, preview: Normalize(mime.TextBody ?? StripHtml(mime.HtmlBody), 250));
		return mapped with
		{
			BodyContent = bodyContent,
			BodyContentType = bodyContent is null ? null : bodyType,
			HasAttachments = mime.Attachments.Any(),
			CcRecipients = mime.Cc.Mailboxes.Select(a => a.Address).ToList(),
			InternetMessageId = string.IsNullOrEmpty(mime.MessageId) ? mapped.InternetMessageId : mime.MessageId,
		};
	}

	public async Task<IReadOnlyList<EmailAttachmentInfo>> GetAttachmentsAsync(string mailbox, string messageId, CancellationToken ct = default)
	{
		if (!TryDecode(messageId, out var pid))
			throw new ArgumentException($"Malformed message id '{messageId}'. Expected 'folder:UIDVALIDITY:UID'.");

		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		var summary = await FetchStructureAsync(client, pid, ct);
		return summary is null ? [] : [.. AttachmentParts(summary).Select(MapAttachmentInfo)];
	}

	public async Task<EmailAttachmentContent?> GetAttachmentAsync(string mailbox, string messageId, string attachmentId, CancellationToken ct = default)
	{
		if (!TryDecode(messageId, out var pid))
			throw new ArgumentException($"Malformed message id '{messageId}'. Expected 'folder:UIDVALIDITY:UID'.");

		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		var folder = await GetFolderByPathAsync(client, pid.Folder, ct);
		if (folder is null) return null;
		await folder.OpenAsync(FolderAccess.ReadOnly, ct);
		if (folder.UidValidity != pid.Validity) return null;

		var summaries = await folder.FetchAsync([pid.Uid], new FetchRequest(MessageSummaryItems.BodyStructure | MessageSummaryItems.UniqueId), ct);
		var summary = summaries.FirstOrDefault();
		var part = summary is null ? null : AttachmentParts(summary).FirstOrDefault(p => string.Equals(p.PartSpecifier, attachmentId, StringComparison.Ordinal));
		if (part is null) return null;

		// Fetch just this body part. A file attachment comes back as a MimePart; an embedded forwarded
		// message (message/rfc822) comes back as a MessagePart (§12) — and since it is enumerated as an
		// attachment it must be fetchable. Decode the MimePart's content, or serialise the encapsulated
		// message to .eml bytes, so enumeration never advertises a part the fetch can't serve.
		var entity = await folder.GetBodyPartAsync(pid.Uid, part, ct);
		using var ms = new MemoryStream();
		string? name;
		string? contentType;
		switch (entity)
		{
			case MimePart mime when mime.Content is not null:
				await mime.Content.DecodeToAsync(ms, ct);
				name = part.FileName ?? mime.FileName;
				contentType = part.ContentType?.MimeType ?? mime.ContentType?.MimeType;
				break;
			case MessagePart forwarded when forwarded.Message is not null:
				await forwarded.Message.WriteToAsync(ms, ct);
				name = part.FileName ?? Rfc822Name(forwarded);
				contentType = part.ContentType?.MimeType ?? "message/rfc822";
				break;
			default:
				return null;
		}

		return new EmailAttachmentContent
		{
			Id = part.PartSpecifier,
			Name = string.IsNullOrEmpty(name) ? "(unnamed)" : name,
			ContentType = contentType,
			Size = ms.Length,
			IsInline = string.Equals(part.ContentDisposition?.Disposition, "inline", StringComparison.OrdinalIgnoreCase),
			Content = ms.ToArray(),
		};
	}

	// A forwarded message rarely carries a filename; name the .eml from its subject so it is legible.
	private static string Rfc822Name(MessagePart part)
	{
		var subject = part.Message?.Subject;
		return string.IsNullOrWhiteSpace(subject) ? "forwarded-message.eml" : $"{subject}.eml";
	}

	private async Task<IMessageSummary?> FetchStructureAsync(ImapClient client, ParsedId pid, CancellationToken ct)
	{
		var folder = await GetFolderByPathAsync(client, pid.Folder, ct);
		if (folder is null) return null;
		await folder.OpenAsync(FolderAccess.ReadOnly, ct);
		if (folder.UidValidity != pid.Validity) return null;
		var summaries = await folder.FetchAsync([pid.Uid], new FetchRequest(MessageSummaryItems.BodyStructure | MessageSummaryItems.UniqueId), ct);
		return summaries.FirstOrDefault();
	}

	// The parts MailKit flags as attachments (disposition=attachment, plus named inline parts). PartSpecifier
	// is the fetch handle used as the attachment id; a bare UID part index is meaningless without the message.
	private static IEnumerable<BodyPartBasic> AttachmentParts(IMessageSummary summary)
		=> (summary.Attachments ?? []).OfType<BodyPartBasic>();

	private static EmailAttachmentInfo MapAttachmentInfo(BodyPartBasic p) => new()
	{
		Id = p.PartSpecifier,
		Name = string.IsNullOrEmpty(p.FileName) ? "(unnamed)" : p.FileName,
		ContentType = p.ContentType?.MimeType,
		Size = p.Octets,
		IsInline = string.Equals(p.ContentDisposition?.Disposition, "inline", StringComparison.OrdinalIgnoreCase),
	};

	public async Task<IReadOnlyList<MailFolderInfo>> GetFoldersAsync(string mailbox, string? parentFolderId = null, bool recursive = false, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);

		IMailFolder root = string.IsNullOrWhiteSpace(parentFolderId)
			? client.GetFolder(client.PersonalNamespaces[0])
			: await GetFolderByPathAsync(client, parentFolderId, ct)
				?? throw new InvalidOperationException($"Parent folder '{parentFolderId}' not found.");

		var result = new List<MailFolderInfo>();
		var queue = new Queue<IMailFolder>();
		queue.Enqueue(root);
		bool isRootNamespace = string.IsNullOrWhiteSpace(parentFolderId);

		while (queue.Count > 0 && result.Count < MaxFolders)
		{
			var parent = queue.Dequeue();
			// GetSubfolders with StatusItems issues LIST + STATUS in one shot, so counts come back inline.
			IList<IMailFolder> children;
			try
			{
				children = await parent.GetSubfoldersAsync(StatusItems.Count | StatusItems.Unread, subscribedOnly: false, ct);
			}
			catch (Exception) when (!ct.IsCancellationRequested)
			{
				continue; // \NoSelect containers etc. — nothing to enumerate under them.
			}

			foreach (var child in children)
			{
				bool hasChildren = child.Attributes.HasFlag(FolderAttributes.HasChildren);
				result.Add(new MailFolderInfo
				{
					Id = child.FullName,
					DisplayName = child.Name,
					// ParentFolderId is null for the top level of the requested scope; else the parent's path.
					ParentFolderId = (isRootNamespace && ReferenceEquals(parent, root)) ? null : parent.FullName,
					UnreadItemCount = child.Unread,
					TotalItemCount = child.Count,
					// IMAP advertises only a has-children flag, not an exact count — reported as 0/1.
					ChildFolderCount = hasChildren ? 1 : 0,
				});

				if (recursive && hasChildren)
					queue.Enqueue(child);
			}
		}

		return result;
	}

	public async Task<MailFolderInfo> CreateFolderAsync(string mailbox, string name, string? parentFolderId = null, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);

		bool topLevel = string.IsNullOrWhiteSpace(parentFolderId);
		IMailFolder parent = topLevel
			? client.GetFolder(client.PersonalNamespaces[0])
			: await GetFolderByPathAsync(client, parentFolderId!, ct)
				?? throw new InvalidOperationException($"Parent folder '{parentFolderId}' not found.");

		// Create-or-get: reuse an existing subfolder with that name so re-runs are safe. (MailKit's IMailFolder
		// returns aren't null-annotated, but both calls always yield a folder here — hence the null-forgiving.)
		IMailFolder folder;
		try { folder = (await parent.GetSubfolderAsync(name, ct))!; }
		catch (FolderNotFoundException) { folder = (await parent.CreateAsync(name, isMessageFolder: true, ct))!; }

		return new MailFolderInfo
		{
			Id = folder.FullName,
			DisplayName = folder.Name,
			ParentFolderId = topLevel ? null : parent.FullName,
			// Counts left at 0 — a fresh/looked-up folder needs a STATUS to populate them; not worth a round-trip here.
		};
	}

	public async Task<MailFolderInfo> RenameFolderAsync(string mailbox, string folderId, string newName, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		var folder = await GetFolderByPathAsync(client, folderId, ct)
			?? throw new InvalidOperationException($"Folder '{folderId}' not found.");

		// IMAP rename takes the (unchanged) parent + new leaf name; MailKit updates the folder's Name/FullName.
		var parent = folder.ParentFolder ?? client.GetFolder(client.PersonalNamespaces[0]);
		await folder.RenameAsync(parent, newName, ct);

		return new MailFolderInfo
		{
			Id = folder.FullName,
			DisplayName = folder.Name,
			ParentFolderId = string.IsNullOrEmpty(parent.FullName) ? null : parent.FullName,
		};
	}

	public async Task<MailFolderInfo> MoveFolderAsync(string mailbox, string folderId, string newParentFolderId, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		var folder = await GetFolderByPathAsync(client, folderId, ct)
			?? throw new InvalidOperationException($"Folder '{folderId}' not found.");
		var newParent = await ResolveFolderAsync(client, newParentFolderId, ct)
			?? throw new InvalidOperationException($"Target parent folder '{newParentFolderId}' could not be resolved.");

		if (IsSelfOrDescendant(folder, newParent))
			throw new InvalidOperationException("Cannot move a folder into itself or one of its own descendants.");

		// No merge onto an existing same-name sibling under the target.
		if (await GetChildByNameAsync(newParent, folder.Name, ct) is not null)
			throw new InvalidOperationException(
				$"A folder named '{folder.Name}' already exists under the target — refusing to merge. Rename one first.");

		// IMAP reparent = RENAME to a new path under the target parent, keeping the same leaf name.
		await folder.RenameAsync(newParent, folder.Name, ct);

		return new MailFolderInfo
		{
			Id = folder.FullName,
			DisplayName = folder.Name,
			ParentFolderId = string.IsNullOrEmpty(newParent.FullName) ? null : newParent.FullName,
		};
	}

	// Soft-delete = reparent under Trash (recoverable), NOT a DELETE + EXPUNGE. Mirrors message DeleteAsync;
	// there is no hard-delete path here.
	public async Task<FolderDeletionResult> DeleteFolderAsync(string mailbox, string folderId, bool force = false, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		var folder = await GetFolderByPathAsync(client, folderId, ct)
			?? throw new InvalidOperationException($"Folder '{folderId}' not found.");

		if (IsWellKnownFolder(client, folder))
			throw new InvalidOperationException($"Refusing to delete a well-known folder ('{folder.Name}').");

		// Empty-only unless forced: count both messages and child folders.
		await folder.StatusAsync(StatusItems.Count, ct);
		int childCount = (await folder.GetSubfoldersAsync(subscribedOnly: false, ct)).Count;
		if (!force && (folder.Count > 0 || childCount > 0))
			throw new InvalidOperationException(
				$"Folder '{folder.Name}' is not empty ({folder.Count} item(s), {childCount} subfolder(s)). Pass force to soft-delete it anyway.");

		var trash = await ResolveWellKnownAsync(client, "deleteditems", ct)
			?? throw new InvalidOperationException(
				"No Trash/Deleted-Items folder could be resolved, so there is no non-destructive delete path. "
				+ "(The MCP never hard-deletes.)");
		if (IsSelfOrDescendant(folder, trash))
			throw new InvalidOperationException("Refusing to delete the Trash folder itself.");

		string? previousParent = folder.ParentFolder?.FullName;

		// Disambiguate if a same-name folder is already in Trash, so the soft-delete never fails on a clash.
		string leaf = folder.Name;
		if (await GetChildByNameAsync(trash, leaf, ct) is not null)
			leaf = $"{folder.Name} (deleted {DateTime.UtcNow:yyyyMMddHHmmss})";
		await folder.RenameAsync(trash, leaf, ct);

		return new FolderDeletionResult
		{
			DeletedFolderId = folder.FullName,
			PreviousParentId = string.IsNullOrEmpty(previousParent) ? null : previousParent,
			Name = folder.Name,
		};
	}

	// Self, or target sits inside the folder's subtree — compared on FullName with the server's separator.
	private static bool IsSelfOrDescendant(IMailFolder folder, IMailFolder target)
	{
		if (ReferenceEquals(folder, target)) return true;
		if (string.Equals(folder.FullName, target.FullName, StringComparison.Ordinal)) return true;
		return target.FullName.StartsWith(folder.FullName + folder.DirectorySeparator, StringComparison.Ordinal);
	}

	private static async Task<IMailFolder?> GetChildByNameAsync(IMailFolder parent, string name, CancellationToken ct)
	{
		try { return await parent.GetSubfolderAsync(name, ct); }
		catch (FolderNotFoundException) { return null; }
	}

	// INBOX plus any SPECIAL-USE folder (RFC 6154), with a name-alias fallback for servers that don't advertise it.
	private static bool IsWellKnownFolder(ImapClient client, IMailFolder folder)
	{
		if (ReferenceEquals(folder, client.Inbox)) return true;
		if (string.Equals(folder.FullName, "INBOX", StringComparison.OrdinalIgnoreCase)) return true;

		const FolderAttributes special = FolderAttributes.Sent | FolderAttributes.Drafts | FolderAttributes.Trash
			| FolderAttributes.Junk | FolderAttributes.Archive | FolderAttributes.All | FolderAttributes.Flagged
			| FolderAttributes.Important;
		if ((folder.Attributes & special) != 0) return true;

		foreach (var aliases in WellKnownAliases.Values)
			if (aliases.Any(a => string.Equals(folder.Name, a, StringComparison.OrdinalIgnoreCase)))
				return true;
		return false;
	}

	public async Task<MailboxInfo> GetMailboxInfoAsync(string mailbox, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		await client.Inbox.StatusAsync(StatusItems.Count | StatusItems.Unread, ct);
		return new MailboxInfo
		{
			Mailbox = mailbox,
			// IMAP exposes no account profile/display name — use the address (unlike Graph's /me lookup).
			DisplayName = mailbox,
			UnreadCount = client.Inbox.Unread,
			TotalCount = client.Inbox.Count,
		};
	}

	/// <summary>Health payload for the IMAP backend: INBOX counts plus the negotiated capabilities the
	/// agent needs to know which substitutes are live. Not part of the contract — used only by
	/// <c>email_health</c>.</summary>
	public async Task<ImapHealthReport> GetHealthAsync(string mailbox, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		await client.Inbox.StatusAsync(StatusItems.Count | StatusItems.Unread, ct);

		var caps = ImapConnection.DescribeCapabilities(client);
		var info = new MailboxInfo
		{
			Mailbox = mailbox,
			DisplayName = mailbox,
			UnreadCount = client.Inbox.Unread,
			TotalCount = client.Inbox.Count,
		};
		return new ImapHealthReport
		{
			Mailbox = info,
			Host = _config.Host ?? "",
			Capabilities = caps,
			NativeMove = client.Capabilities.HasFlag(ImapCapabilities.Move),
			SpecialUse = client.Capabilities.HasFlag(ImapCapabilities.SpecialUse),
			QResyncDelta = client.Capabilities.HasFlag(ImapCapabilities.QuickResync),
			ServerSort = client.Capabilities.HasFlag(ImapCapabilities.Sort),
			GmailExtensions = client.Capabilities.HasFlag(ImapCapabilities.GMailExt1),
		};
	}

	// =====================================================================================
	//  P0: writes
	// =====================================================================================

	public Task<IReadOnlyList<BatchItemResult>> SetReadStateAsync(string mailbox, IReadOnlyList<string> messageIds, bool isRead, CancellationToken ct = default)
		=> ForEachMessageAsync(messageIds, FolderAccess.ReadWrite, async (folder, pid, c) =>
		{
			await folder.StoreAsync(
				pid.Uid,
				new StoreFlagsRequest(isRead ? StoreAction.Add : StoreAction.Remove, MessageFlags.Seen) { Silent = true },
				c);
			return new BatchItemResult { MessageId = pid.RawId, Success = true };
		}, ct);

	public async Task<IReadOnlyList<BatchItemResult>> MoveAsync(string mailbox, IReadOnlyList<string> messageIds, string destinationFolderId, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		var dest = await ResolveFolderAsync(client, destinationFolderId, ct)
			?? throw new InvalidOperationException($"Destination folder '{destinationFolderId}' could not be resolved.");
		return await MoveToResolvedAsync(client, messageIds, dest, ct);
	}

	// Soft-delete = move to the Trash special-use folder (NOT in-place \Deleted+EXPUNGE, which is a hard
	// delete). The recovery window is the server's Trash retention, not a guaranteed 30 days — documented.
	public async Task<IReadOnlyList<BatchItemResult>> DeleteAsync(string mailbox, IReadOnlyList<string> messageIds, CancellationToken ct = default)
	{
		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		var trash = await ResolveWellKnownAsync(client, "deleteditems", ct)
			?? throw new InvalidOperationException(
				"No Trash/Deleted-Items folder could be resolved, so there is no non-destructive delete path. "
				+ "(The MCP never hard-deletes.)");
		return await MoveToResolvedAsync(client, messageIds, trash, ct);
	}

	public Task<DraftResult> CreateReplyDraftAsync(string mailbox, string messageId, string? comment, bool replyAll = false, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Reply-draft creation is not supported on the IMAP backend yet. IMAP has no createReply action; the "
			+ "substitute (build a MIME reply with In-Reply-To/References headers and APPEND it to Drafts with the "
			+ "\\Draft flag) is a P2 item per the IMAP-Email-MCP-Scoping doc.");

	public Task<SendResult> SendDraftAsync(string mailbox, string draftId, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Sending an existing draft is not supported on the IMAP backend yet. There is no IMAP draft-creation "
			+ "path here (createReply is Graph-only), and sending an arbitrary stored message would mean fetching "
			+ "its MIME and SMTP-submitting it — a fast-follow if IMAP draft composition lands.");

	public async Task<SendResult> SendAsync(string mailbox, EmailDraft draft, CancellationToken ct = default)
	{
		if (draft.To is null || draft.To.Count == 0)
			return new SendResult { Success = false, Error = "At least one 'to' recipient is required." };

		MimeMessage message;
		try
		{
			message = new MimeMessage();
			string from = string.IsNullOrWhiteSpace(mailbox) ? (_config.DefaultMailbox ?? _config.Username ?? "") : mailbox;
			message.From.Add(MailboxAddress.Parse(from));
			AddAddresses(message.To, draft.To);
			AddAddresses(message.Cc, draft.Cc);
			AddAddresses(message.Bcc, draft.Bcc);
			message.Subject = draft.Subject ?? string.Empty;
			var body = new BodyBuilder();
			if (draft.BodyFormat == EmailBodyFormat.Html)
				body.HtmlBody = draft.Body;
			else
				body.TextBody = draft.Body;
			message.Body = body.ToMessageBody();
			message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
		}
		catch (Exception ex)
		{
			return new SendResult { Success = false, Error = $"Could not compose the message: {ex.Message}" };
		}

		try
		{
			using (var smtp = await ImapConnection.OpenSmtpAsync(_config, ct))
			{
				await smtp.SendAsync(message, ct);
				await smtp.DisconnectAsync(true, ct);
			}
		}
		catch (Exception ex)
		{
			return new SendResult { Success = false, Error = ex.Message };
		}

		// Best-effort Sent-folder append (SMTP doesn't file a copy the way Graph's SaveToSentItems does).
		if (draft.SaveToSentItems)
		{
			try
			{
				using var client = await ImapConnection.OpenImapAsync(_config, ct);
				var sent = await ResolveWellKnownAsync(client, "sentitems", ct);
				if (sent is not null)
				{
					await sent.OpenAsync(FolderAccess.ReadWrite, ct);
					await sent.AppendAsync(new AppendRequest(message, MessageFlags.Seen), ct);
				}
				await client.DisconnectAsync(true, ct);
			}
			catch (Exception) when (!ct.IsCancellationRequested)
			{
				// The send succeeded; a failed Sent-append is not a send failure. Swallow.
			}
		}

		return new SendResult { Success = true, MessageId = message.MessageId };
	}

	// =====================================================================================
	//  P1/P2: no native IMAP equivalent — NotSupported (never faked). See the scoping doc §4.
	// =====================================================================================

	public Task<IReadOnlyList<EmailMessage>> SearchAsync(string mailbox, string search, string? folder = null, int maxResults = 25, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Relevance-ranked search is not exposed on the IMAP backend yet. IMAP SEARCH (TEXT/BODY) exists but "
			+ "is unranked and per-folder; wiring it to a mailbox-wide email_read_search is a fast-follow, tracked "
			+ "separately from the O365 $search work (#518).");

	public Task<FacetResult> GetFacetsAsync(string mailbox, EmailQuery query, string? groupBy, int maxScan = 5000, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Faceted counting is not exposed on the IMAP backend yet. IMAP SEARCH can return matching UIDs "
			+ "cheaply (a count is a UID-set size), but grouping (senderDomain/year/...) needs envelope fetches; "
			+ "wiring it is a fast-follow, tracked separately from the O365 facet work (#585-range).");

	public Task<IReadOnlyList<EmailMessage>> GetByConversationAsync(string mailbox, IReadOnlyList<string> conversationIds, int maxResults = 50, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Conversation lookup is not supported on the IMAP backend. IMAP has no native cross-folder conversation "
			+ "id; the substitutes (Gmail X-GM-THRID, the THREAD extension, or References/In-Reply-To header walking) "
			+ "are a P2 item per the IMAP-Email-MCP-Scoping doc.");

	public Task<IReadOnlyList<BatchItemResult>> CategorizeAsync(string mailbox, IReadOnlyList<string> messageIds, IReadOnlyList<string> categories, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Categorize is not supported on the IMAP backend. IMAP has no equivalent to Outlook categories; the "
			+ "substitute (IMAP keywords where PERMANENTFLAGS allows, Gmail X-GM-LABELS, else an agent-side sidecar) "
			+ "is a P1 item per the IMAP-Email-MCP-Scoping doc.");

	public Task<IReadOnlyList<BatchItemResult>> SetExtendedPropertyAsync(string mailbox, IReadOnlyList<string> messageIds, string propertySetGuid, string name, string value, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Extended-property writes are not supported on the IMAP backend. IMAP cannot attach arbitrary structured "
			+ "payload to a message; the substitute is an agent-side sidecar store keyed by Message-ID (a P2 item per "
			+ "the IMAP-Email-MCP-Scoping doc).");

	public Task<DeltaResult> GetDeltaAsync(string mailbox, string folder, string? deltaToken, int maxPageSize = 50, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Delta sync is not supported on the IMAP backend yet. IMAP has no Graph-style delta token; the substitutes "
			+ "(a UID-watermark additions-only delta, or CONDSTORE/QRESYNC for changes+removals where advertised) are a "
			+ "P1 item per the IMAP-Email-MCP-Scoping doc.");

	public Task<IReadOnlyList<InboxRuleInfo>> GetInboxRulesAsync(string mailbox, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Server-side inbox rules are not supported on the IMAP backend. IMAP has no server-side rule concept; "
			+ "the substitute (SIEVE via the ManageSieve protocol, a separate connection) is a P2 item per the "
			+ "IMAP-Email-MCP-Scoping doc. Client-side rule evaluation belongs in the agent, not the MCP.");

	public Task<InboxRuleInfo> CreateInboxRuleAsync(string mailbox, InboxRuleDraft rule, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Server-side inbox rules are not supported on the IMAP backend. IMAP has no server-side rule concept; "
			+ "the substitute (SIEVE via the ManageSieve protocol, a separate connection) is a P2 item per the "
			+ "IMAP-Email-MCP-Scoping doc. Client-side rule evaluation belongs in the agent, not the MCP.");

	public Task<InboxRuleInfo> UpdateInboxRuleAsync(string mailbox, string ruleId, bool? isEnabled = null, string? displayName = null, int? sequence = null,
		InboxRuleConditions? conditions = null, InboxRuleConditions? exceptions = null, InboxRuleActions? actions = null, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Server-side inbox rules are not supported on the IMAP backend (see CreateInboxRuleAsync). SIEVE via "
			+ "ManageSieve is the P2 substitute.");

	public Task<InboxRuleInfo?> DeleteInboxRuleAsync(string mailbox, string ruleId, CancellationToken ct = default)
		=> throw new NotSupportedException(
			"Server-side inbox rules are not supported on the IMAP backend (see CreateInboxRuleAsync). SIEVE via "
			+ "ManageSieve is the P2 substitute.");

	// =====================================================================================
	//  Internals
	// =====================================================================================

	private readonly record struct Hit(IMailFolder Folder, IMessageSummary Summary, DateTimeOffset Received);

	private readonly record struct ParsedId(string Folder, uint Validity, UniqueId Uid, string RawId);

	// Determine which folders a query runs against: a single resolved folder, or the whole mailbox
	// (all selectable folders, optionally minus Junk/Trash when the filter asks and no folder is scoped).
	private async Task<List<IMailFolder>> ResolveScopeAsync(ImapClient client, EmailQuery query, CancellationToken ct)
	{
		if (!string.IsNullOrWhiteSpace(query.Folder))
		{
			var folder = await ResolveFolderAsync(client, query.Folder!, ct)
				?? throw new InvalidOperationException($"Folder '{query.Folder}' could not be resolved.");
			return [folder];
		}

		var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (query.ExcludeDeletedAndJunk)
		{
			foreach (var wk in (string[])["deleteditems", "junkemail"])
			{
				var f = await ResolveWellKnownAsync(client, wk, ct);
				if (f is not null) exclude.Add(f.FullName);
			}
		}

		var all = new List<IMailFolder>();

		// INBOX is special-cased in IMAP and is NOT reliably a child of the personal-namespace root
		// (some servers root the namespace at "INBOX."), so collecting only the root's subfolders would
		// silently skip the mailbox's most important folder — an unscoped unread scan then returns 0 while
		// per-folder counts stay correct (Field-Findings §11). Seed the inbox explicitly, first.
		if (!exclude.Contains(client.Inbox.FullName))
			all.Add(client.Inbox);

		await CollectSelectableFoldersAsync(client.GetFolder(client.PersonalNamespaces[0]), all, ct);

		// Dedup by FullName (the root's subfolders MAY already include INBOX on some servers) while
		// preserving first-seen order, apply the Junk/Trash exclusion, and cap.
		return [.. all
			.Where(f => !exclude.Contains(f.FullName))
			.GroupBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
			.Select(g => g.First())
			.Take(MaxFolders)];
	}

	private static async Task CollectSelectableFoldersAsync(IMailFolder parent, List<IMailFolder> acc, CancellationToken ct)
	{
		if (acc.Count >= MaxFolders) return;
		IList<IMailFolder> children;
		try
		{
			children = await parent.GetSubfoldersAsync(subscribedOnly: false, ct);
		}
		catch (Exception) when (!ct.IsCancellationRequested)
		{
			return;
		}

		foreach (var child in children)
		{
			if (!child.Attributes.HasFlag(FolderAttributes.NoSelect) && !child.Attributes.HasFlag(FolderAttributes.NonExistent))
				acc.Add(child);
			if (child.Attributes.HasFlag(FolderAttributes.HasChildren))
				await CollectSelectableFoldersAsync(child, acc, ct);
		}
	}

	// Batched write scaffold: parse + group ids by folder (one SELECT per folder), run `op` per id,
	// one BatchItemResult each; a failure on one item never fails the set. Mirrors GraphEmailService.RunBatchAsync.
	private async Task<IReadOnlyList<BatchItemResult>> ForEachMessageAsync(
		IReadOnlyList<string> messageIds,
		FolderAccess access,
		Func<IMailFolder, ParsedId, CancellationToken, Task<BatchItemResult>> op,
		CancellationToken ct)
	{
		var results = new List<BatchItemResult>();
		var parsed = new List<ParsedId>();
		foreach (var id in messageIds)
		{
			if (TryDecode(id, out var pid))
				parsed.Add(pid);
			else
				results.Add(new BatchItemResult { MessageId = id, Success = false, Error = $"Malformed message id '{id}' (expected 'folder:UIDVALIDITY:UID')." });
		}

		if (parsed.Count == 0)
			return results;

		using var client = await ImapConnection.OpenImapAsync(_config, ct);
		foreach (var group in parsed.GroupBy(p => p.Folder, StringComparer.Ordinal))
		{
			IMailFolder? folder = await GetFolderByPathAsync(client, group.Key, ct);
			if (folder is null)
			{
				foreach (var p in group)
					results.Add(new BatchItemResult { MessageId = p.RawId, Success = false, Error = $"Folder '{group.Key}' not found." });
				continue;
			}

			await folder.OpenAsync(access, ct);
			foreach (var p in group)
			{
				try
				{
					if (folder.UidValidity != p.Validity)
					{
						results.Add(new BatchItemResult { MessageId = p.RawId, Success = false, Error = "UIDVALIDITY changed since the id was issued — re-list the folder." });
						continue;
					}
					results.Add(await op(folder, p, ct));
				}
				catch (Exception ex)
				{
					results.Add(new BatchItemResult { MessageId = p.RawId, Success = false, Error = ex.Message });
				}
			}
		}

		return results;
	}

	private async Task<IReadOnlyList<BatchItemResult>> MoveToResolvedAsync(ImapClient client, IReadOnlyList<string> messageIds, IMailFolder dest, CancellationToken ct)
	{
		var results = new List<BatchItemResult>();
		var parsed = new List<ParsedId>();
		foreach (var id in messageIds)
		{
			if (TryDecode(id, out var pid))
				parsed.Add(pid);
			else
				results.Add(new BatchItemResult { MessageId = id, Success = false, Error = $"Malformed message id '{id}' (expected 'folder:UIDVALIDITY:UID')." });
		}

		foreach (var group in parsed.GroupBy(p => p.Folder, StringComparer.Ordinal))
		{
			IMailFolder? folder = await GetFolderByPathAsync(client, group.Key, ct);
			if (folder is null)
			{
				foreach (var p in group)
					results.Add(new BatchItemResult { MessageId = p.RawId, Success = false, Error = $"Folder '{group.Key}' not found." });
				continue;
			}

			await folder.OpenAsync(FolderAccess.ReadWrite, ct);
			foreach (var p in group)
			{
				try
				{
					if (folder.UidValidity != p.Validity)
					{
						results.Add(new BatchItemResult { MessageId = p.RawId, Success = false, Error = "UIDVALIDITY changed since the id was issued — re-list the folder." });
						continue;
					}

					// MailKit uses the MOVE extension when advertised, else COPY + \Deleted + EXPUNGE. The UID
					// changes; with UIDPLUS/COPYUID the new id comes back directly, else NewId is null.
					UniqueId? newUid = await folder.MoveToAsync(p.Uid, dest, ct);
					string? newId = newUid is { } nu ? Encode(dest.FullName, nu.Validity != 0 ? nu.Validity : dest.UidValidity, nu.Id) : null;
					results.Add(new BatchItemResult
					{
						MessageId = p.RawId,
						Success = true,
						NewId = newId,
						FromFolderId = folder.FullName,
					});
				}
				catch (Exception ex)
				{
					results.Add(new BatchItemResult { MessageId = p.RawId, Success = false, Error = ex.Message });
				}
			}
		}

		return results;
	}

	// ---- Folder resolution ----

	// Resolve a well-known name (inbox/junkemail/…) or treat the value as a folder path/id.
	private async Task<IMailFolder?> ResolveFolderAsync(ImapClient client, string nameOrPath, CancellationToken ct)
	{
		string key = nameOrPath.Trim();
		if (string.Equals(key, "inbox", StringComparison.OrdinalIgnoreCase))
			return client.Inbox;
		if (WellKnownSpecial.ContainsKey(key))
			return await ResolveWellKnownAsync(client, key, ct);
		return await GetFolderByPathAsync(client, key, ct);
	}

	// SPECIAL-USE first (RFC 6154), then a name-guessing table across common aliases.
	private async Task<IMailFolder?> ResolveWellKnownAsync(ImapClient client, string wellKnown, CancellationToken ct)
	{
		if (string.Equals(wellKnown, "inbox", StringComparison.OrdinalIgnoreCase))
			return client.Inbox;

		if (WellKnownSpecial.TryGetValue(wellKnown, out var special))
		{
			try
			{
				var f = client.GetFolder(special);
				if (f is not null)
					return f;
			}
			catch (Exception) when (!ct.IsCancellationRequested)
			{
				// SPECIAL-USE/XLIST not advertised — fall through to name guessing.
			}
		}

		if (WellKnownAliases.TryGetValue(wellKnown, out var aliases))
		{
			var all = new List<IMailFolder>();
			await CollectSelectableFoldersAsync(client.GetFolder(client.PersonalNamespaces[0]), all, ct);
			foreach (var alias in aliases)
			{
				var match = all.FirstOrDefault(f => string.Equals(f.Name, alias, StringComparison.OrdinalIgnoreCase));
				if (match is not null)
					return match;
			}
		}

		return null;
	}

	private static async Task<IMailFolder?> GetFolderByPathAsync(ImapClient client, string path, CancellationToken ct)
	{
		if (string.Equals(path, "inbox", StringComparison.OrdinalIgnoreCase))
			return client.Inbox;
		try
		{
			return await client.GetFolderAsync(path, ct);
		}
		catch (FolderNotFoundException)
		{
			return null;
		}
	}

	// ---- Query translation ----

	private static SearchQuery BuildSearchQuery(EmailQuery query)
	{
		SearchQuery result = SearchQuery.All;
		bool empty = true;
		void Add(SearchQuery clause)
		{
			result = empty ? clause : result.And(clause);
			empty = false;
		}

		if (query.IsRead == true) Add(SearchQuery.Seen);
		else if (query.IsRead == false) Add(SearchQuery.NotSeen);

		if (query.IsFlagged == true) Add(SearchQuery.Flagged);
		else if (query.IsFlagged == false) Add(SearchQuery.NotFlagged);

		foreach (var p in query.Predicates)
		{
			string v = p.Value?.Trim() ?? "";
			if (v.Length == 0) continue;
			switch (p.Field)
			{
				case EmailField.From:
					// Domain match is an approximation (FROM "@domain") — IMAP has no address-part search.
					Add(SearchQuery.FromContains(p.Op == EmailOperator.Domain && !v.StartsWith('@') ? "@" + v : v));
					break;
				case EmailField.Subject:
					Add(SearchQuery.SubjectContains(v));
					break;
				case EmailField.Body:
					Add(SearchQuery.BodyContains(v)); // server-side full-text — a genuine IMAP strength.
					break;
				case EmailField.To:
					Add(SearchQuery.ToContains(v));
					break;
				case EmailField.Recipient:
					// To OR Cc — Graph's recipientContains semantics.
					Add(SearchQuery.ToContains(v).Or(SearchQuery.CcContains(v)));
					break;
			}
		}

		// IMAP SEARCH dates are date-granular (INTERNALDATE); widen by a day and refine client-side.
		if (query.Received?.After is { } after)
			Add(SearchQuery.DeliveredAfter(after.UtcDateTime.Date));
		if (query.Received?.Before is { } before)
			Add(SearchQuery.DeliveredBefore(before.UtcDateTime.Date.AddDays(1)));

		return result;
	}

	private static EmailQuery ToQuery(EmailFilter? f, int maxResults, int skip)
	{
		var predicates = new List<EmailPredicate>();
		if (!string.IsNullOrWhiteSpace(f?.FromContains))
			predicates.Add(new EmailPredicate(EmailField.From, EmailOperator.Contains, f!.FromContains!));
		if (!string.IsNullOrWhiteSpace(f?.SubjectContains))
			predicates.Add(new EmailPredicate(EmailField.Subject, EmailOperator.Contains, f!.SubjectContains!));

		return new EmailQuery
		{
			Received = f?.ReceivedAfter is not null || f?.ReceivedBefore is not null
				? new DateRange { After = f?.ReceivedAfter, Before = f?.ReceivedBefore }
				: null,
			ExcludeDeletedAndJunk = f?.ExcludeDeletedAndJunk ?? false,
			IncludeCc = f?.IncludeCc ?? false,
			IncludeBody = f?.IncludeBody ?? false,
			BodyFormat = f?.BodyFormat ?? EmailBodyFormat.Text,
			Predicates = predicates,
			Top = maxResults,
			Skip = skip,
		};
	}

	// ---- Projection ----

	private static EmailMessage Map(IMailFolder folder, IMessageSummary s, bool includeCc, string preview)
	{
		var env = s.Envelope;
		var from = env?.From?.Mailboxes?.FirstOrDefault();
		var flags = s.Flags ?? MessageFlags.None;

		return new EmailMessage
		{
			Id = Encode(folder.FullName, folder.UidValidity, s.UniqueId.Id),
			Subject = string.IsNullOrEmpty(env?.Subject) ? "(no subject)" : env!.Subject,
			FromAddress = from?.Address ?? "",
			FromName = from?.Name ?? "",
			ReceivedDateTime = ReceivedOf(s),
			BodyPreview = preview,
			IsRead = flags.HasFlag(MessageFlags.Seen),
			IsFlagged = flags.HasFlag(MessageFlags.Flagged),
			HasAttachments = s.Attachments?.Any() ?? false,
			ToRecipients = env?.To?.Mailboxes?.Select(a => a.Address).ToList() ?? [],
			CcRecipients = includeCc ? env?.Cc?.Mailboxes?.Select(a => a.Address).ToList() ?? [] : [],
			InternetMessageId = NormalizeMessageId(env?.MessageId),
			ParentFolderId = folder.FullName,
			// ConversationId + Categories have no P0 IMAP source (P1/P2 substitutes) — left null/empty.
		};
	}

	private static DateTimeOffset ReceivedOf(IMessageSummary s)
		=> s.InternalDate ?? s.Envelope?.Date ?? s.Date;

	private async Task<string> SynthesizePreviewAsync(IMailFolder folder, IMessageSummary s, CancellationToken ct)
	{
		// IMAP has no preview field: pull the text (or html) part and normalise to ~250 chars. This costs
		// one part-fetch per row — a documented cost vs Graph returning bodyPreview for free.
		var part = s.TextBody ?? s.HtmlBody;
		if (part is null)
			return "";
		try
		{
			var entity = await folder.GetBodyPartAsync(s.UniqueId, part, ct);
			if (entity is not TextPart tp)
				return "";
			string text = ReferenceEquals(part, s.HtmlBody) ? StripHtml(tp.Text) : tp.Text;
			return Normalize(text, 250);
		}
		catch (Exception) when (!ct.IsCancellationRequested)
		{
			return "";
		}
	}

	// ---- id codec: folder:UIDVALIDITY:UID (folder path may contain ':', so split from the right) ----

	private static string Encode(string folder, uint validity, uint uid) => $"{folder}:{validity}:{uid}";

	private static bool TryDecode(string id, out ParsedId parsed)
	{
		parsed = default;
		if (string.IsNullOrWhiteSpace(id)) return false;
		int last = id.LastIndexOf(':');
		if (last <= 0) return false;
		int prev = id.LastIndexOf(':', last - 1);
		if (prev <= 0) return false;

		string folder = id[..prev];
		if (!uint.TryParse(id.AsSpan(prev + 1, last - prev - 1), out uint validity)) return false;
		if (!uint.TryParse(id.AsSpan(last + 1), out uint uid)) return false;

		parsed = new ParsedId(folder, validity, new UniqueId(validity, uid), id);
		return true;
	}

	// ---- text helpers ----

	private static void AddAddresses(InternetAddressList list, IReadOnlyList<string>? addresses)
	{
		if (addresses is null) return;
		foreach (var a in addresses)
			if (!string.IsNullOrWhiteSpace(a))
				list.Add(MailboxAddress.Parse(a.Trim()));
	}

	private static string? NormalizeMessageId(string? messageId)
	{
		if (string.IsNullOrWhiteSpace(messageId)) return null;
		string m = messageId.Trim();
		// MailKit's Envelope.MessageId is bare; present it with angle brackets like Graph's internetMessageId.
		return m.StartsWith('<') ? m : $"<{m}>";
	}

	private static string StripHtml(string? html)
	{
		if (string.IsNullOrEmpty(html)) return "";
		string noTags = Regex.Replace(html, "<[^>]+>", " ");
		return WebUtility.HtmlDecode(noTags);
	}

	private static string Normalize(string? text, int max)
	{
		if (string.IsNullOrEmpty(text)) return "";
		string collapsed = Regex.Replace(text, @"\s+", " ").Trim();
		return collapsed.Length <= max ? collapsed : collapsed[..max];
	}
}

/// <summary>Health snapshot for the IMAP backend — INBOX counts plus the negotiated capabilities/
/// substitutes the agent needs. Not part of the shared contract; consumed only by <c>email_health</c>.</summary>
public sealed record ImapHealthReport
{
	public required MailboxInfo Mailbox { get; init; }
	public required string Host { get; init; }
	public required IReadOnlyList<string> Capabilities { get; init; }
	public bool NativeMove { get; init; }
	public bool SpecialUse { get; init; }
	public bool QResyncDelta { get; init; }
	public bool ServerSort { get; init; }
	public bool GmailExtensions { get; init; }
}
