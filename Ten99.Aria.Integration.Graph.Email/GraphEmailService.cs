using System.Collections.Concurrent;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>
/// IEmailService implementation using Microsoft Graph SDK.
/// Requires a pre-authenticated GraphServiceClient (provided via DI from GraphClientFactory).
/// Provider-neutral <see cref="EmailQuery"/> is translated to OData by <see cref="GraphQueryTranslator"/>.
/// </summary>
public class GraphEmailService(GraphServiceClient graphClient) : IEmailService
{
	public async Task<IReadOnlyList<EmailMessage>> GetMessagesAsync(
		string mailbox,
		EmailQuery query,
		CancellationToken ct = default)
	{
		var (filter, clientSide) = GraphQueryTranslator.Translate(query);

		// Mailbox-wide (no folder) can sweep in Junk/Deleted; exclude them when asked. Moot when a single
		// folder is targeted — you've already chosen the scope. Skipped for flagged queries: Exchange
		// rejects `flag/flagStatus` combined with `parentFolderId ne` + orderby as "too complex", and
		// flagged mail almost never sits in Junk/Deleted, so the exclusion isn't worth the broken query.
		if (query.ExcludeDeletedAndJunk && string.IsNullOrEmpty(query.Folder) && query.IsFlagged is not true)
			filter = AndClause(filter, await NoiseFolderExclusionAsync(mailbox, ct));

		// Exchange rejects `$orderby` alongside some filters as "restriction or sort order too complex":
		// flag/flagStatus, contains(...) text search, and a conversationId filter (unless an equality like
		// isRead happens to re-plan the query). For those, omit server-side ordering and sort the (small)
		// result set in memory. These result sets are inherently small (a thread; a text-search hit list).
		bool hasTextSearch = query.Predicates.Any(p =>
			(p.Op is EmailOperator.Contains or EmailOperator.StartsWith or EmailOperator.Domain)
			&& p.Field is EmailField.Subject or EmailField.From);
		bool sortClientSide = query.IsFlagged is true || hasTextSearch || query.ConversationIds.Count > 0;
		string[]? orderby = sortClientSide ? null
			: query.Sort == EmailSort.ReceivedAscending ? ["receivedDateTime asc"] : ["receivedDateTime desc"];

		// Opt-in: widen the projection with the full body when asked (off by default — a big HTML body per
		// row overflows context). The Prefer header renders it server-side in the requested format.
		var extraFields = new List<string>();
		// Cc is needed either when the caller asked for it, or to match a Recipient (To-or-Cc) predicate client-side.
		if (query.IncludeCc || query.Predicates.Any(p => p.Field == EmailField.Recipient)) extraFields.Add("ccRecipients");
		if (query.IncludeBody) extraFields.Add("body");
		string[] selectFields = extraFields.Count > 0 ? [.. MessageSelectFields, .. extraFields] : MessageSelectFields;
		string bodyContentType = query.BodyFormat == EmailBodyFormat.Html ? "html" : "text";

		// Users[].Messages and MailFolders[].Messages are distinct generated builders, so the call branches.
		async Task<List<Message>?> FetchAsync(int top, int skip)
		{
			if (string.IsNullOrEmpty(query.Folder))
			{
				var res = await graphClient.Users[mailbox].Messages.GetAsync(config =>
				{
					if (!string.IsNullOrEmpty(filter)) config.QueryParameters.Filter = filter;
					config.QueryParameters.Top = top;
					if (skip > 0) config.QueryParameters.Skip = skip;
					if (orderby is not null) config.QueryParameters.Orderby = orderby;
					config.QueryParameters.Select = selectFields;
					if (query.IncludeBody) config.Headers.Add("Prefer", $"outlook.body-content-type=\"{bodyContentType}\"");
				}, ct);
				return res?.Value;
			}
			var fres = await graphClient.Users[mailbox].MailFolders[query.Folder].Messages.GetAsync(config =>
			{
				if (!string.IsNullOrEmpty(filter)) config.QueryParameters.Filter = filter;
				config.QueryParameters.Top = top;
				if (skip > 0) config.QueryParameters.Skip = skip;
				if (orderby is not null) config.QueryParameters.Orderby = orderby;
				config.QueryParameters.Select = selectFields;
				if (query.IncludeBody) config.Headers.Add("Prefer", $"outlook.body-content-type=\"{bodyContentType}\"");
			}, ct);
			return fres?.Value;
		}

		List<EmailMessage> mapped;
		if (clientSide.Count == 0)
		{
			// Server does everything — one page, already windowed by Skip/Top.
			mapped = [.. MapMessages(await FetchAsync(query.Top, query.Skip))];
		}
		else
		{
			// §22: client-side predicates (To/Recipient/Body, unsupported ops) are applied AFTER the fetch, so a
			// single server page yields only the matches that happen to fall in it — a filtered first page posing
			// as the whole result. Page the server results (bounded) from the start and client-filter, so the
			// filter reaches the whole mailbox. Server Skip no longer maps to matched Skip once a filter drops
			// rows, so window at the end.
			const int PageSize = 500;
			int scanCap = query.MaxClientScan > 0 ? query.MaxClientScan : 3000;
			int need = query.Skip + query.Top;
			mapped = [];
			int serverSkip = 0;
			while (mapped.Count < need && serverSkip < scanCap)
			{
				int top = Math.Min(PageSize, scanCap - serverSkip);
				var pageValue = await FetchAsync(top, serverSkip);
				int got = pageValue?.Count ?? 0;
				if (got == 0)
					break;
				foreach (var m in MapMessages(pageValue))
					if (clientSide.All(p => GraphQueryTranslator.MatchesClientSide(m, p)))
						mapped.Add(m);
				if (got < top)
					break; // server exhausted
				serverSkip += got;
			}
		}

		if (sortClientSide)
			mapped = query.Sort == EmailSort.ReceivedAscending
				? [.. mapped.OrderBy(m => m.ReceivedDateTime)]
				: [.. mapped.OrderByDescending(m => m.ReceivedDateTime)];

		// The client-side-paged path accumulated from server offset 0, so apply the requested window now.
		if (clientSide.Count > 0)
			mapped = [.. mapped.Skip(query.Skip).Take(query.Top)];

		return mapped;
	}

	public async Task<FacetResult> GetFacetsAsync(string mailbox, EmailQuery query, string? groupBy, int maxScan = 5000, CancellationToken ct = default)
	{
		var (filter, clientSide) = GraphQueryTranslator.Translate(query);
		if (query.ExcludeDeletedAndJunk && string.IsNullOrEmpty(query.Folder) && query.IsFlagged is not true)
			filter = AndClause(filter, await NoiseFolderExclusionAsync(mailbox, ct));

		Func<EmailMessage, string>? keyOf = FacetKey(groupBy); // throws on an unknown groupBy (rejected, not ignored)
		// Lean projection; widen only for the recipient predicates that need To/Cc to match client-side.
		var selectList = new List<string> { "id", "from", "receivedDateTime", "isRead", "parentFolderId" };
		if (clientSide.Any(p => p.Field is EmailField.To or EmailField.Recipient)) selectList.Add("toRecipients");
		if (clientSide.Any(p => p.Field == EmailField.Recipient)) selectList.Add("ccRecipients");
		string[] select = [.. selectList];
		const int pageSize = 500;

		int scanned = 0, matched = 0;
		bool truncated = false;
		var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		// Graph has no $apply/aggregate on messages, so page the matched set with a lean $select and count in
		// memory — only the counts leave the MCP. No $orderby (unneeded, and it trips Exchange's "too complex").
		int skip = 0;
		while (scanned < maxScan)
		{
			int top = Math.Min(pageSize, maxScan - scanned);
			List<Message>? value = string.IsNullOrEmpty(query.Folder)
				? (await graphClient.Users[mailbox].Messages.GetAsync(c =>
					{ if (!string.IsNullOrEmpty(filter)) c.QueryParameters.Filter = filter; c.QueryParameters.Top = top; c.QueryParameters.Skip = skip; c.QueryParameters.Select = select; }, ct))?.Value
				: (await graphClient.Users[mailbox].MailFolders[query.Folder].Messages.GetAsync(c =>
					{ if (!string.IsNullOrEmpty(filter)) c.QueryParameters.Filter = filter; c.QueryParameters.Top = top; c.QueryParameters.Skip = skip; c.QueryParameters.Select = select; }, ct))?.Value;

			int got = value?.Count ?? 0;
			if (got == 0)
				break;
			scanned += got;

			foreach (var msg in MapMessages(value))
			{
				if (clientSide.Count > 0 && !clientSide.All(p => GraphQueryTranslator.MatchesClientSide(msg, p)))
					continue;
				matched++;
				if (keyOf is not null)
				{
					string k = keyOf(msg);
					counts[k] = counts.GetValueOrDefault(k) + 1;
				}
			}

			if (got < top)
				break; // server exhausted
			skip += got;
			if (scanned >= maxScan)
				truncated = true;
		}

		return new FacetResult
		{
			Matched = matched,
			Scanned = scanned,
			Truncated = truncated,
			Facets = keyOf is null ? null
				: [.. counts.OrderByDescending(kv => kv.Value).Select(kv => new FacetBucket { Key = kv.Key, Count = kv.Value })],
		};
	}

	// Grouping-key selectors over the lean fields fetched above. Unknown groupBy throws (rejected, per §1).
	private static Func<EmailMessage, string>? FacetKey(string? groupBy) => (groupBy?.Trim().ToLowerInvariant()) switch
	{
		null or "" => null,
		"senderdomain" => m => DomainOf(m.FromAddress),
		"senderaddress" => m => string.IsNullOrEmpty(m.FromAddress) ? "(none)" : m.FromAddress.ToLowerInvariant(),
		"year" => m => m.ReceivedDateTime.Year.ToString(),
		"month" => m => m.ReceivedDateTime.ToString("yyyy-MM"),
		"folder" => m => m.ParentFolderId ?? "(none)",
		"isread" => m => m.IsRead ? "read" : "unread",
		_ => throw new ArgumentException($"Unknown groupBy '{groupBy}'. Use senderDomain, senderAddress, year, month, folder, isRead."),
	};

	private static string DomainOf(string? addr)
	{
		int at = addr?.LastIndexOf('@') ?? -1;
		return at >= 0 ? addr![(at + 1)..].ToLowerInvariant() : "(none)";
	}

	public Task<IReadOnlyList<EmailMessage>> GetByConversationAsync(
		string mailbox,
		IReadOnlyList<string> conversationIds,
		int maxResults = 50,
		CancellationToken ct = default)
		=> GetMessagesAsync(mailbox, new EmailQuery
		{
			ConversationIds = conversationIds,
			Top = maxResults,
		}, ct);

	public async Task<IReadOnlyList<EmailMessage>> SearchAsync(string mailbox, string search, string? folder = null, int maxResults = 25, CancellationToken ct = default)
	{
		// Graph $search is full-text across from/subject/body/recipients, ranked by relevance. It cannot
		// combine with $filter or $orderby, so this is a dedicated path rather than part of GetMessagesAsync.
		string quoted = "\"" + search.Replace("\"", "\\\"") + "\"";
		List<Message>? value;
		if (string.IsNullOrEmpty(folder))
		{
			var res = await graphClient.Users[mailbox].Messages.GetAsync(c =>
			{
				c.QueryParameters.Search = quoted;
				c.QueryParameters.Top = maxResults;
				c.QueryParameters.Select = MessageSelectFields;
			}, ct);
			value = res?.Value;
		}
		else
		{
			var res = await graphClient.Users[mailbox].MailFolders[folder].Messages.GetAsync(c =>
			{
				c.QueryParameters.Search = quoted;
				c.QueryParameters.Top = maxResults;
				c.QueryParameters.Select = MessageSelectFields;
			}, ct);
			value = res?.Value;
		}
		return MapMessages(value);
	}

	public Task<IReadOnlyList<EmailMessage>> GetUnreadAsync(
		string mailbox,
		EmailFilter? filter = null,
		int maxResults = 50,
		int skip = 0,
		CancellationToken ct = default)
		=> GetMessagesAsync(mailbox, ToQuery(filter, maxResults, skip), ct);

	// Flagged is just the general query with IsFlagged=true, regardless of read state — sugar over
	// GetMessagesAsync so it inherits folder scoping, the date window, and category filters.
	public Task<IReadOnlyList<EmailMessage>> GetFlaggedAsync(
		string mailbox,
		EmailFilter? filter = null,
		int maxResults = 50,
		int skip = 0,
		CancellationToken ct = default)
		=> GetMessagesAsync(mailbox, ToQuery(filter, maxResults, skip) with { IsRead = null, IsFlagged = true }, ct);

	// Deleted Items + Junk Email are folders too, so a mailbox-wide query sweeps unread trash/spam into
	// triage. Resolve the two well-known folders' ids (stable per mailbox → cached) and exclude them by
	// parentFolderId. Returns "" when neither resolves (nothing to exclude).
	private static readonly ConcurrentDictionary<string, (string? Deleted, string? Junk)> _noiseFolders = new();

	private async Task<string> NoiseFolderExclusionAsync(string mailbox, CancellationToken ct)
	{
		if (!_noiseFolders.TryGetValue(mailbox, out var ids))
		{
			string? deleted = await ResolveFolderIdAsync(mailbox, "deleteditems", ct);
			string? junk = await ResolveFolderIdAsync(mailbox, "junkemail", ct);
			ids = (deleted, junk);
			if (deleted is not null || junk is not null)
				_noiseFolders[mailbox] = ids;
		}

		var parts = new List<string>();
		if (ids.Deleted is not null) parts.Add($"parentFolderId ne '{ids.Deleted}'");
		if (ids.Junk is not null) parts.Add($"parentFolderId ne '{ids.Junk}'");
		return string.Join(" and ", parts);
	}

	private async Task<string?> ResolveFolderIdAsync(string mailbox, string wellKnownName, CancellationToken ct)
	{
		try
		{
			var folder = await graphClient.Users[mailbox].MailFolders[wellKnownName]
				.GetAsync(config => config.QueryParameters.Select = ["id"], ct);
			return folder?.Id;
		}
		catch (Exception) when (!ct.IsCancellationRequested)
		{
			return null; // Folder missing/inaccessible — just don't exclude it.
		}
	}

	// Combine two OData clauses with AND, tolerating either being empty.
	private static string AndClause(string a, string b)
		=> string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : $"{a} and {b}";

	public async Task<EmailMessage?> GetMessageAsync(
		string mailbox,
		string messageId,
		EmailBodyFormat bodyFormat = EmailBodyFormat.Text,
		string? propertySetGuid = null,
		string? propertyName = null,
		CancellationToken ct = default)
	{
		// Prefer header asks Graph to render the body in the requested format (text strips HTML markup).
		string bodyType = bodyFormat == EmailBodyFormat.Html ? "html" : "text";

		// Optionally expand ONE caller-named extended property (the agent's ledger) so a single read is
		// self-contained. The MCP holds no vocabulary — the caller supplies the set GUID + name.
		bool wantProperty = !string.IsNullOrWhiteSpace(propertySetGuid) && !string.IsNullOrWhiteSpace(propertyName);
		string? expand = wantProperty
			? $"singleValueExtendedProperties($filter=id eq 'String {{{propertySetGuid}}} Name {propertyName}')"
			: null;

		var message = await graphClient.Users[mailbox].Messages[messageId]
			.GetAsync(config =>
			{
				config.QueryParameters.Select = MessageDetailSelectFields;
				if (expand is not null)
					config.QueryParameters.Expand = [expand];
				config.Headers.Add("Prefer", $"outlook.body-content-type=\"{bodyType}\"");
			}, ct);

		if (message is null)
			return null;

		var mapped = MapMessages([message]).FirstOrDefault();
		if (mapped is not null && wantProperty && message.SingleValueExtendedProperties is { Count: > 0 })
			mapped = mapped with { PropertyValue = message.SingleValueExtendedProperties[0].Value };
		return mapped;
	}

	public Task<IReadOnlyList<BatchItemResult>> SetExtendedPropertyAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		string propertySetGuid,
		string name,
		string value,
		CancellationToken ct = default)
		=> RunBatchAsync(messageIds, async (id, c) =>
		{
			var update = new Message
			{
				SingleValueExtendedProperties =
				[
					new SingleValueLegacyExtendedProperty
					{
						// "String {guid} Name {name}" addresses a named string extended property (MAPI named
						// property). GUID + name + payload are the caller's — the MCP holds no vocabulary of its own.
						Id = $"String {{{propertySetGuid}}} Name {name}",
						Value = value,
					}
				]
			};
			await graphClient.Users[mailbox].Messages[id].PatchAsync(update, cancellationToken: c);
			return new BatchItemResult { MessageId = id, Success = true };
		}, ct);

	public Task<IReadOnlyList<BatchItemResult>> CategorizeAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		IReadOnlyList<string> categories,
		CancellationToken ct = default)
		=> RunBatchAsync(messageIds, async (id, c) =>
		{
			// Merge, don't overwrite — never clobber the human's own categories.
			var current = await graphClient.Users[mailbox].Messages[id]
				.GetAsync(config => config.QueryParameters.Select = ["categories"], c);
			var merged = new HashSet<string>(current?.Categories ?? []);
			foreach (var cat in categories)
				merged.Add(cat);
			await graphClient.Users[mailbox].Messages[id]
				.PatchAsync(new Message { Categories = merged.ToList() }, cancellationToken: c);
			return new BatchItemResult { MessageId = id, Success = true };
		}, ct);

	public Task<IReadOnlyList<BatchItemResult>> SetReadStateAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		bool isRead,
		CancellationToken ct = default)
		=> RunBatchAsync(messageIds, async (id, c) =>
		{
			await graphClient.Users[mailbox].Messages[id]
				.PatchAsync(new Message { IsRead = isRead }, cancellationToken: c);
			return new BatchItemResult { MessageId = id, Success = true };
		}, ct);

	public Task<IReadOnlyList<BatchItemResult>> MoveAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		string destinationFolderId,
		CancellationToken ct = default)
		=> RunBatchAsync(messageIds, async (id, c) =>
		{
			// Record the source folder first (the move response only knows the destination) — the other
			// half of the undo record. Graph reassigns the id on move, so return the new one.
			var before = await graphClient.Users[mailbox].Messages[id]
				.GetAsync(config => config.QueryParameters.Select = ["parentFolderId"], c);
			var moved = await graphClient.Users[mailbox].Messages[id].Move
				.PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.Move.MovePostRequestBody { DestinationId = destinationFolderId }, cancellationToken: c);
			return new BatchItemResult
			{
				MessageId = id,
				Success = true,
				NewId = moved?.Id,
				FromFolderId = before?.ParentFolderId,
			};
		}, ct);

	// Soft delete = move to Deleted Items (30-day recovery). There is deliberately NO hard-delete path.
	public Task<IReadOnlyList<BatchItemResult>> DeleteAsync(
		string mailbox,
		IReadOnlyList<string> messageIds,
		CancellationToken ct = default)
		=> MoveAsync(mailbox, messageIds, "deleteditems", ct);

	public async Task<DraftResult> CreateReplyDraftAsync(string mailbox, string messageId, string? comment, bool replyAll = false, CancellationToken ct = default)
	{
		// createReply / createReplyAll build a draft in Drafts with the original quoted and `comment` inserted
		// above it — no send. Returns the created draft message.
		Message? draft = replyAll
			? await graphClient.Users[mailbox].Messages[messageId].CreateReplyAll
				.PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.CreateReplyAll.CreateReplyAllPostRequestBody { Comment = comment }, cancellationToken: ct)
			: await graphClient.Users[mailbox].Messages[messageId].CreateReply
				.PostAsync(new Microsoft.Graph.Users.Item.Messages.Item.CreateReply.CreateReplyPostRequestBody { Comment = comment }, cancellationToken: ct);

		if (draft?.Id is null)
			throw new InvalidOperationException("The reply draft was not created (no draft id returned).");

		return new DraftResult
		{
			Id = draft.Id,
			Subject = draft.Subject ?? "(no subject)",
			ParentFolderId = draft.ParentFolderId,
			WebLink = draft.WebLink,
		};
	}

	public async Task<SendResult> SendDraftAsync(string mailbox, string draftId, CancellationToken ct = default)
	{
		try
		{
			// Sends the existing draft as-is (202, no body); a sent draft moves to Sent Items automatically.
			await graphClient.Users[mailbox].Messages[draftId].Send.PostAsync(cancellationToken: ct);
			return new SendResult { Success = true };
		}
		catch (ODataError ex)
		{
			return new SendResult { Success = false, Error = ex.Error?.Message ?? ex.Message };
		}
	}

	public async Task<SendResult> SendAsync(
		string mailbox,
		EmailDraft draft,
		CancellationToken ct = default)
	{
		if (draft.To is null || draft.To.Count == 0)
			return new SendResult { Success = false, Error = "At least one 'to' recipient is required." };

		var message = new Message
		{
			Subject = draft.Subject,
			Body = new ItemBody
			{
				ContentType = draft.BodyFormat == EmailBodyFormat.Html ? BodyType.Html : BodyType.Text,
				Content = draft.Body,
			},
			ToRecipients = ToRecipients(draft.To),
			CcRecipients = ToRecipients(draft.Cc),
			BccRecipients = ToRecipients(draft.Bcc),
		};

		try
		{
			// sendMail is fire-and-forget (HTTP 202) — Graph does not return the created message, so there
			// is no Message-ID to surface here. SaveToSentItems keeps the human's Sent-Items record.
			await graphClient.Users[mailbox].SendMail
				.PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
				{
					Message = message,
					SaveToSentItems = draft.SaveToSentItems,
				}, cancellationToken: ct);
			return new SendResult { Success = true };
		}
		catch (ODataError ex)
		{
			return new SendResult { Success = false, Error = ex.Error?.Message ?? ex.Message };
		}
	}

	// Map addresses to Graph recipients; null/empty → null so the SDK omits the field entirely.
	private static List<Recipient>? ToRecipients(IReadOnlyList<string>? addresses)
	{
		if (addresses is null || addresses.Count == 0)
			return null;
		return addresses
			.Where(a => !string.IsNullOrWhiteSpace(a))
			.Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a.Trim() } })
			.ToList();
	}

	// Run a per-message write as a batch: bounded concurrency, one BatchItemResult per id; a failure on
	// one item never fails the whole set. (Graph auto-retries 429s.)
	private static async Task<IReadOnlyList<BatchItemResult>> RunBatchAsync(
		IReadOnlyList<string> messageIds,
		Func<string, CancellationToken, Task<BatchItemResult>> op,
		CancellationToken ct)
	{
		using var gate = new SemaphoreSlim(4);
		var tasks = messageIds.Select(async id =>
		{
			await gate.WaitAsync(ct);
			try { return await op(id, ct); }
			catch (Exception ex) { return new BatchItemResult { MessageId = id, Success = false, Error = ex.Message }; }
			finally { gate.Release(); }
		});
		return await Task.WhenAll(tasks);
	}

	public async Task<DeltaResult> GetDeltaAsync(
		string mailbox,
		string folder,
		string? deltaToken,
		int maxPageSize = 50,
		CancellationToken ct = default)
	{
		try
		{
			return await FetchDeltaPageAsync(mailbox, folder, deltaToken, maxPageSize, resynced: false, ct);
		}
		catch (ODataError e) when (e.ResponseStatusCode == 410)
		{
			// 410 Gone — the token expired or the folder was rearranged. Graph's contract is to resync:
			// restart a fresh baseline. The caller re-reads a window (idempotent), so nothing is dropped.
			return await FetchDeltaPageAsync(mailbox, folder, token: null, maxPageSize, resynced: true, ct);
		}
	}

	private async Task<DeltaResult> FetchDeltaPageAsync(
		string mailbox, string folder, string? token, int maxPageSize, bool resynced, CancellationToken ct)
	{
		var builder = graphClient.Users[mailbox].MailFolders[folder].Messages.Delta;

		// First call sets $select/$top; a continuation token is an opaque URL that already carries them.
		var response = string.IsNullOrEmpty(token)
			? await builder.GetAsDeltaGetResponseAsync(config =>
				{
					config.QueryParameters.Select = MessageSelectFields;
					config.QueryParameters.Top = maxPageSize;
				}, ct)
			: await builder.WithUrl(token).GetAsDeltaGetResponseAsync(cancellationToken: ct);

		var changed = new List<Message>();
		var removed = new List<string>();
		foreach (var m in response?.Value ?? [])
		{
			// Removed items come back annotated with @removed and only an id.
			if (m.AdditionalData is not null && m.AdditionalData.ContainsKey("@removed"))
			{
				if (m.Id is not null) removed.Add(m.Id);
			}
			else
			{
				changed.Add(m);
			}
		}

		return new DeltaResult
		{
			Changed = MapMessages(changed),
			RemovedIds = removed,
			NextToken = response?.OdataNextLink ?? response?.OdataDeltaLink,
			More = response?.OdataNextLink is not null,
			Resynced = resynced,
		};
	}

	public async Task<IReadOnlyList<MailFolderInfo>> GetFoldersAsync(
		string mailbox,
		string? parentFolderId = null,
		bool recursive = false,
		CancellationToken ct = default)
	{
		string[] select = ["id", "displayName", "parentFolderId", "unreadItemCount", "totalItemCount", "childFolderCount"];
		var result = new List<MailFolderInfo>();
		var toVisit = new Queue<string?>();
		toVisit.Enqueue(parentFolderId); // null = top-level mailFolders; else this folder's children
		const int cap = 500;             // backstop against a pathological folder tree

		while (toVisit.Count > 0 && result.Count < cap)
		{
			string? parent = toVisit.Dequeue();
			List<MailFolder>? folders;
			if (parent is null)
			{
				var res = await graphClient.Users[mailbox].MailFolders
					.GetAsync(c => { c.QueryParameters.Select = select; c.QueryParameters.Top = 100; }, ct);
				folders = res?.Value;
			}
			else
			{
				var res = await graphClient.Users[mailbox].MailFolders[parent].ChildFolders
					.GetAsync(c => { c.QueryParameters.Select = select; c.QueryParameters.Top = 100; }, ct);
				folders = res?.Value;
			}

			if (folders is null) continue;
			foreach (var f in folders)
			{
				result.Add(new MailFolderInfo
				{
					Id = f.Id!,
					DisplayName = f.DisplayName ?? "(unnamed)",
					ParentFolderId = f.ParentFolderId,
					UnreadItemCount = f.UnreadItemCount ?? 0,
					TotalItemCount = f.TotalItemCount ?? 0,
					ChildFolderCount = f.ChildFolderCount ?? 0,
				});
				// Only descend when the caller asked for the whole tree — otherwise a single level
				// (top-level, or one folder's children) keeps the response bounded (#519).
				if (recursive && (f.ChildFolderCount ?? 0) > 0 && f.Id is not null)
					toVisit.Enqueue(f.Id);
			}
		}

		return result;
	}

	public async Task<MailFolderInfo> CreateFolderAsync(
		string mailbox,
		string name,
		string? parentFolderId = null,
		CancellationToken ct = default)
	{
		string[] select = ["id", "displayName", "parentFolderId", "unreadItemCount", "totalItemCount", "childFolderCount"];
		string filter = $"displayName eq '{name.Replace("'", "''")}'";

		// Create-or-get: a same-named folder already under the parent is returned rather than erroring.
		List<MailFolder>? existing = string.IsNullOrEmpty(parentFolderId)
			? (await graphClient.Users[mailbox].MailFolders
				.GetAsync(c => { c.QueryParameters.Select = select; c.QueryParameters.Filter = filter; }, ct))?.Value
			: (await graphClient.Users[mailbox].MailFolders[parentFolderId].ChildFolders
				.GetAsync(c => { c.QueryParameters.Select = select; c.QueryParameters.Filter = filter; }, ct))?.Value;
		if (existing is { Count: > 0 })
			return MapFolder(existing[0]);

		var body = new MailFolder { DisplayName = name };
		MailFolder? created = string.IsNullOrEmpty(parentFolderId)
			? await graphClient.Users[mailbox].MailFolders.PostAsync(body, cancellationToken: ct)
			: await graphClient.Users[mailbox].MailFolders[parentFolderId].ChildFolders.PostAsync(body, cancellationToken: ct);
		return MapFolder(created!);
	}

	public async Task<MailFolderInfo> RenameFolderAsync(
		string mailbox,
		string folderId,
		string newName,
		CancellationToken ct = default)
	{
		var updated = await graphClient.Users[mailbox].MailFolders[folderId]
			.PatchAsync(new MailFolder { DisplayName = newName }, cancellationToken: ct);
		return MapFolder(updated!);
	}

	public async Task<MailFolderInfo> MoveFolderAsync(
		string mailbox,
		string folderId,
		string newParentFolderId,
		CancellationToken ct = default)
	{
		// Need the folder's own name to guard against a same-name sibling under the target.
		var source = await graphClient.Users[mailbox].MailFolders[folderId]
			.GetAsync(c => c.QueryParameters.Select = ["id", "displayName", "parentFolderId"], ct)
			?? throw new InvalidOperationException($"Folder '{folderId}' not found.");

		await GuardNotSelfOrDescendantAsync(mailbox, source.Id!, newParentFolderId, ct);

		// Refuse to merge onto an existing same-name sibling under the target. Create-or-get semantics
		// deliberately do NOT apply to a move: a silent merge of two folders is worse than a duplicate.
		var clash = (await graphClient.Users[mailbox].MailFolders[newParentFolderId].ChildFolders
			.GetAsync(c =>
			{
				c.QueryParameters.Select = ["id"];
				c.QueryParameters.Filter = $"displayName eq '{(source.DisplayName ?? "").Replace("'", "''")}'";
			}, ct))?.Value;
		if (clash is { Count: > 0 })
			throw new InvalidOperationException(
				$"A folder named '{source.DisplayName}' already exists under the target — refusing to merge. Rename one first.");

		var moved = await graphClient.Users[mailbox].MailFolders[folderId].Move
			.PostAsync(new Microsoft.Graph.Users.Item.MailFolders.Item.Move.MovePostRequestBody { DestinationId = newParentFolderId }, cancellationToken: ct);
		return MapFolder(moved!);
	}

	public async Task<FolderDeletionResult> DeleteFolderAsync(
		string mailbox,
		string folderId,
		bool force = false,
		CancellationToken ct = default)
	{
		var folder = await graphClient.Users[mailbox].MailFolders[folderId]
			.GetAsync(c => c.QueryParameters.Select = ["id", "displayName", "parentFolderId", "totalItemCount", "childFolderCount"], ct)
			?? throw new InvalidOperationException($"Folder '{folderId}' not found.");

		// Refuse well-known folders — whether the caller addressed one by its well-known name or by its id.
		if (await IsWellKnownFolderAsync(mailbox, folderId, folder.Id!, ct))
			throw new InvalidOperationException($"Refusing to delete a well-known folder ('{folder.DisplayName}').");

		// Empty-only unless forced: a folder that still holds mail or subfolders is kept unless the caller opts in.
		int items = folder.TotalItemCount ?? 0;
		int children = folder.ChildFolderCount ?? 0;
		if (!force && (items > 0 || children > 0))
			throw new InvalidOperationException(
				$"Folder '{folder.DisplayName}' is not empty ({items} item(s), {children} subfolder(s)). Pass force to soft-delete it anyway.");

		string? previousParent = folder.ParentFolderId;

		// Soft delete = move under Deleted Items (recoverable). Never a hard delete.
		var moved = await graphClient.Users[mailbox].MailFolders[folderId].Move
			.PostAsync(new Microsoft.Graph.Users.Item.MailFolders.Item.Move.MovePostRequestBody { DestinationId = "deleteditems" }, cancellationToken: ct);

		return new FolderDeletionResult
		{
			DeletedFolderId = moved?.Id ?? folder.Id!,
			PreviousParentId = previousParent,
			Name = folder.DisplayName ?? "(unnamed)",
		};
	}

	// The well-known folders a delete must refuse. Graph accepts these as ids in the path, so we resolve
	// each to its real id and compare — catching both a name-addressed and an id-addressed protected folder.
	private static readonly string[] ProtectedWellKnown =
		["inbox", "sentitems", "deleteditems", "drafts", "junkemail", "archive", "outbox"];

	private async Task<bool> IsWellKnownFolderAsync(string mailbox, string requestedId, string resolvedId, CancellationToken ct)
	{
		if (ProtectedWellKnown.Contains(requestedId, StringComparer.OrdinalIgnoreCase))
			return true;
		foreach (var wk in ProtectedWellKnown)
		{
			try
			{
				var f = await graphClient.Users[mailbox].MailFolders[wk]
					.GetAsync(c => c.QueryParameters.Select = ["id"], ct);
				if (f?.Id is not null && string.Equals(f.Id, resolvedId, StringComparison.Ordinal))
					return true;
			}
			catch (ODataError)
			{
				// This mailbox may not have the folder (e.g. no Archive) — not protected if absent.
			}
		}
		return false;
	}

	// Walk up from the target parent through parentFolderId; if we reach the folder being moved, the move
	// would place a folder inside its own subtree (or onto itself). Bounded to avoid an unexpected cycle.
	private async Task GuardNotSelfOrDescendantAsync(string mailbox, string folderId, string newParentFolderId, CancellationToken ct)
	{
		string? cursor = newParentFolderId;
		for (int hops = 0; !string.IsNullOrEmpty(cursor) && hops < 64; hops++)
		{
			if (string.Equals(cursor, folderId, StringComparison.Ordinal))
				throw new InvalidOperationException("Cannot move a folder into itself or one of its own descendants.");
			var node = await graphClient.Users[mailbox].MailFolders[cursor]
				.GetAsync(c => c.QueryParameters.Select = ["id", "parentFolderId"], ct);
			cursor = node?.ParentFolderId;
		}
	}

	private static MailFolderInfo MapFolder(MailFolder f) => new()
	{
		Id = f.Id!,
		DisplayName = f.DisplayName ?? "(unnamed)",
		ParentFolderId = f.ParentFolderId,
		UnreadItemCount = f.UnreadItemCount ?? 0,
		TotalItemCount = f.TotalItemCount ?? 0,
		ChildFolderCount = f.ChildFolderCount ?? 0,
	};

	// --- Server-side inbox rules (Exchange messageRules; fire on delivery only) ---

	public async Task<IReadOnlyList<InboxRuleInfo>> GetInboxRulesAsync(string mailbox, CancellationToken ct = default)
	{
		var res = await graphClient.Users[mailbox].MailFolders["inbox"].MessageRules.GetAsync(cancellationToken: ct);
		return [.. (res?.Value ?? []).Select(MapRule)];
	}

	public async Task<InboxRuleInfo> CreateInboxRuleAsync(string mailbox, InboxRuleDraft rule, CancellationToken ct = default)
	{
		var body = new MessageRule
		{
			DisplayName = rule.DisplayName,
			IsEnabled = rule.IsEnabled,
			Sequence = rule.Sequence,
			Conditions = MapPredicates(rule.Conditions),
			Exceptions = MapPredicates(rule.Exceptions),
			Actions = MapActions(rule.Actions),
		};
		try
		{
			var created = await graphClient.Users[mailbox].MailFolders["inbox"].MessageRules.PostAsync(body, cancellationToken: ct);
			return MapRule(created!);
		}
		catch (Microsoft.Kiota.Abstractions.ApiException apiEx)
		{
			// Unmask the real Graph reason — the SDK otherwise surfaces an opaque "unable to deserialize" that
			// hides the HTTP status. Echo the ACTUAL request body (Kiota's wire JSON, camelCase) too, so a
			// body-shape rejection (UnableToDeserializePostBody) is attributable, not guessed (§23).
			throw new InvalidOperationException(
				$"Graph rejected the rule (HTTP {apiEx.ResponseStatusCode}): {RuleGraphDetail(apiEx)}. Request body sent: {await SerializeForDiagnosticsAsync(body)}", apiEx);
		}
	}

	public async Task<InboxRuleInfo> UpdateInboxRuleAsync(string mailbox, string ruleId, bool? isEnabled = null, string? displayName = null, int? sequence = null,
		InboxRuleConditions? conditions = null, InboxRuleConditions? exceptions = null, InboxRuleActions? actions = null, CancellationToken ct = default)
	{
		// PATCH sends only the properties the SDK sees set — a null stays absent, so unspecified fields are
		// untouched (isEnabled:false is a real value and IS sent). Conditions/exceptions/actions REPLACE the
		// rule's set wholesale when supplied, and are left untouched when null.
		var body = new MessageRule { IsEnabled = isEnabled, DisplayName = displayName, Sequence = sequence };
		if (conditions is not null) body.Conditions = MapPredicates(conditions);
		if (exceptions is not null) body.Exceptions = MapPredicates(exceptions);
		if (actions is not null) body.Actions = MapActions(actions);
		try
		{
			var updated = await graphClient.Users[mailbox].MailFolders["inbox"].MessageRules[ruleId]
				.PatchAsync(body, cancellationToken: ct);
			return MapRule(updated!);
		}
		catch (Microsoft.Kiota.Abstractions.ApiException apiEx)
		{
			throw new InvalidOperationException(
				$"Graph rejected the rule update (HTTP {apiEx.ResponseStatusCode}): {RuleGraphDetail(apiEx)}. Request body sent: {await SerializeForDiagnosticsAsync(body)}", apiEx);
		}
	}

	public async Task<InboxRuleInfo?> DeleteInboxRuleAsync(string mailbox, string ruleId, CancellationToken ct = default)
	{
		// Read the rule first, so the caller gets back exactly what was destroyed — a rule has no recycle bin,
		// so a delete that returns its definition makes an irreversible action recoverable (§21-residual).
		InboxRuleInfo? doomed = null;
		try
		{
			var rule = await graphClient.Users[mailbox].MailFolders["inbox"].MessageRules[ruleId].GetAsync(cancellationToken: ct);
			if (rule is not null) doomed = MapRule(rule);
		}
		catch (Microsoft.Kiota.Abstractions.ApiException)
		{
			// Couldn't read it back (already gone, or a permission gap) — still attempt the delete below.
		}

		await graphClient.Users[mailbox].MailFolders["inbox"].MessageRules[ruleId].DeleteAsync(cancellationToken: ct);
		return doomed;
	}

	private static InboxRuleInfo MapRule(MessageRule r) => new()
	{
		Id = r.Id!,
		DisplayName = r.DisplayName ?? "(unnamed)",
		Sequence = r.Sequence ?? 0,
		IsEnabled = r.IsEnabled ?? false,
		Conditions = MapPredicatesBack(r.Conditions),
		Actions = MapActionsBack(r.Actions),
		Exceptions = MapPredicatesBack(r.Exceptions),
	};

	private static MessageRulePredicates? MapPredicates(InboxRuleConditions? c)
		=> c is null ? null : new MessageRulePredicates
		{
			SubjectContains = c.SubjectContains?.ToList(),
			BodyContains = c.BodyContains?.ToList(),
			BodyOrSubjectContains = c.BodyOrSubjectContains?.ToList(),
			SenderContains = c.SenderContains?.ToList(),
			RecipientContains = c.RecipientContains?.ToList(),
			HeaderContains = c.HeaderContains?.ToList(),
			FromAddresses = ToRecipients(c.FromAddresses),
			SentToAddresses = ToRecipients(c.SentToAddresses),
			Categories = c.Categories?.ToList(),
			Importance = ParseImportance(c.Importance),
			MessageActionFlag = ParseActionFlag(c.MessageActionFlag),
			WithinSizeRange = c.WithinSizeRange is null ? null
				: new SizeRange { MinimumSize = c.WithinSizeRange.MinimumSizeKb, MaximumSize = c.WithinSizeRange.MaximumSizeKb },
			HasAttachments = c.HasAttachments,
			SentToMe = c.SentToMe,
			SentOnlyToMe = c.SentOnlyToMe,
			SentCcMe = c.SentCcMe,
			SentToOrCcMe = c.SentToOrCcMe,
			NotSentToMe = c.NotSentToMe,
			IsApprovalRequest = c.IsApprovalRequest,
			IsAutomaticForward = c.IsAutomaticForward,
			IsAutomaticReply = c.IsAutomaticReply,
			IsEncrypted = c.IsEncrypted,
			IsMeetingRequest = c.IsMeetingRequest,
			IsMeetingResponse = c.IsMeetingResponse,
			IsNonDeliveryReport = c.IsNonDeliveryReport,
			IsPermissionControlled = c.IsPermissionControlled,
			IsReadReceipt = c.IsReadReceipt,
			IsSigned = c.IsSigned,
			IsVoicemail = c.IsVoicemail,
		};

	private static InboxRuleConditions? MapPredicatesBack(MessageRulePredicates? p)
		=> p is null ? null : new InboxRuleConditions
		{
			SubjectContains = p.SubjectContains,
			BodyContains = p.BodyContains,
			BodyOrSubjectContains = p.BodyOrSubjectContains,
			SenderContains = p.SenderContains,
			RecipientContains = p.RecipientContains,
			HeaderContains = p.HeaderContains,
			FromAddresses = AddressesOf(p.FromAddresses),
			SentToAddresses = AddressesOf(p.SentToAddresses),
			Categories = p.Categories,
			Importance = p.Importance?.ToString(),
			MessageActionFlag = p.MessageActionFlag?.ToString(),
			WithinSizeRange = p.WithinSizeRange is null ? null
				: new InboxRuleSizeRange { MinimumSizeKb = p.WithinSizeRange.MinimumSize, MaximumSizeKb = p.WithinSizeRange.MaximumSize },
			HasAttachments = p.HasAttachments,
			SentToMe = p.SentToMe,
			SentOnlyToMe = p.SentOnlyToMe,
			SentCcMe = p.SentCcMe,
			SentToOrCcMe = p.SentToOrCcMe,
			NotSentToMe = p.NotSentToMe,
			IsApprovalRequest = p.IsApprovalRequest,
			IsAutomaticForward = p.IsAutomaticForward,
			IsAutomaticReply = p.IsAutomaticReply,
			IsEncrypted = p.IsEncrypted,
			IsMeetingRequest = p.IsMeetingRequest,
			IsMeetingResponse = p.IsMeetingResponse,
			IsNonDeliveryReport = p.IsNonDeliveryReport,
			IsPermissionControlled = p.IsPermissionControlled,
			IsReadReceipt = p.IsReadReceipt,
			IsSigned = p.IsSigned,
			IsVoicemail = p.IsVoicemail,
		};

	private static MessageRuleActions MapActions(InboxRuleActions a) => new()
	{
		MoveToFolder = a.MoveToFolder,
		CopyToFolder = a.CopyToFolder,
		ForwardTo = ToRecipients(a.ForwardTo),
		ForwardAsAttachmentTo = ToRecipients(a.ForwardAsAttachmentTo),
		RedirectTo = ToRecipients(a.RedirectTo),
		MarkAsRead = a.MarkAsRead,
		MarkImportance = ParseImportance(a.MarkImportance),
		Delete = a.Delete,
		PermanentDelete = a.PermanentDelete,
		AssignCategories = a.AssignCategories?.ToList(),
		StopProcessingRules = a.StopProcessingRules,
	};

	private static InboxRuleActions? MapActionsBack(MessageRuleActions? a)
		=> a is null ? null : new InboxRuleActions
		{
			MoveToFolder = a.MoveToFolder,
			CopyToFolder = a.CopyToFolder,
			ForwardTo = AddressesOf(a.ForwardTo),
			ForwardAsAttachmentTo = AddressesOf(a.ForwardAsAttachmentTo),
			RedirectTo = AddressesOf(a.RedirectTo),
			MarkAsRead = a.MarkAsRead,
			MarkImportance = a.MarkImportance?.ToString(),
			Delete = a.Delete,
			PermanentDelete = a.PermanentDelete,
			AssignCategories = a.AssignCategories,
			StopProcessingRules = a.StopProcessingRules,
		};

	// Serialise the exact request body the SDK would send (Kiota wire JSON, camelCase) for the failure echo —
	// so a body-shape rejection reveals the real body, not the internal PascalCase model (§23).
	private static async Task<string> SerializeForDiagnosticsAsync(MessageRule body)
	{
		try { return await Microsoft.Kiota.Abstractions.Serialization.KiotaSerializer.SerializeAsStringAsync("application/json", body); }
		catch (Exception ex) { return $"(could not serialise request body: {ex.Message})"; }
	}

	private static string RuleGraphDetail(Microsoft.Kiota.Abstractions.ApiException apiEx)
		=> apiEx is ODataError { Error: { } err }
			? $"{err.Message} (code: {err.Code})"
			: $"{apiEx.Message} — if the status is 403, the mailbox is likely missing the MailboxSettings.ReadWrite scope (add it via O365:Auth:AdditionalScopes)";

	private static Importance? ParseImportance(string? s)
		=> string.IsNullOrWhiteSpace(s) ? null : Enum.TryParse<Importance>(s, ignoreCase: true, out var v) ? v : null;

	private static MessageActionFlag? ParseActionFlag(string? s)
		=> string.IsNullOrWhiteSpace(s) ? null : Enum.TryParse<MessageActionFlag>(s, ignoreCase: true, out var v) ? v : null;

	private static List<string>? AddressesOf(List<Recipient>? recipients)
		=> recipients is null || recipients.Count == 0
			? null
			: [.. recipients.Select(r => r.EmailAddress?.Address).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!)];

	public async Task<IReadOnlyList<EmailAttachmentInfo>> GetAttachmentsAsync(
		string mailbox,
		string messageId,
		CancellationToken ct = default)
	{
		var res = await graphClient.Users[mailbox].Messages[messageId].Attachments
			.GetAsync(config => config.QueryParameters.Select = ["id", "name", "contentType", "size", "isInline"], ct);

		return res?.Value?.Select(a => new EmailAttachmentInfo
		{
			Id = a.Id ?? "",
			Name = a.Name ?? "(unnamed)",
			ContentType = a.ContentType,
			Size = a.Size ?? 0,
			IsInline = a.IsInline ?? false,
		}).ToList() ?? [];
	}

	public async Task<EmailAttachmentContent?> GetAttachmentAsync(
		string mailbox,
		string messageId,
		string attachmentId,
		CancellationToken ct = default)
	{
		var attachment = await graphClient.Users[mailbox].Messages[messageId].Attachments[attachmentId].GetAsync(cancellationToken: ct);

		// Only file attachments carry inline bytes; item/reference attachments have no ContentBytes here.
		if (attachment is not FileAttachment file || file.ContentBytes is null)
			return null;

		return new EmailAttachmentContent
		{
			Id = file.Id ?? attachmentId,
			Name = file.Name ?? "(unnamed)",
			ContentType = file.ContentType,
			Size = file.Size ?? file.ContentBytes.Length,
			IsInline = file.IsInline ?? false,
			Content = file.ContentBytes,
		};
	}

	public async Task<MailboxInfo> GetMailboxInfoAsync(
		string mailbox,
		CancellationToken ct = default)
	{
		// Display name is a best-effort nicety. Read it from /me (User.Read, no admin consent) — but only
		// claim it when /me actually IS the requested mailbox. LIMITATION (#513): /me only ever returns the
		// signed-in user, so shared mailboxes / other users / arbitrary senders fall back to the address.
		// Resolving those needs a directory read (User.ReadBasic.All, admin consent) — deferred to #513, but
		// expect to add it once triage spans the estate. The inbox read below is a Mail op and stays required.
		string displayName = mailbox;
		try
		{
			var me = await graphClient.Me
				.GetAsync(config => config.QueryParameters.Select = ["displayName", "mail", "userPrincipalName"], ct);
			bool isSelf = string.Equals(me?.Mail, mailbox, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(me?.UserPrincipalName, mailbox, StringComparison.OrdinalIgnoreCase);
			if (isSelf && !string.IsNullOrWhiteSpace(me?.DisplayName))
				displayName = me!.DisplayName!;
		}
		catch (Exception) when (!ct.IsCancellationRequested)
		{
			// Profile read unavailable — keep the mailbox address as the name.
		}

		var inbox = await graphClient.Users[mailbox].MailFolders["Inbox"]
			.GetAsync(config => config.QueryParameters.Select = ["unreadItemCount", "totalItemCount"], ct);

		return new MailboxInfo
		{
			Mailbox = mailbox,
			DisplayName = displayName,
			UnreadCount = inbox?.UnreadItemCount ?? 0,
			TotalCount = inbox?.TotalItemCount ?? 0
		};
	}

	// Maps the old EmailFilter onto the provider-neutral EmailQuery (unread-only).
	private static EmailQuery ToQuery(EmailFilter? f, int maxResults, int skip = 0)
	{
		var predicates = new List<EmailPredicate>();
		if (!string.IsNullOrWhiteSpace(f?.FromContains))
			predicates.Add(new EmailPredicate(EmailField.From, EmailOperator.Contains, f!.FromContains!));
		if (!string.IsNullOrWhiteSpace(f?.SubjectContains))
			predicates.Add(new EmailPredicate(EmailField.Subject, EmailOperator.Contains, f!.SubjectContains!));

		return new EmailQuery
		{
			IsRead = false,
			Received = f?.ReceivedAfter is not null || f?.ReceivedBefore is not null
				? new DateRange { After = f?.ReceivedAfter, Before = f?.ReceivedBefore }
				: null,
			ExcludeCategories = f?.ExcludeCategories ?? [],
			ExcludeDeletedAndJunk = f?.ExcludeDeletedAndJunk ?? false,
			IncludeCc = f?.IncludeCc ?? false,
			IncludeBody = f?.IncludeBody ?? false,
			BodyFormat = f?.BodyFormat ?? EmailBodyFormat.Text,
			Predicates = predicates,
			Top = maxResults,
			Skip = skip,
		};
	}

	// Lean list projection — deliberately NO "body": a 98K-char HTML body per message overflows agent
	// context (see #496). Lists carry bodyPreview only; full content is a separate get_email call (#497).
	private static readonly string[] MessageSelectFields =
	[
		"id", "subject", "from", "receivedDateTime", "bodyPreview",
		"isRead", "flag", "categories", "toRecipients", "hasAttachments",
		"conversationId", "internetMessageId", "parentFolderId", "webLink"
	];

	// Detail projection for single-message fetches (get_email, #497): the lean fields plus the full body.
	private static readonly string[] MessageDetailSelectFields = [.. MessageSelectFields, "body", "ccRecipients"];

	private static IReadOnlyList<EmailMessage> MapMessages(List<Message>? messages)
	{
		if (messages is null or { Count: 0 })
			return [];

		return messages.Select(m => new EmailMessage
		{
			Id = m.Id!,
			Subject = m.Subject ?? "(no subject)",
			FromAddress = m.From?.EmailAddress?.Address ?? "",
			FromName = m.From?.EmailAddress?.Name ?? "",
			ReceivedDateTime = m.ReceivedDateTime ?? DateTimeOffset.MinValue,
			BodyPreview = m.BodyPreview ?? "",
			BodyContent = m.Body?.Content,
			BodyContentType = m.Body?.ContentType?.ToString().ToLowerInvariant(),
			IsRead = m.IsRead ?? false,
			IsFlagged = m.Flag?.FlagStatus == FollowupFlagStatus.Flagged,
			Categories = m.Categories?.ToList() ?? [],
			ToRecipients = m.ToRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? [],
			CcRecipients = m.CcRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? [],
			HasAttachments = m.HasAttachments ?? false,
			ConversationId = m.ConversationId,
			InternetMessageId = m.InternetMessageId,
			ParentFolderId = m.ParentFolderId,
			WebLink = m.WebLink
		}).ToList();
	}
}
