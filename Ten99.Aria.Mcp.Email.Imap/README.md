# Ten99.Aria.Mcp.Email.Imap

ARIA's IMAP/SMTP email MCP server (MailKit backend). A **read/triage surface** over any IMAP mailbox
for Claude Desktop/Code and ARIA agents — the tooling half; triage *logic* belongs in an agent.

This is the **second `IEmailService` backend** behind the shared, provider-neutral contract
`Ten99.Aria.Integration.Graph.Email` (despite the `Graph` in the name, it is the neutral contract). It
mirrors the [`Ten99.Aria.Mcp.Email.O365`](../Ten99.Aria.Mcp.Email.O365/README.md) tool surface so an
agent written against one works against the other — with the honest divergences noted below.

Runs via `dnx` (packaged as a dotnet tool).

## Running it

Requires the .NET 10 SDK; the first run fetches the package from NuGet (`--yes` accepts the prompt). Configure it with the `Imap:Auth` / `Imap:Smtp` settings (see **Auth** below) — here via env vars on the MCP-client entry.

- **macOS / Linux:** `dotnet dnx Ten99.Aria.Mcp.Email.Imap --yes`
- **Windows:** `dnx Ten99.Aria.Mcp.Email.Imap --yes` (the standalone `dnx` command ships on the PATH with the SDK on Windows; on macOS/Linux use `dotnet dnx`)

```json
{
  "mcpServers": {
    "email-imap": {
      "command": "dotnet",
      "args": ["dnx", "Ten99.Aria.Mcp.Email.Imap", "--yes"],
      "env": {
        "Imap__Auth__Host": "imap.your-provider.com",
        "Imap__Auth__Port": "993",
        "Imap__Auth__UseSsl": "true",
        "Imap__Auth__Username": "you@your-org.com",
        "Imap__Auth__Password": "<app-password>",
        "Imap__Smtp__Host": "smtp.your-provider.com",
        "Imap__Smtp__Port": "587"
      }
    }
  }
}
```

(On Windows you can set `"command": "dnx"` and drop the leading `"dnx"` arg.)

## Tools
- `email_health` — connection + mailbox access check. **Also reports the negotiated IMAP capabilities**
  (MOVE, SPECIAL-USE, SORT, CONDSTORE/QRESYNC, X-GM-EXT-1) and which substitutes are live, so the agent
  knows what this server can actually do.
- `email_read_folders` — folders with unread/total counts; **top-level by default**, `parentFolderId` to
  drill down one level, `recursive:true` for the whole tree. `childFolderCount` is a **0/1 has-children
  flag** on IMAP (the protocol advertises has-children, not an exact count).
- `email_read_list` — general list: scope to a `folder` (well-known name or path) or whole mailbox;
  filter by `isRead`/`isFlagged`/`fromContains`/`subjectContains`/date-window; `oldestFirst` for backlog
  order; `includeCc` to add CC. `email_read_unread`/`email_read_flagged` are sugar over this. Rows are
  **lean by default** (drops `internetMessageId`/`parentFolderId`); pass `verbose:true` for the full
  shape, or fetch detail via `email_read_body`. Unread-Junk audit = `folder:"junkemail", isRead:false`.
- `email_read_unread` — unread mail across all folders, newest first; excludes Deleted Items + Junk by
  default (`includeSpamAndTrash` to include). Optional `fromContains`/`subjectContains`, a `since`/`before`
  date window (ISO-8601). Lean list + paging.
- `email_read_flagged` — flagged mail; same date window + Junk/Deleted exclusion; lean list + paging.
- `email_read_body` — one message in full (metadata incl. `ccRecipients` + body) by id; `bodyFormat`
  `text` (default, HTML stripped) or `html`.
- `email_read_attachments` — list a message's attachments (metadata only: id = MIME part specifier, name,
  contentType, size, isInline)
- `email_read_attachment` — fetch one attachment by id; returns base64 up to the inline cap
  (`Imap:Auth:MaxInlineAttachmentBytes`, default 30 MB), or pass `saveToDirectory` to write it to disk
  and get back a `savedPath` (any size)
- `email_edit_move` — move messages to a folder (batched); returns each message's **new id** +
  **fromFolderId** (the undo record). Uses the MOVE extension where advertised, else COPY + expunge.
- `email_edit_read` — set read/unread on messages (batched, both directions).
- `email_delete` — **soft-delete** messages → move to the Trash/Deleted-Items folder (batched). There is
  **no hard-delete** path, by design. (Recovery window is the server's Trash retention, not a guaranteed
  30 days — see divergences.)
- `email_send` — send a **new** outbound email via SMTP (`to`/`cc`/`bcc`, `subject`, `body`, `bodyFormat`).
  Named alone so it's gated separately. Mechanism only — the caller owns whether/what to send; **off by
  default**, an operator opts in with `Imap:Auth:AllowSend=true`.

Write tools are **batched**: `messageIds` is comma-separated and the result is a per-item
`{success, error}` envelope (`{count, succeeded, failed, results}`), so a partial failure reports which
ids failed rather than all-or-nothing.

### Message ids
Tool ids are encoded **`folder:UIDVALIDITY:UID`** — a bare IMAP UID is meaningless without its folder and
UIDVALIDITY, and UIDs change on move. The stable RFC5322 Message-ID is surfaced as `internetMessageId`
(the dedup key across mailbox copies).

## Tool naming & grouped permissions
Tools are class-segmented so a client can grant by group (Claude Code globs after `mcp__<server>__`):
- **`email_read_*`** — reads (no side effects) · **`email_edit_*`** — mutations · **`email_delete`** —
  destructive (soft-delete only), named alone · **`email_send`** — outbound send, named alone (off by
  default; enable via `Imap:Auth:AllowSend=true`) · `email_health` — diagnostic.

```
allow  mcp__email-imap__email_read_*     # all reads
allow  mcp__email-imap__email_edit_*     # all safe writes
ask    mcp__email-imap__email_delete     # gate the destructive one
ask    mcp__email-imap__email_send       # gate outbound send
```

## Vocabulary-agnostic
The MCP holds **no vocabulary of its own** — it moves any folder, sends any message; ARIA's own triage
scheme lives in the agent, exactly as with the O365 backend.

## Mailbox targeting
Every data tool takes an optional `mailbox`, defaulting to `Imap:Auth:DefaultMailbox` when omitted.
**IMAP is single-account per credential:** a login authenticates exactly one account — there is **no
`/users/{upn}` re-targeting** the way Graph offers. So `mailbox` selects the configured account/default
(and rides the response envelope + the `email_send` From); reaching a *different* mailbox means a
different credential/instance (a per-account credential map is the natural estate extension). List results
carry the resolved `mailbox` on the response envelope.

## Auth (`authMode`)
- **`basic`** (default) — LOGIN/PLAIN over TLS with `username` + `password` (or an app-specific password).
  Simple; works with self-hosted Dovecot, Fastmail app passwords, and legacy servers. This is the only
  mode implemented today.
- **`xoauth2`** — SASL OAuth2 bearer, the modern path required by Gmail and Exchange-Online-over-IMAP
  (basic auth is disabled tenant-wide by Microsoft). **A documented P1 follow-up** — the seam is in place
  (`ImapConnection`), but it is not yet implemented; selecting it today returns a clear error.

Config lives under the **`Imap:Auth`** section (SMTP submission under **`Imap:Smtp`**). Supply it via:
- **env** — `Imap__Auth__Host`, `Imap__Auth__Port`, `Imap__Auth__UseSsl`, `Imap__Auth__Username`,
  `Imap__Auth__Password`, `Imap__Auth__AuthMode`, `Imap__Auth__DefaultMailbox`; SMTP:
  `Imap__Smtp__Host`, `Imap__Smtp__Port`, `Imap__Smtp__UseStartTls`.
- **CLI** — friendly switches map to those keys: `--host`, `--port`, `--username`, `--password`,
  `--authMode`, `--defaultMailbox`, `--smtpHost`, `--smtpPort`.
- **user secrets** — the `Imap:Auth` / `Imap:Smtp` sections.

## Operator toggles
- **`Imap:Auth:RedactBodyPreview`** (default `true`) — scrub `BodyPreview` on list results of
  bearer-shaped secrets (magic/reset links, one-time codes) before they reach a model. Previews flow to a
  model on every page, so this is default-on transport hygiene. Operator toggle, not a per-call flag.
- **`Imap:Auth:AllowSend`** (default `false`) — `email_send` is **off unless an operator opts in**
  (`Imap:Auth:AllowSend=true`), a safe default for an irreversible, outward action. Once enabled the MCP
  stays a mechanism; policy belongs to the agent. Operator toggle, not a per-call flag.

Secrets are never returned by any tool.

## IMAP divergences (honest capability map)
IMAP matches most of the O365 read/triage surface and **wins on body/full-text search** (`SEARCH BODY`
is server-side), but a few capabilities are substitutes or deferred. This backend does **not** claim
parity it doesn't have:

- **Categories** (`email_edit_categorize`, `withCategories`/`excludeCategories`) — **omitted (P1).** IMAP
  has no equivalent to Outlook categories. The substitute is IMAP keywords (where `PERMANENTFLAGS` allows),
  Gmail `X-GM-LABELS`, or an agent-side sidecar keyed by Message-ID.
- **Extended-property ledger** (`email_edit_property`) — **omitted (P2).** IMAP cannot attach arbitrary
  structured payload to a message; the substitute is an agent-side sidecar store.
- **Delta / incremental sync** (`email_read_delta`) — **omitted (P1).** No Graph-style delta token; the
  substitute is a UID-watermark additions-only delta, or CONDSTORE/QRESYNC for changes+removals where the
  server advertises it (`email_health` reports whether QRESYNC is live).
- **Conversation threading** (`conversationId`) — **not exposed (P2).** No native cross-folder thread id;
  the substitute is Gmail `X-GM-THRID`, the THREAD extension, or References/In-Reply-To header walking.
- **Well-known folders** — resolved via **SPECIAL-USE** attributes where advertised, else a name-guessing
  table (Sent/Trash/Junk/Archive/Drafts across common aliases).
- **Soft-delete recovery window** — `email_delete` moves to Trash (never in-place `\Deleted`+EXPUNGE), so
  the no-hard-delete guarantee holds, but the recovery window is the **server's Trash retention**, not a
  guaranteed 30 days.
- **`bodyPreview`** — IMAP has no preview field, so it is **synthesized** from the text/html part (one
  partial part-fetch per row) and normalised to ~250 chars, then secret-scrubbed like O365.
