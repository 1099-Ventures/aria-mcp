using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Ten99.Aria.Common.Secrets;
using Ten99.Aria.Integration.Graph.Email;
using AriaGraphClientFactory = Ten99.Aria.Integration.Graph.GraphClientFactory;
using Ten99.Aria.Mcp.Email.O365.Configuration;
using Ten99.Aria.Mcp.Email.O365.Operations;

//	Debug Cmd: --mcp "D:\Dev\1099\Aria\Code\Ten99.Aria.Mcp.Email.O365\bin\Debug\net10.0\Ten99.Aria.Mcp.Email.O365.dll" --usesHttp true --userSecrets "Ten99.Aria.Mcp.Email.O365.Configuration.O365EmailConfiguration, Ten99.Aria.Mcp.Email.O365"
namespace Ten99.Aria.Mcp.Email.O365;

[McpServerToolType]
public static class O365EmailTool
{
	private static readonly SemaphoreSlim _rateLimiter = new(10, 10);
	private static DateTime _lastReset = DateTime.UtcNow;

	[McpServerTool(Name = "email_health")]
	[Description("Test O365 Graph API connection and mailbox access via the shared Integration.Graph.Email layer")]
	public static async Task<CallToolResult> Health(
		IConfiguration configuration,
		HttpClient httpClient,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		return await new O365EmailHealthOperation(_rateLimiter, ref _lastReset)
			.Execute(configuration, httpClient, typedSecrets, genericSecrets, cancellationToken);
	}

	[McpServerTool(Name = "email_read_mailboxes")]
	[Description("List the mailboxes this MCP is configured to reach and PROBE each for access — so a "
		+ "session knows up front which mailboxes it can act on, rather than discovering by failing. Returns "
		+ "the auth mode and, per mailbox, whether it's reachable (a lightweight folder-counts probe), plus "
		+ "unread/total + displayName when reachable or the error when not. The candidate set is the configured "
		+ "default (O365:Auth:DefaultMailbox) plus any operator-declared O365:Auth:Mailboxes (comma/semicolon-"
		+ "separated UPNs) — Graph has no reliable 'shared mailboxes I can access' enumeration, so the estate is "
		+ "operator-declared. Reaching a shared/other mailbox needs Full Access + Mail.ReadWrite.Shared; "
		+ "Send-As is an Exchange permission not probed here.")]
	public static async Task<CallToolResult> ListMailboxes(
		IConfiguration configuration,
		HttpClient httpClient,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			string? defaultMailbox = typedSecrets?.Settings?.DefaultMailbox ?? configuration["O365:Auth:DefaultMailbox"];
			string? estateCsv = typedSecrets?.Settings?.Mailboxes ?? configuration["O365:Auth:Mailboxes"];

			// Candidates = the default plus any operator-declared estate, de-duplicated (case-insensitive).
			var candidates = new List<string>();
			void Add(string? m) { m = m?.Trim(); if (!string.IsNullOrEmpty(m) && !candidates.Contains(m, StringComparer.OrdinalIgnoreCase)) candidates.Add(m); }
			Add(defaultMailbox);
			foreach (var m in SplitAddresses(estateCsv)) Add(m);
			if (candidates.Count == 0)
				return InputError("No mailboxes configured: set O365:Auth:DefaultMailbox and/or O365:Auth:Mailboxes.");

			// One credential/service reaches every mailbox (it's just the /users/{mailbox} segment); build once,
			// probe each. Use the first candidate as the interactive login hint when that mode is active.
			var (service, _) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, candidates[0], cancellationToken);
			string? authModeRaw = typedSecrets?.Settings?.AuthMode ?? configuration["O365:Auth:AuthMode"];
			string authMode = O365GraphClientBuilder.ParseAuthMode(authModeRaw).ToString();

			using var gate = new SemaphoreSlim(4);
			var probes = candidates.Select(async mbx =>
			{
				await gate.WaitAsync(cancellationToken);
				try
				{
					var info = await service.GetMailboxInfoAsync(mbx, cancellationToken);
					return new
					{
						mailbox = mbx,
						reachable = true,
						isDefault = string.Equals(mbx, defaultMailbox, StringComparison.OrdinalIgnoreCase),
						displayName = (string?)info.DisplayName,
						unread = (int?)info.UnreadCount,
						total = (int?)info.TotalCount,
						error = (string?)null,
					};
				}
				catch (Exception ex)
				{
					return new
					{
						mailbox = mbx,
						reachable = false,
						isDefault = string.Equals(mbx, defaultMailbox, StringComparison.OrdinalIgnoreCase),
						displayName = (string?)null,
						unread = (int?)null,
						total = (int?)null,
						error = (string?)ex.Message,
					};
				}
				finally { gate.Release(); }
			});
			var mailboxes = await Task.WhenAll(probes);

			var payload = new
			{
				authMode,
				count = mailboxes.Length,
				reachable = mailboxes.Count(m => m.reachable),
				mailboxes,
			};
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error listing mailboxes: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_read_unread")]
	[Description("Get unread emails, newest first, across ALL folders (wherever rules auto-filed them) "
		+ "EXCEPT Deleted Items and Junk Email, which are excluded by default as noise. Targets `mailbox` "
		+ "(a UPN — your own or a shared/other mailbox you can access) or the configured default when "
		+ "omitted. Optional filters: `fromContains`, `subjectContains`, and a received-date window "
		+ "`since`/`before` (ISO-8601, e.g. 2026-08-01 or 2026-08-01T00:00:00Z). Set includeSpamAndTrash "
		+ "to also include Junk/Deleted. Returns a lean list — metadata + a short bodyPreview, no full body "
		+ "(use email_read_body); bearer-shaped secrets in bodyPreview are masked. Set `includeBody:true` to "
		+ "widen each row with the full body (`bodyFormat` text|default or html) — use when you'd otherwise call "
		+ "email_read_body per message; lean by default. `bodyMaxChars` caps an included body's length "
		+ "(0 = full). Page with "
		+ "maxResults + skip: when hasMore is true, call again with skip = nextSkip.")]
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
		string? excludeCategories = null,
		bool includeSpamAndTrash = false,
		bool includeCc = false,
		bool includeBody = false,
		string bodyFormat = "text",
		int bodyMaxChars = 0,
		bool verbose = false,
		bool includePreview = true,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (ParseCutoff(since, nameof(since), out var receivedAfter) is { } e1) return InputError(e1);
			if (ParseCutoff(before, nameof(before), out var receivedBefore) is { } e2) return InputError(e2);

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var filter = new EmailFilter
			{
				FromContains = fromContains,
				SubjectContains = subjectContains,
				ReceivedAfter = receivedAfter,
				ReceivedBefore = receivedBefore,
				ExcludeDeletedAndJunk = !includeSpamAndTrash,
				ExcludeCategories = SplitCsv(excludeCategories),
				IncludeCc = includeCc,
				IncludeBody = includeBody,
				BodyFormat = ParseBodyFormat(bodyFormat),
			};

			var filtersApplied = new Dictionary<string, object?>();
			if (!string.IsNullOrWhiteSpace(fromContains)) filtersApplied["fromContains"] = fromContains;
			if (!string.IsNullOrWhiteSpace(subjectContains)) filtersApplied["subjectContains"] = subjectContains;
			if (!string.IsNullOrWhiteSpace(since)) filtersApplied["since"] = since;
			if (!string.IsNullOrWhiteSpace(before)) filtersApplied["before"] = before;
			if (!string.IsNullOrWhiteSpace(excludeCategories)) filtersApplied["excludeCategories"] = excludeCategories;
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
	[Description("Get flagged (Follow Up) emails, newest first, from `mailbox` (UPN) or the configured "
		+ "default when omitted. Optional received-date window `since`/`before` (ISO-8601). Returns a lean "
		+ "list (bodyPreview only, no full body — use email_read_body). Set `includeBody:true` to widen each "
		+ "row with the full body (`bodyFormat` text|default or html) — use when you'd otherwise call "
		+ "email_read_body per message; lean by default. `bodyMaxChars` caps an included body's length "
		+ "(0 = full). Page with maxResults + skip: "
		+ "when hasMore is true, call again with skip = nextSkip. (Flagged spans all folders — flagged mail "
		+ "rarely sits in Junk/Deleted — and is sorted newest-first in-process.)")]
	public static async Task<CallToolResult> GetFlagged(
		IConfiguration configuration,
		HttpClient httpClient,
		string? mailbox = null,
		int maxResults = 20,
		int skip = 0,
		string? since = null,
		string? before = null,
		string? excludeCategories = null,
		bool includeSpamAndTrash = false,
		bool includeCc = false,
		bool includeBody = false,
		string bodyFormat = "text",
		int bodyMaxChars = 0,
		bool verbose = false,
		bool includePreview = true,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (ParseCutoff(since, nameof(since), out var receivedAfter) is { } e1) return InputError(e1);
			if (ParseCutoff(before, nameof(before), out var receivedBefore) is { } e2) return InputError(e2);

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var filter = new EmailFilter
			{
				ReceivedAfter = receivedAfter,
				ReceivedBefore = receivedBefore,
				ExcludeDeletedAndJunk = !includeSpamAndTrash,
				ExcludeCategories = SplitCsv(excludeCategories),
				IncludeCc = includeCc,
				IncludeBody = includeBody,
				BodyFormat = ParseBodyFormat(bodyFormat),
			};

			var filtersApplied = new Dictionary<string, object?>();
			if (!string.IsNullOrWhiteSpace(since)) filtersApplied["since"] = since;
			if (!string.IsNullOrWhiteSpace(before)) filtersApplied["before"] = before;
			if (!string.IsNullOrWhiteSpace(excludeCategories)) filtersApplied["excludeCategories"] = excludeCategories;
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
		+ "pass `parentFolderId` to list one folder's direct children (drill down level by level), or "
		+ "`recursive:true` for the whole tree (can be large — hundreds of folders). Nesting is via each "
		+ "folder's `ParentFolderId`. Targets `mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> ListFolders(
		IConfiguration configuration,
		HttpClient httpClient,
		string? mailbox = null,
		string? parentFolderId = null,
		bool recursive = false,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
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

	[McpServerTool(Name = "email_edit_create_folder")]
	[Description("Create a mail folder — top-level, or under `parentFolderId` (a folder id from "
		+ "email_read_folders) when given. Create-or-get: if a folder with that name already exists under "
		+ "the parent it's returned rather than erroring, so this is safe to re-run. Returns the folder "
		+ "{id, displayName, parentFolderId}; pair with email_edit_move to file mail into it. Targets "
		+ "`mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> CreateFolder(
		IConfiguration configuration,
		HttpClient httpClient,
		string name,
		string? parentFolderId = null,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(name))
				return InputError("`name` is required.");
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
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
	[Description("Rename a mail folder (identified by `folderId` from email_read_folders) to `newName`, in "
		+ "place under the same parent. Returns the updated folder {id, displayName, parentFolderId}. Targets "
		+ "`mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> RenameFolder(
		IConfiguration configuration,
		HttpClient httpClient,
		string folderId,
		string newName,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folderId)) return InputError("`folderId` is required.");
			if (string.IsNullOrWhiteSpace(newName)) return InputError("`newName` is required.");
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
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
	[Description("Reparent a mail folder (identified by `folderId` from email_read_folders) under "
		+ "`newParentFolderId` — the correction for a folder filed in the wrong place. Non-destructive: the "
		+ "mail inside travels with it. Refuses to move a folder into itself/a descendant, or to merge onto an "
		+ "existing same-name folder under the target (rename one first). Returns the moved folder "
		+ "{id, displayName, parentFolderId}. Targets `mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> MoveFolder(
		IConfiguration configuration,
		HttpClient httpClient,
		string folderId,
		string newParentFolderId,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folderId)) return InputError("`folderId` is required.");
			if (string.IsNullOrWhiteSpace(newParentFolderId)) return InputError("`newParentFolderId` is required.");
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
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
	[Description("Soft-delete a mail folder (identified by `folderId` from email_read_folders) by moving it "
		+ "to Deleted Items — recoverable, never a hard delete. Refuses well-known folders (Inbox, Sent, "
		+ "Deleted Items, Drafts, Junk, Archive) and, unless `force` is true, a folder that still holds "
		+ "messages or subfolders. Returns {deletedFolderId, previousParentId, name} — the undo record. "
		+ "Targets `mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> DeleteFolder(
		IConfiguration configuration,
		HttpClient httpClient,
		string folderId,
		bool force = false,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folderId)) return InputError("`folderId` is required.");
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var deleted = await emailService.DeleteFolderAsync(resolvedMailbox, folderId, force, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, deleted };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error deleting folder: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_read_rules")]
	[Description("List the mailbox's server-side inbox rules (Exchange/Graph messageRules) — read-only. "
		+ "Rules fire ON DELIVERY ONLY: they are the steady-state disposition and do NOT act on mail already "
		+ "in the mailbox (use email_read_list + the move/batch tools for backlog). Returns {count, rules[]} "
		+ "with each rule {id, displayName, sequence, isEnabled, conditions, actions, exceptions}. Needs the "
		+ "MailboxSettings.Read delegated scope (add it via O365:Auth:AdditionalScopes). Targets `mailbox` (UPN) or the "
		+ "configured default. O365 only — IMAP has no server-side rules.")]
	public static async Task<CallToolResult> ListRules(
		IConfiguration configuration,
		HttpClient httpClient,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var rules = await emailService.GetInboxRulesAsync(resolvedMailbox, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, count = rules.Count, rules };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error listing rules: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_create_rule")]
	[Description("Create a server-side inbox rule (Exchange/Graph messageRule) — the steady-state disposition "
		+ "of triage, applied to FUTURE deliveries only, never the existing backlog. The MCP is a mechanism: "
		+ "you decide the rule. `displayName` required. `conditionsJson`/`exceptionsJson` are JSON over the full "
		+ "Graph messageRulePredicates set — the contains predicates (subjectContains, bodyContains, "
		+ "bodyOrSubjectContains, senderContains, recipientContains, headerContains — string arrays), "
		+ "fromAddresses/sentToAddresses, categories, importance ('low'|'normal'|'high'), messageActionFlag, "
		+ "withinSizeRange {minimumSizeKb,maximumSizeKb}, hasAttachments, and the recipient-scope/message-type "
		+ "flags (sentToMe, sentOnlyToMe, sentCcMe, sentToOrCcMe, notSentToMe, isAutomaticForward/Reply, "
		+ "isMeetingRequest/Response, isEncrypted, isSigned, isReadReceipt, isVoicemail, ...). Multiple set "
		+ "fields AND-combine; values within a list OR. `actionsJson` (required) matches {moveToFolder, "
		+ "copyToFolder, forwardTo, forwardAsAttachmentTo, redirectTo, markAsRead, markImportance, delete "
		+ "(soft), permanentDelete (NOT recoverable), assignCategories, stopProcessingRules} — folder ids from "
		+ "email_read_folders. `sequence` sets evaluation order (lower first); `isEnabled` "
		+ "default true. WORKED EXAMPLE — conditionsJson: {\"senderContains\":[\"@e-mails.microsoft.com\"]}, "
		+ "actionsJson: {\"moveToFolder\":\"<folder id from email_read_folders>\",\"stopProcessingRules\":true}. "
		+ "On failure the response echoes what the MCP parsed under `parsed` plus Graph's real reason. "
		+ "Needs the MailboxSettings.ReadWrite delegated scope. Targets `mailbox` or the default. "
		+ "O365 only.")]
	public static async Task<CallToolResult> CreateRule(
		IConfiguration configuration,
		HttpClient httpClient,
		string displayName,
		string actionsJson,
		string? conditionsJson = null,
		string? exceptionsJson = null,
		int? sequence = null,
		bool isEnabled = true,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		InboxRuleConditions? conditions = null, exceptions = null;
		InboxRuleActions? actions = null;
		try
		{
			if (string.IsNullOrWhiteSpace(displayName)) return InputError("`displayName` is required.");

			try
			{
				actions = ParseRuleJson<InboxRuleActions>(actionsJson);
				conditions = ParseRuleJson<InboxRuleConditions>(conditionsJson);
				exceptions = ParseRuleJson<InboxRuleConditions>(exceptionsJson);
			}
			catch (JsonException jex)
			{
				return InputError($"Could not parse rule JSON: {jex.Message}");
			}

			if (actions is null || !HasAnyAction(actions))
				return InputError("`actionsJson` must be a JSON object with at least one action (e.g. moveToFolder, markAsRead, delete).");

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var draft = new InboxRuleDraft
			{
				DisplayName = displayName.Trim(),
				Sequence = sequence,
				IsEnabled = isEnabled,
				Conditions = conditions,
				Exceptions = exceptions,
				Actions = actions,
			};
			var rule = await emailService.CreateInboxRuleAsync(resolvedMailbox, draft, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, rule };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return RuleError("Error creating rule", ex, conditions, exceptions, actions);
		}
	}

	private static readonly JsonSerializerOptions RuleJsonOptions = new() { PropertyNameCaseInsensitive = true };

	private static T? ParseRuleJson<T>(string? json) where T : class
		=> string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, RuleJsonOptions);

	private static bool HasAnyAction(InboxRuleActions a)
		=> a.MoveToFolder is not null || a.CopyToFolder is not null || a.ForwardTo is { Count: > 0 }
			|| a.ForwardAsAttachmentTo is { Count: > 0 } || a.RedirectTo is { Count: > 0 }
			|| a.MarkAsRead is not null || a.MarkImportance is not null || a.Delete is not null
			|| a.PermanentDelete is not null || a.AssignCategories is { Count: > 0 }
			|| a.StopProcessingRules is not null;

	[McpServerTool(Name = "email_edit_update_rule")]
	[Description("Update a server-side inbox rule (from email_read_rules). `ruleId` required; supply any of: "
		+ "`isEnabled` (enable/disable), `displayName` (rename), `sequence` (reorder — lower runs first), and "
		+ "`conditionsJson`/`exceptionsJson`/`actionsJson` (same shapes as email_edit_create_rule). Conditions/"
		+ "actions REPLACE the rule's set WHOLESALE — to add one domain to a senderContains, read the rule, "
		+ "extend the array locally, and send the whole array back (email_read_rules returns them in the same "
		+ "shape). Only the fields you pass change. Refuses a conditionsJson that resolves to no conditions (an "
		+ "unconditional rule swallows the inbox) and an actionless actionsJson. Needs MailboxSettings.ReadWrite. "
		+ "Returns the updated rule. `mailbox` (UPN) or default. O365 only.")]
	public static async Task<CallToolResult> UpdateRule(
		IConfiguration configuration,
		HttpClient httpClient,
		string ruleId,
		bool? isEnabled = null,
		string? displayName = null,
		int? sequence = null,
		string? conditionsJson = null,
		string? exceptionsJson = null,
		string? actionsJson = null,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		InboxRuleConditions? conditions = null, exceptions = null;
		InboxRuleActions? actions = null;
		try
		{
			if (string.IsNullOrWhiteSpace(ruleId)) return InputError("`ruleId` is required.");

			try
			{
				conditions = ParseRuleJson<InboxRuleConditions>(conditionsJson);
				exceptions = ParseRuleJson<InboxRuleConditions>(exceptionsJson);
				actions = ParseRuleJson<InboxRuleActions>(actionsJson);
			}
			catch (JsonException jex)
			{
				return InputError($"Could not parse rule JSON: {jex.Message}");
			}

			if (isEnabled is null && string.IsNullOrWhiteSpace(displayName) && sequence is null
				&& conditionsJson is null && exceptionsJson is null && actionsJson is null)
				return InputError("Supply at least one of isEnabled, displayName, sequence, conditionsJson, exceptionsJson, actionsJson to update.");

			// §17a guardrails: a supplied-but-empty conditionsJson makes the rule unconditional (a filing/
			// deleting action would then swallow the whole inbox); an actionless actionsJson leaves it inert.
			if (conditionsJson is not null && !HasAnyCondition(conditions))
				return InputError("`conditionsJson` resolves to no conditions — an unconditional rule matches every delivered message. Narrow the rule rather than clearing its conditions.");
			if (actionsJson is not null && (actions is null || !HasAnyAction(actions)))
				return InputError("`actionsJson` must specify at least one action (a rule with no action is inert).");

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var rule = await emailService.UpdateInboxRuleAsync(resolvedMailbox, ruleId, isEnabled,
				string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(), sequence, conditions, exceptions, actions, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, rule };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return RuleError("Error updating rule", ex, conditions, exceptions, actions);
		}
	}

	private static bool HasAnyCondition(InboxRuleConditions? c)
		=> c is not null && JsonSerializer.Serialize(c, JsonOptions) != "{}";

	// §17b: on a rule failure, echo the conditions/actions the MCP actually parsed from the caller's JSON,
	// so they can tell whether the fault is their JSON, the MCP's mapping, or Graph (whose real message is
	// now surfaced, not the SDK's opaque "unable to deserialize").
	private static CallToolResult RuleError(string prefix, Exception ex, InboxRuleConditions? conditions, InboxRuleConditions? exceptions, InboxRuleActions? actions)
	{
		var payload = new
		{
			error = $"{prefix}: {ex.Message}",
			hint = "`parsed` below is what the MCP understood from your JSON. If it matches your intent, the fault is Graph's (see the message); if not, fix the JSON.",
			parsed = new { conditions, exceptions, actions },
		};
		return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
	}

	[McpServerTool(Name = "email_edit_delete_rule")]
	[Description("Delete a server-side inbox rule by `ruleId` (from email_read_rules). Unlike message/folder "
		+ "deletes this is NOT soft — a rule is configuration, not content, and Graph has no rule recycle bin; "
		+ "recreate it with email_edit_create_rule if needed. Needs the MailboxSettings.ReadWrite delegated "
		+ "scope. Returns {deletedRuleId, deletedRule} — the full definition it removed, so you can recreate it "
		+ "with email_edit_create_rule if the delete was wrong (a rule has no recycle bin). Targets `mailbox` "
		+ "(UPN) or the default. O365 only.")]
	public static async Task<CallToolResult> DeleteRule(
		IConfiguration configuration,
		HttpClient httpClient,
		string ruleId,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(ruleId)) return InputError("`ruleId` is required.");
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var deletedRule = await emailService.DeleteInboxRuleAsync(resolvedMailbox, ruleId, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, deletedRuleId = ruleId, deletedRule };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error deleting rule: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_read_list")]
	[Description("General message list — the flexible read the sugar tools wrap. Scope to a `folder` "
		+ "(well-known name: inbox, junkemail, deleteditems, archive, sentitems, drafts; or a folder id) or "
		+ "omit for the whole mailbox. Filter by isRead, isFlagged, fromContains, subjectContains, toContains "
		+ "(To only), recipientContains (To OR Cc — for sent-mail / matter reconstruction), a "
		+ "since/before date window (ISO-8601), withCategories (any-of) and excludeCategories. Pass "
		+ "`conversationId` to walk a whole thread across ALL folders incl. Sent Items (folder + Junk/"
		+ "Deleted exclusion are ignored for a chain). Set "
		+ "oldestFirst for backlog order (oldest→newest), includeCc to add CC recipients. Set `includeBody:true` "
		+ "to widen each row with the full body (`bodyFormat` text|default or html) — use when you'd otherwise "
		+ "call email_read_body per message; lean by default. `bodyMaxChars` caps an included body's length "
		+ "(0 = full). With no "
		+ "folder, Deleted Items + Junk are excluded unless includeSpamAndTrash. Same lean list + paging as "
		+ "email_read_unread. Unread-Junk audit = folder:\"junkemail\", isRead:false.")]
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
		string? withCategories = null,
		string? excludeCategories = null,
		string? conversationId = null,
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
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
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

			// Walking a thread: with a conversationId, don't scope to a folder or exclude Junk/Deleted —
			// a chain routinely has inbound in a filed folder and replies in Sent, so span everything.
			bool chain = !string.IsNullOrWhiteSpace(conversationId);

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var query = new EmailQuery
			{
				Folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim(),
				IsRead = isRead,
				IsFlagged = isFlagged,
				ConversationIds = chain ? [conversationId!.Trim()] : [],
				Received = receivedAfter is not null || receivedBefore is not null
					? new DateRange { After = receivedAfter, Before = receivedBefore }
					: null,
				Predicates = predicates,
				IncludeCategories = SplitCsv(withCategories),
				ExcludeCategories = SplitCsv(excludeCategories),
				ExcludeDeletedAndJunk = !chain && string.IsNullOrWhiteSpace(folder) && !includeSpamAndTrash,
				IncludeCc = includeCc,
				IncludeBody = includeBody,
				BodyFormat = ParseBodyFormat(bodyFormat),
				Sort = oldestFirst ? EmailSort.ReceivedAscending : EmailSort.ReceivedDescending,
				Top = maxResults,
				Skip = skip,
				// §22: bound for scanning a client-side predicate (toContains/…) across the mailbox. Operator-
				// tunable; the caller narrows (folder/date/from) to keep the match set inside it. Never arbitrary.
				MaxClientScan = configuration.GetValue("O365:Email:ClientFilterScanCap", 3000),
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
			if (!string.IsNullOrWhiteSpace(withCategories)) filtersApplied["withCategories"] = withCategories;
			if (!string.IsNullOrWhiteSpace(excludeCategories)) filtersApplied["excludeCategories"] = excludeCategories;
			if (chain) filtersApplied["conversationId"] = conversationId!.Trim();
			if (oldestFirst) filtersApplied["oldestFirst"] = true;
			if (includeCc) filtersApplied["includeCc"] = true;
			if (includeBody) { filtersApplied["includeBody"] = true; filtersApplied["bodyFormat"] = bodyFormat; }
			if (bodyMaxChars > 0) filtersApplied["bodyMaxChars"] = bodyMaxChars;

			EchoScope(filtersApplied, includeSpamAndTrash, !chain && string.IsNullOrWhiteSpace(folder) && !includeSpamAndTrash);

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

	[McpServerTool(Name = "email_read_delta")]
	[Description("Folder-scoped incremental sync — the 'what changed' read. Returns `changed` (added/updated "
		+ "lean messages) and `removedIds` (deleted or moved out of the folder) since `deltaToken`, plus "
		+ "`nextDeltaToken`. First call (no token) baselines the folder: page through with the returned token "
		+ "while `hasMore` is true, then persist `nextDeltaToken`; later calls with it return only changes. "
		+ "YOU own the token (this MCP does not persist it), keyed by mailbox+folder. If the token expired or "
		+ "the folder was rearranged, the call transparently re-baselines and sets `resynced=true` — treat "
		+ "that as normal, not an error. `folder` is a well-known name (inbox, junkemail, …) or a folder id.")]
	public static async Task<CallToolResult> Delta(
		IConfiguration configuration,
		HttpClient httpClient,
		string folder,
		string? deltaToken = null,
		string? mailbox = null,
		int maxResults = 50,
		bool verbose = false,
		bool includePreview = true,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var d = await emailService.GetDeltaAsync(resolvedMailbox, folder, deltaToken, maxResults, cancellationToken);

			var payload = new
			{
				mailbox = resolvedMailbox,
				folder,
				changedCount = d.Changed.Count,
				removedCount = d.RemovedIds.Count,
				hasMore = d.More,
				resynced = d.Resynced,
				nextDeltaToken = d.NextToken,
				changed = ProjectRows(d.Changed, RedactPreviews(configuration), verbose, 0, includePreview),
				removedIds = d.RemovedIds,
			};
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
				Content = [new TextContentBlock { Text = $"Error running delta: {ex.Message}" }]
			};
		}
	}

	[McpServerTool(Name = "email_read_facets")]
	[Description("Count messages matching a filter, optionally grouped — census ('how many, by whom, in which "
		+ "period') without paging the corpus back to you. Same filter grammar as email_read_list (folder, "
		+ "isRead, isFlagged, fromContains, subjectContains, toContains, recipientContains (To or Cc), since, before, includeSpamAndTrash). "
		+ "`groupBy` (optional): senderDomain | senderAddress | year | month | folder | isRead — OMIT for just "
		+ "the total. The MCP pages the matched set internally (bounded by `maxScan`, default 5000) and returns "
		+ "ONLY counts. Returns {matched, scanned, truncated, facets:[{key,count}]} (truncated=true means the "
		+ "scan hit maxScan — counts are a floor). Also the cheap 'did that sweep finish?' check. Targets "
		+ "`mailbox` (UPN) or the default. O365 only.")]
	public static async Task<CallToolResult> Facets(
		IConfiguration configuration,
		HttpClient httpClient,
		string? groupBy = null,
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
		int maxScan = 5000,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (ParseCutoff(since, nameof(since), out var receivedAfter) is { } e1) return InputError(e1);
			if (ParseCutoff(before, nameof(before), out var receivedBefore) is { } e2) return InputError(e2);
			if (maxScan <= 0) return InputError("`maxScan` must be > 0.");

			var predicates = new List<EmailPredicate>();
			if (!string.IsNullOrWhiteSpace(fromContains)) predicates.Add(new EmailPredicate(EmailField.From, EmailOperator.Contains, fromContains));
			if (!string.IsNullOrWhiteSpace(subjectContains)) predicates.Add(new EmailPredicate(EmailField.Subject, EmailOperator.Contains, subjectContains));
			if (!string.IsNullOrWhiteSpace(toContains)) predicates.Add(new EmailPredicate(EmailField.To, EmailOperator.Contains, toContains));
			if (!string.IsNullOrWhiteSpace(recipientContains)) predicates.Add(new EmailPredicate(EmailField.Recipient, EmailOperator.Contains, recipientContains));

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var query = new EmailQuery
			{
				Folder = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim(),
				IsRead = isRead,
				IsFlagged = isFlagged,
				Received = receivedAfter is not null || receivedBefore is not null ? new DateRange { After = receivedAfter, Before = receivedBefore } : null,
				Predicates = predicates,
				ExcludeDeletedAndJunk = string.IsNullOrWhiteSpace(folder) && !includeSpamAndTrash,
			};
			var result = await emailService.GetFacetsAsync(resolvedMailbox, query, string.IsNullOrWhiteSpace(groupBy) ? null : groupBy.Trim(), maxScan, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, groupBy = string.IsNullOrWhiteSpace(groupBy) ? null : groupBy.Trim(), result };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error computing facets: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_read_search")]
	[Description("Full-text SEARCH across the mailbox (or a `folder`) — Graph $search over from/subject/body/"
		+ "recipients, ranked by relevance. Use it for 'has this sender ever written about X' and body-scanning "
		+ "without paging the whole mailbox — matter reconstruction where history predates your sample. `search` "
		+ "is the query text; `folder` scopes it (well-known name or id; default whole mailbox); `maxResults` "
		+ "caps the hit list. Returns the same lean list shape as email_read_list. NOTE: $search cannot combine "
		+ "with the structured filters (isRead, dates, categories) — use email_read_list for those. Targets "
		+ "`mailbox` (UPN) or the default.")]
	public static async Task<CallToolResult> Search(
		IConfiguration configuration,
		HttpClient httpClient,
		string search,
		string? folder = null,
		int maxResults = 25,
		bool verbose = false,
		bool includePreview = true,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(search)) return InputError("`search` is required.");
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var messages = await emailService.SearchAsync(resolvedMailbox, search.Trim(), string.IsNullOrWhiteSpace(folder) ? null : folder.Trim(), maxResults, cancellationToken);
			var filtersApplied = new Dictionary<string, object?> { ["search"] = search.Trim() };
			if (!string.IsNullOrWhiteSpace(folder)) filtersApplied["folder"] = folder.Trim();
			return LeanList(messages, maxResults, 0, RedactPreviews(configuration), resolvedMailbox, verbose, filtersApplied, 0, includePreview);
		}
		catch (Exception ex)
		{
			return InputError($"Error searching messages: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_read_body")]
	[Description("Get one email in full — metadata (incl. ccRecipients), the complete message body, and "
		+ "optionally a named extended property — by its messageId (from a list tool). This is the only "
		+ "tool that returns the body; the list tools carry just a bodyPreview. Targets `mailbox` (UPN) or "
		+ "the configured default when omitted — pass the same mailbox the message id came from. bodyFormat "
		+ "is 'text' (default, HTML stripped — best for reading/triage) or 'html'. To read back your own "
		+ "ledger record, pass propertySetGuid + propertyName and its value returns as `propertyValue`.")]
	public static async Task<CallToolResult> GetEmail(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageId,
		string? mailbox = null,
		string bodyFormat = "text",
		string? propertySetGuid = null,
		string? propertyName = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var format = string.Equals(bodyFormat, "html", StringComparison.OrdinalIgnoreCase)
				? EmailBodyFormat.Html
				: EmailBodyFormat.Text;

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var message = await emailService.GetMessageAsync(resolvedMailbox, messageId, format, propertySetGuid, propertyName, cancellationToken);

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
		+ "content. Use the returned `id` with email_read_attachment to fetch the bytes. `messageId` is a "
		+ "message id from a list/body call; targets `mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> ListAttachments(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageId,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
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
		+ "(O365:Auth:MaxInlineAttachmentBytes, default 30 MB) — for anything larger, pass `saveToDirectory` and "
		+ "the file is written to disk and its `savedPath` returned instead (avoids flooding the context). "
		+ "`saveToDirectory` always writes to disk regardless of size. Targets `mailbox` (UPN) or the configured "
		+ "default. Returns null-not-found if the id isn't a downloadable (file) attachment.")]
	public static async Task<CallToolResult> GetAttachment(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageId,
		string attachmentId,
		string? mailbox = null,
		string? saveToDirectory = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var att = await emailService.GetAttachmentAsync(resolvedMailbox, messageId, attachmentId, cancellationToken);
			if (att is null)
				return InputError($"Attachment '{attachmentId}' not found on the message, or it has no downloadable content (e.g. an item/reference attachment).");
			long maxInline = configuration.GetValue("O365:Auth:MaxInlineAttachmentBytes", 30L * 1024 * 1024);
			return AttachmentResult(resolvedMailbox, messageId, att, saveToDirectory, maxInline);
		}
		catch (Exception ex)
		{
			return InputError($"Error getting attachment: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_categorize")]
	[Description("Merge categories onto one or more messages — the queryable read-path state. `messageIds` "
		+ "is comma-separated; `categories` is comma-separated. Categories are merged, never overwritten "
		+ "(the human's own categories are preserved). Batched: returns a per-item {success, error} result. "
		+ "Targets `mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> Categorize(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageIds,
		string categories,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var results = await emailService.CategorizeAsync(resolvedMailbox, SplitCsv(messageIds), SplitCsv(categories), cancellationToken);
			return BatchResult(resolvedMailbox, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error categorizing email: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_property")]
	[Description("Write a named extended property onto one or more messages — the caller's own structured "
		+ "record (e.g. a triage ledger entry). `messageIds` is comma-separated; you supply propertySetGuid, "
		+ "name, and value (typically a JSON string); the MCP holds no vocabulary of its own. Batched: "
		+ "returns a per-item {success, error} result. Targets `mailbox` (UPN) or the configured default. "
		+ "The split: categories (email_edit_categorize) are the queryable read-path state; this property is "
		+ "the on-demand ledger — not returned by list tools, not a read filter (read it via email_read_body).")]
	public static async Task<CallToolResult> SetProperty(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageIds,
		string propertySetGuid,
		string name,
		string value,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var results = await emailService.SetExtendedPropertyAsync(resolvedMailbox, SplitCsv(messageIds), propertySetGuid, name, value, cancellationToken);
			return BatchResult(resolvedMailbox, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error setting property: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_edit_move")]
	[Description("Move one or more messages to a folder — the FILE disposition. `messageIds` is "
		+ "comma-separated; `destinationFolderId` is a folder id (from email_read_folders) or a well-known "
		+ "name (inbox, archive, …). Batched: per-item result carries the message's NEW id (Graph reassigns "
		+ "it on move) and its previous fromFolderId — together the undo record. Targets `mailbox` (UPN) or "
		+ "the configured default.")]
	public static async Task<CallToolResult> Move(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageIds,
		string destinationFolderId,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
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
		+ "tool's. Batched: per-item {success, error}. Targets `mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> SetReadState(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageIds,
		bool isRead,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var results = await emailService.SetReadStateAsync(resolvedMailbox, SplitCsv(messageIds), isRead, cancellationToken);
			return BatchResult(resolvedMailbox, results);
		}
		catch (Exception ex)
		{
			return InputError($"Error setting read state: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_delete")]
	[Description("Soft-delete one or more messages — move them to Deleted Items (30-day recovery). "
		+ "`messageIds` is comma-separated. There is NO hard-delete: deletion is always recoverable, by "
		+ "design. Batched: per-item result carries the message's NEW id and previous fromFolderId (so a "
		+ "wrongful delete can be moved back). Targets `mailbox` (UPN) or the configured default.")]
	public static async Task<CallToolResult> Delete(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageIds,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
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
		+ "TRUE: returns {wouldAffect, sample of matched subjects} and writes nothing — pass dryRun:false as a "
		+ "second, deliberate call to move. A filter needs a narrowing predicate beyond folder. Returns "
		+ "{matched, excluded, affected, filtersApplied, results}; per-item carries the new id + previous "
		+ "folder (undo). Targets `mailbox` (UPN) or the default.")]
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
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(destinationFolderId)) return InputError("`destinationFolderId` is required.");
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var (targets, matched, excluded, filtersApplied, error) = await ResolveBulkAsync(
				emailService, resolvedMailbox, fromContains, subjectContains, toContains, recipientContains, since, before, isRead, isFlagged, folder, includeSpamAndTrash, excludeIds, maxAffected, configuration.GetValue("O365:Email:ClientFilterScanCap", 3000), cancellationToken);
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
		+ "matching a filter to Deleted Items (recoverable; there is NO hard delete). Same filter grammar as "
		+ "email_read_list — PREVIEW with email_read_list, then apply the identical filter. `excludeIds` "
		+ "(comma-separated) carves keepers out: match the group, keep the ones you want, delete the rest. "
		+ "`maxAffected` is REQUIRED and bounds the blast radius: if the filter matches more than that after "
		+ "exclusions, the call is REFUSED. `dryRun` DEFAULTS TO TRUE: returns {wouldAffect, sample of matched "
		+ "subjects} and writes nothing — pass dryRun:false as a second, deliberate call to delete. A filter "
		+ "needs a narrowing predicate beyond folder. Returns {matched, excluded, affected, filtersApplied, "
		+ "results}; per-item carries the new id + previous folder (undo). Targets `mailbox` (UPN) or the default.")]
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
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var (targets, matched, excluded, filtersApplied, error) = await ResolveBulkAsync(
				emailService, resolvedMailbox, fromContains, subjectContains, toContains, recipientContains, since, before, isRead, isFlagged, folder, includeSpamAndTrash, excludeIds, maxAffected, configuration.GetValue("O365:Email:ClientFilterScanCap", 3000), cancellationToken);
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
		+ "pass dryRun:false to apply. A filter needs a narrowing predicate beyond folder. Read state is the "
		+ "human's signal, so WHEN to touch it is caller policy. Targets `mailbox` (UPN) or the default.")]
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
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			// Filter by the CURRENT state (the opposite of the target) so a re-run is a no-op, not a churn:
			// marking read only touches unread mail, and vice versa.
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var (targets, matched, excluded, filtersApplied, error) = await ResolveBulkAsync(
				emailService, resolvedMailbox, fromContains, subjectContains, toContains, recipientContains, since, before, !isRead, isFlagged, folder, includeSpamAndTrash, excludeIds, maxAffected, configuration.GetValue("O365:Email:ClientFilterScanCap", 3000), cancellationToken);
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
		string? excludeIds, int maxAffected, int maxClientScan, CancellationToken ct)
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
			MaxClientScan = maxClientScan > 0 ? maxClientScan : 3000, // §22: bound the client-side (toContains/…) scan
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

	[McpServerTool(Name = "email_edit_draft")]
	[Description("Create a DRAFT reply to a message (saved in Drafts). `messageId` is from a list/body tool; "
		+ "`comment` is your reply text, prepended above the quoted original. `replyAll` addresses all original "
		+ "recipients (default false: reply to the sender only). This composes a draft and does not send — to "
		+ "send it, use email_send_draft, which is gated by O365:Auth:AllowSend (off by default). Returns "
		+ "{id, subject, parentFolderId, webLink}. Targets `mailbox` (UPN) or the default. O365 only.")]
	public static async Task<CallToolResult> CreateDraft(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageId,
		string? comment = null,
		bool replyAll = false,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(messageId)) return InputError("`messageId` is required.");
			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var draft = await emailService.CreateReplyDraftAsync(resolvedMailbox, messageId, comment, replyAll, cancellationToken);
			var payload = new { mailbox = resolvedMailbox, draft };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error creating draft: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_send_draft")]
	[Description("Send an existing DRAFT by its `messageId` (from email_edit_draft, or a message id in the "
		+ "Drafts folder) — Graph sends it as-is. Composes with email_edit_draft: draft, optionally review, then "
		+ "send. Named under email_send* so it is permission-gated alongside email_send. Sending is OFF by "
		+ "default and must be enabled by the operator with O365:Auth:AllowSend=true. Needs the Mail.Send scope "
		+ "(or application send permission). Targets `mailbox` (UPN) or the default. O365 only.")]
	public static async Task<CallToolResult> SendDraft(
		IConfiguration configuration,
		HttpClient httpClient,
		string messageId,
		string? mailbox = null,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			// Same operator gate as email_send — sending a draft is still sending outbound, irreversible mail.
			if (!configuration.GetValue("O365:Auth:AllowSend", false))
				return InputError("Sending is disabled by default. An operator must set O365:Auth:AllowSend=true to enable email_send_draft.");
			if (string.IsNullOrWhiteSpace(messageId)) return InputError("`messageId` is required.");

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
			var result = await emailService.SendDraftAsync(resolvedMailbox, messageId, cancellationToken);
			if (!result.Success) return InputError($"Error sending draft: {result.Error}");

			var payload = new { mailbox = resolvedMailbox, sent = true, messageId, sentMessageId = result.MessageId };
			return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, JsonOptions) }] };
		}
		catch (Exception ex)
		{
			return InputError($"Error sending draft: {ex.Message}");
		}
	}

	[McpServerTool(Name = "email_send")]
	[Description("Send a NEW email from `mailbox` (UPN) or the configured default. `to` (and optional `cc`/"
		+ "`bcc`) are comma- or semicolon-separated addresses; `subject` + `body`; `bodyFormat` `text` "
		+ "(default) or `html`. Named alone so it can be permission-gated separately (it sends outbound mail). "
		+ "The MCP is a mechanism — deciding WHETHER and WHAT to send is the caller's policy. Sending is OFF by "
		+ "default and must be enabled by the operator with O365:Auth:AllowSend=true. Needs the "
		+ "Mail.Send scope (delegated) or application send permission. `saveToSentItems` (default true) keeps a "
		+ "copy in Sent Items.")]
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
		UserSecretHelper<O365EmailConfiguration>? typedSecrets = null,
		ISecretHelper? genericSecrets = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			// Operator gate — outbound send is OFF unless an operator opts in (safe default for an
			// irreversible, outward action). Independent of what the caller asks.
			if (!configuration.GetValue("O365:Auth:AllowSend", false))
				return InputError("Sending is disabled by default. An operator must set O365:Auth:AllowSend=true to enable email_send.");

			var recipients = SplitAddresses(to);
			if (recipients.Count == 0)
				return InputError("`to` must contain at least one recipient address.");

			bool isHtml = string.Equals(bodyFormat?.Trim(), "html", StringComparison.OrdinalIgnoreCase);

			var (emailService, resolvedMailbox) = await CreateServiceFromConfigAsync(configuration, typedSecrets, genericSecrets, mailbox, cancellationToken);
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

	// Delegated Graph Mail scopes for interactive sign-in (offline_access is added automatically by Azure.Identity).
	private static async Task<(IEmailService service, string mailbox)> CreateServiceFromConfigAsync(
		IConfiguration configuration,
		UserSecretHelper<O365EmailConfiguration>? typedSecrets,
		ISecretHelper? genericSecrets,
		string? mailboxOverride = null,
		CancellationToken ct = default)
	{
		var config = new O365EmailConfiguration
		{
			TenantId = typedSecrets?.Settings?.TenantId ?? configuration["O365:Auth:TenantId"],
			ClientId = typedSecrets?.Settings?.ClientId ?? configuration["O365:Auth:ClientId"],
			ClientSecret = typedSecrets?.Settings?.ClientSecret ?? configuration["O365:Auth:ClientSecret"],
			ClientCertThumbprint = typedSecrets?.Settings?.ClientCertThumbprint ?? configuration["O365:Auth:ClientCertThumbprint"],
			ClientCertPath = typedSecrets?.Settings?.ClientCertPath ?? configuration["O365:Auth:ClientCertPath"],
			ClientCertPassword = typedSecrets?.Settings?.ClientCertPassword ?? configuration["O365:Auth:ClientCertPassword"],
			DefaultMailbox = typedSecrets?.Settings?.DefaultMailbox ?? configuration["O365:Auth:DefaultMailbox"],
			AuthMode = typedSecrets?.Settings?.AuthMode ?? configuration["O365:Auth:AuthMode"],
			Scopes = typedSecrets?.Settings?.Scopes ?? configuration.GetSection("O365:Auth:Scopes").Get<string[]>(),
				AdditionalScopes = typedSecrets?.Settings?.AdditionalScopes ?? configuration.GetSection("O365:Auth:AdditionalScopes").Get<string[]>(),
		};

		// Per-call `mailbox` wins over the configured default. The credential is the same either way
		// (the mailbox is just the /users/{mailbox} URL segment); reaching another user's or a shared
		// mailbox needs Full Access + Mail.ReadWrite.Shared, which the delegated scope set requests.
		string? mailbox = string.IsNullOrWhiteSpace(mailboxOverride) ? config.DefaultMailbox : mailboxOverride.Trim();
		if (string.IsNullOrEmpty(mailbox))
			throw new InvalidOperationException("No mailbox: pass a `mailbox` (UPN) or configure O365:Auth:DefaultMailbox.");

		var graphClient = await O365GraphClientBuilder.BuildAsync(config, mailbox, ct);
		var service = new GraphEmailService(graphClient);
		return (service, mailbox);
	}

	// Compact + null-omitting: lists carry no message body (BodyContent is null), so drop it from the wire.
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
	};

	// Operator toggle (default on): scrub BodyPreview secrets before they reach the model. Read from
	// configuration, not the per-call args — a model must not be able to turn its own safety net off.
	private static bool RedactPreviews(IConfiguration configuration)
		=> configuration.GetValue("O365:Auth:RedactBodyPreview", true);

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

	// Split a comma-separated tool argument (e.g. id/category list) into a trimmed, non-empty list.
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
	// base64 under the caller-configured inline cap (O365:Auth:MaxInlineAttachmentBytes).
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
			return InputError($"Attachment '{att.Name}' is {att.Content.LongLength:N0} bytes (over the {maxInlineBytes:N0}-byte inline cap, O365:Auth:MaxInlineAttachmentBytes); pass saveToDirectory to write it to disk instead of returning base64.");

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
	/// <c>nextSkip</c> is the offset to pass back as <c>skip</c> to fetch it. Graph offset-paging has
	/// no exact total, so <c>hasMore</c> can be a false positive on the final full page — a follow-up
	/// call then returns count 0.
	/// </summary>
	// §13 (parity with the IMAP tool): echo the scope the query actually ran with. includeSpamAndTrash is
	// scope-affecting, and the Deleted/Junk exclusion is a default the caller never passed — both must be
	// visible so a caller can assert the scope it got.
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

	// Project rows for the wire. Default (lean) drops the long base64 fields that dominate payload —
	// webLink, conversationId, internetMessageId, parentFolderId — which an agent rarely reads at triage
	// time (~4x overshoot, #520); `verbose` keeps the full shape for dedup/undo/threading. BodyPreview is
	// scrubbed of bearer-shaped secrets either way (#508). The detail is always available via email_read_body.
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
			m.Categories,
			m.ToRecipients,
			CcRecipients = m.CcRecipients.Count > 0 ? m.CcRecipients : null,
			// §19: BodyPreview is ~47% of a lean row and census work never reads it — drop it when
			// includePreview is false (null → omitted by the serializer).
			BodyPreview = includePreview ? Preview(m) : null,
			// Kept only when includeBody populated it — otherwise null and dropped by the serializer, so the
			// default row stays lean. Capped to bodyMaxChars when set.
			BodyContent = Cap(m.BodyContent),
			BodyContentType = m.BodyContent is not null ? m.BodyContentType : null,
		}).ToList<object>();
	}
}
