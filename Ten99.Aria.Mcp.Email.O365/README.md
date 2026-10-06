# Ten99.Aria.Mcp.Email.O365

ARIA's Microsoft 365 (Outlook / Microsoft Graph) email MCP server. A **read/triage surface** over a
mailbox for Claude Desktop/Code and ARIA agents — the tooling half; triage *logic* belongs in an agent.

Runs via `dnx` (packaged as a dotnet tool).

## Running it

Requires the .NET 10 SDK; the first run fetches the package from NuGet (`--yes` accepts the prompt). Configure it with the `O365:Auth` settings (see **Auth** below) — here via env vars on the MCP-client entry.

- **macOS / Linux:** `dotnet dnx Ten99.Aria.Mcp.Email.O365 --yes`
- **Windows:** `dnx Ten99.Aria.Mcp.Email.O365 --yes` (the standalone `dnx` command ships on the PATH with the SDK on Windows; on macOS/Linux use `dotnet dnx`)

```json
{
  "mcpServers": {
    "email-o365": {
      "command": "dotnet",
      "args": ["dnx", "Ten99.Aria.Mcp.Email.O365", "--yes"],
      "env": {
        "O365__Auth__AuthMode": "interactive",
        "O365__Auth__ClientId": "<your-entra-app-client-id>",
        "O365__Auth__DefaultMailbox": "you@your-org.com"
      }
    }
  }
}
```

(On Windows you can set `"command": "dnx"` and drop the leading `"dnx"` arg.)

## Tools
- `email_health` — connection + mailbox access check
- `email_read_mailboxes` — list the configured mailboxes and **probe** each for access (reachable? unread/total + displayName, or the error), plus the active auth mode — so a session knows which mailboxes it can act on up front instead of discovering by failing. Candidates = `O365:Auth:DefaultMailbox` + operator-declared `O365:Auth:Mailboxes` (Graph has no reliable "shared mailboxes I can access" enumeration, so the estate is operator-declared)
- `email_read_folders` — folders with unread/total counts; **top-level by default**, `parentFolderId` to drill down one level, `recursive:true` for the whole tree
- `email_read_list` — general list: scope to a `folder` (well-known name or id) or whole mailbox; filter by `isRead`/`isFlagged`/from/subject/date-window/`withCategories`/`excludeCategories`/`conversationId` (walk a thread across all folders incl. Sent); `oldestFirst` for backlog order; `includeCc` to add CC. `email_read_unread`/`email_read_flagged` are sugar over this. Rows are **lean by default** (drops the long base64 ids — `webLink`/`conversationId`/`internetMessageId`/`parentFolderId`); pass `verbose:true` for the full shape, or fetch detail via `email_read_body`. Unread-Junk audit = `folder:"junkemail", isRead:false`.
- `email_read_delta` — folder-scoped incremental sync: `changed` + `removedIds` since `deltaToken`, plus `nextDeltaToken`. First call baselines (page while `hasMore`); the caller owns the token (keyed by mailbox+folder). Stale token → transparent resync (`resynced=true`).
- `email_read_unread` — unread mail across all folders, newest first; excludes Deleted Items + Junk by default (`includeSpamAndTrash` to include them). Optional `fromContains`/`subjectContains`, a `since`/`before` date window (ISO-8601), and `excludeCategories` (comma-separated). Lean list + paging.
- `email_read_flagged` — flagged (follow-up) mail; same Junk/Deleted exclusion + `since`/`before` + `excludeCategories`; lean list + paging
- `email_read_body` — one message in full (metadata incl. `ccRecipients` + body) by id; `bodyFormat` `text` (default) or `html`. Pass `propertySetGuid`+`propertyName` to read back a named extended property as `propertyValue`.
- `email_read_attachments` — list a message's attachments (metadata only: id, name, contentType, size, isInline)
- `email_read_attachment` — fetch one attachment by id; returns base64 up to the inline cap (`O365:Auth:MaxInlineAttachmentBytes`, default 30 MB), or pass `saveToDirectory` to write it to disk and get back a `savedPath` (any size)
- `email_edit_categorize` — merge categories onto messages (batched; the queryable read-path state)
- `email_edit_property` — write a named extended property onto messages (batched; the caller's ledger: `propertySetGuid`, `name`, `value`)
- `email_edit_move` — move messages to a folder (batched); returns each message's **new id** + **fromFolderId** (the undo record)
- `email_edit_read` — set read/unread on messages (batched, both directions)
- `email_delete` — **soft-delete** messages → Deleted Items, 30-day recovery (batched). No hard-delete exists.
- `email_send` — send a **new** outbound email (`to`/`cc`/`bcc`, `subject`, `body`, `bodyFormat`). Named alone so it's gated separately. Mechanism only — the caller owns whether/what to send; the operator can hard-disable with `O365:Auth:AllowSend=false`. Needs `Mail.Send`.

Write tools are **batched**: `messageIds` is comma-separated and the result is a per-item `{success, error}` envelope (`{count, succeeded, failed, results}`), so a partial failure reports which ids failed rather than all-or-nothing.

## Tool naming & grouped permissions
Tools are class-segmented so a client can grant by group (Claude Code globs after `mcp__<server>__`):
- **`email_read_*`** — reads (no side effects) · **`email_edit_*`** — mutations · **`email_delete`** — destructive (soft-delete only), named alone so it's gated separately · **`email_send`** — outbound send, named alone (also killable via `O365:Auth:AllowSend=false`) · `email_health` — diagnostic.

```
allow  mcp__email-o365__email_read_*     # all reads
allow  mcp__email-o365__email_edit_*     # all safe writes
ask    mcp__email-o365__email_delete     # gate the destructive one
ask    mcp__email-o365__email_send       # gate outbound send
```

## Vocabulary-agnostic
The MCP holds **no vocabulary of its own** — no ARIA-specific categories or property schema. Categories and the extended property are pure mechanism; the caller (agent) supplies all tokens. Filter/exclude by any category, and write any property. ARIA's own scheme lives in the agent (see the wiki *Email Triage: Categorisation & Ledger Convention*), so other consumers can use this MCP without inheriting ARIA's conventions.

## Mailbox targeting
Every data tool takes an optional `mailbox` (a UPN), defaulting to `O365:Auth:DefaultMailbox` when omitted. The credential is the same regardless of mailbox (it's just the `/users/{mailbox}` segment); reaching another user's or a **shared** mailbox needs Full Access + `Mail.ReadWrite.Shared` (in the delegated scope set), so one interactive login covers the signed-in mailbox plus every in-tenant shared mailbox it can access. List results carry the resolved `mailbox` on the response envelope, plus `internetMessageId` (stable dedup key across mailbox copies) and `parentFolderId` per message.

## Auth (`authMode`)
Three modes (credentials come from the shared `Ten99.Aria.Identity.EntraId` factory):
- **`servicePrincipal`** (default; `clientcredential` accepted as an alias) — app-only. Needs `tenantId`,
  `clientId`, a **credential**, and `defaultMailbox` (admin-consented app). The credential is either a
  **`clientSecret`** or a **client certificate** — `clientCertThumbprint` (CurrentUser/LocalMachine `My`
  store) or `clientCertPath` (PFX/PKCS#12, or a PEM with cert+key; `clientCertPassword` for an encrypted
  PFX). A certificate wins over a secret when both are set. Uses application permissions (`Mail.ReadWrite`
  **application** role) via `/.default`.
- **`managedIdentity`** — app-only via Azure Managed Identity, **no secret to manage**. System-assigned
  needs only `defaultMailbox` (a silent login from the runtime's identity — no `clientId`/`tenantId`/secret);
  user-assigned adds the identity's `clientId`. For unattended hosting on Azure.
- **`interactive`** — desktop browser sign-in as the signed-in user (delegated `Mail.ReadWrite`),
  with a persistent token cache so it only prompts once. Needs `clientId` (a multi-tenant app) and
  `defaultMailbox` (your UPN) — **no secret, and no `tenantId`**. `defaultMailbox` is the sign-in
  login hint: Azure AD does home-realm discovery on its domain and routes to the right tenant, so the
  UPN alone selects the correct account/mailbox (that's what makes multi-tenancy work without a
  `tenantId`). It also keys a per-account token cache, so several instances for different mailboxes
  run side by side without colliding. Pass a `tenantId` only to pin a specific/guest tenant.

**Scopes** (delegated): `Mail.ReadWrite`, `Mail.ReadWrite.Shared`, `Mail.Send`, and `User.Read` — all
**no admin consent**. `User.Read` reads the signed-in user's own display name via `/me`; other mailboxes'
display names fall back to the address (resolving those needs the directory scope `User.ReadBasic.All`,
deferred to the estate name-resolution feature that actually needs it).

Config lives under the **`O365:Auth`** section (like the ADO MCP's `Ado:Auth`). Supply it via:
- **env** — `O365__Auth__ClientId`, `O365__Auth__TenantId`, `O365__Auth__DefaultMailbox`, `O365__Auth__Mailboxes` (estate list for `email_read_mailboxes`), `O365__Auth__AuthMode` (+ `O365__Auth__ClientSecret` **or** `O365__Auth__ClientCertThumbprint`/`ClientCertPath`/`ClientCertPassword` for app-only)
- **CLI** — friendly switches map to those keys: `--clientId`, `--tenantId`, `--defaultMailbox`, `--mailboxes`, `--authMode`, `--clientSecret`, `--clientCertThumbprint`, `--clientCertPath`, `--clientCertPassword`
- **user secrets** — the `O365:Auth` section

### Requested scopes (`O365__Auth__Scopes`)
Interactive requests `Mail.ReadWrite`, `Mail.ReadWrite.Shared`, `Mail.Send`, `User.Read` by default.
Override with `O365__Auth__Scopes` (space- or comma-separated; short names or full URLs) to request a
**leaner** set — useful in restricted **client tenants** where an admin must consent and a smaller ask is
easier to approve. For triaging only your own mailbox, `O365__Auth__Scopes="Mail.ReadWrite User.Read"`
drops the shared-mailbox and send permissions. Omit it to keep the full default set.

Secrets are never returned by any tool. `BodyPreview` on list results is scrubbed of bearer-shaped
secrets (magic/reset links, one-time codes) by default — previews flow to a model on every page. Disable
with `O365__Auth__RedactBodyPreview=false` (operator toggle; not a per-call, model-controlled flag).

**Sending** (`email_send`) is **off by default** — a deliberately safe default for an irreversible,
outward action. An operator must opt in with `O365__Auth__AllowSend=true`. Once enabled the MCP stays a
mechanism and does not itself decide whether to send — that policy belongs to the agent; this is the
operator's on/off gate, not a per-call/model-reachable flag.
