# Ten99.Aria.Mcp.Ado

Native C# Azure DevOps MCP server for ARIA — a rewrite of the Microsoft Node `azure-devops-mcp`
fork onto ARIA's own MCP framework, with **first-class multi-org** and **lean-by-default responses**.

Mirrors the `Ten99.Aria.Mcp.Codecks` shape: a static `[McpServerToolType]` tool class delegating to
`Operations/*Operation.cs`, process-static org state (`AdoOrgState`), and `[ConfigurationSection]`
config loaded from the host's `--config` file.

## Why this exists

The upstream MS MCP fights multi-tenancy and is brutally context-hungry (every write returns the
entire work item — all fields, HTML, identities, relations, `_links`). This rewrite makes multi-org
first-class state and treats **lean output as a core feature**. See
`Wiki/Implementation/MCP-Servers/ADO/CSharp-Rewrite-Discussion.md`.

## Transport & auth

- **Raw REST via `HttpClient`** (no azure-devops .NET SDK). Base URL `https://dev.azure.com/{orgName}`,
  `api-version=7.1`.
- **Auth by per-org `authType`**, behind the async `IAdoAuthProvider` seam:
  - `pat` — `Authorization: Basic base64(":" + pat)`; token from `Ado:Pats:<key>`.
  - `interactive` — AAD bearer via `InteractiveBrowserCredential`. Cross-tenant: the org's tenant is
    discovered (`HEAD https://vssps.dev.azure.com/{org}` → `x-vss-resourcetenant`). First sign-in per
    tenant opens a browser once; tokens live in an **encrypted persistent cross-platform cache**
    (MSAL — DPAPI/Keychain/libsecret) and an `AuthenticationRecord` is saved under
    `{home}/.claude/ten99-aria-mcp/auth`, so restarts acquire **silently** until the refresh token
    expires. Requires an Entra app id in `Ado:Auth:ClientId` (see below).
  - `servicePrincipal` — AAD bearer via client credentials (autonomous, no browser). Uses
    `Ado:Auth:ClientId` + `Ado:Auth:ClientSecret`; tenant from `Ado:Auth:TenantId` when set, else the
    per-org discovered tenant. The SP must be a member/guest of the org's tenant and added to the org.
  - `managedIdentity` — AAD bearer via an Azure managed identity (for deployed agents). **System-assigned**
    by default (a silent login — no id or secret); set `Ado:Auth:ManagedIdentityClientId` for a
    **user-assigned** identity. The identity must be added to the target org.

  The three AAD modes share one credential factory (`Ten99.Aria.Identity.EntraId`) with the O365 email
  MCP; all acquire a Bearer token for the Azure DevOps resource (`499b84ac-…/.default`).

### Entra app for interactive auth

Interactive sign-in needs a **multi-tenant, public-client Entra app registration** (once, org-wide):

- **Authentication →** add a **Mobile and desktop** platform with redirect `http://localhost` (loopback);
  set **Allow public client flows = Yes**.
- **Supported account types →** *Accounts in any organizational directory* (multi-tenant, for cross-tenant orgs).
- **API permissions →** *Azure DevOps → user_impersonation* (delegated).
- Put the app's **Application (client) ID** in `Ado:Auth:ClientId` (env `Ado__Auth__ClientId`).

> On Linux the persistent cache uses **libsecret** (`libsecret-1` / a Secret Service keyring). Without
> it, cache persistence fails; the desktop keyring covers this on a normal workstation.

## Configuration

Two layers — a **global org registry** (what orgs exist) and a **per-project default** (which one
starts active). They are deliberately separate.

### Global org registry

Lives at `{home}/.claude/.ado-mcp-orgs.json` (OS-agnostic; override with `--registry <path>`).
Lists orgs only — **no secrets, no default**:

```json
{
  "organizations": {
    "acme": { "orgName": "acme-corp", "authType": "interactive", "domains": ["core", "work-items", "repositories", "wiki"] },
    "contoso": { "orgName": "contoso-devops", "authType": "pat", "domains": ["core", "work-items"] }
  }
}
```

- map key — friendly selector used by `org_switch`.
- `orgName` — the `{org}` in `https://dev.azure.com/{org}`.
- `authType` — `pat`, `interactive`, `servicePrincipal`, or `managedIdentity` (all wired; ADO #423, #431).
- `domains` — API surfaces enabled for the org; **read but not yet enforced** (ADO #429).
- A root `defaultOrganization` (if present, e.g. from the Node fork) is **ignored** — the default is
  per-project, never global.

### Per-project default & settings

The default org is resolved as `Ado:DefaultOrg` through the normal config chain — **no root
fallback**. When unset, the MCP starts with no active org and the first call must
`org_switch`. In precedence order:

1. `--default-org <key>` CLI arg (set in the project `.mcp.json`);
2. env `Ado__DefaultOrg` (Azure Functions-native app setting);

PATs for `authType=pat` orgs are supplied out-of-band (registry stays secret-free) via
`Ado:Pats:<key>` — typically env `Ado__Pats__<key>`. `IndentResponses` and an optional inline
`Ado:Orgs` (legacy/testing) may come from a `--config` json.

Shared `Ado:Auth` settings for the AAD modes (all secret-free in the registry — supply via env/secrets):

| key | used by | notes |
| --- | --- | --- |
| `Ado:Auth:ClientId` | interactive, servicePrincipal | Entra app (client) id |
| `Ado:Auth:ClientSecret` | servicePrincipal | SP secret (env/secret only); or use a cert below |
| `Ado:Auth:ClientCertThumbprint` | servicePrincipal | SP cert from CurrentUser/LocalMachine `My` store |
| `Ado:Auth:ClientCertPath` | servicePrincipal | SP cert file — PFX/PKCS#12 or PEM (cert+key) |
| `Ado:Auth:ClientCertPassword` | servicePrincipal | password for an encrypted PFX (ignored for PEM) |
| `Ado:Auth:TenantId` | servicePrincipal | optional; else discovered per-org |
| `Ado:Auth:ManagedIdentityClientId` | managedIdentity | optional; omit for system-assigned |
| `Ado:Auth:CacheDirectory` | interactive | optional; defaults under `~/.claude/ten99-aria-mcp/auth` |

## Running it

Packaged as a .NET tool, run over stdio via `dnx`. Requires the .NET 10 SDK. The first run fetches the package from NuGet (`--yes` accepts the prompt). `--default-org` picks which registry org is active by default.

- **macOS / Linux:** `dotnet dnx Ten99.Aria.Mcp.Ado --yes -- --default-org acme`
- **Windows:** `dnx Ten99.Aria.Mcp.Ado --yes -- --default-org acme` (the standalone `dnx` command ships on the PATH with the SDK on Windows; on macOS/Linux use `dotnet dnx`)

Register it with an MCP client:

```json
{
  "mcpServers": {
    "ado": {
      "command": "dotnet",
      "args": ["dnx", "Ten99.Aria.Mcp.Ado", "--yes", "--", "--default-org", "acme"]
    }
  }
}
```

(On Windows you can set `"command": "dnx"` and drop the leading `"dnx"` arg.)

The generic host form loads any MCP assembly by path instead: `--mcp "<path>/Ten99.Aria.Mcp.Ado.dll" --usesHttp true --default-org acme`.

## Tools (v1)

Multi-org meta:
- `org_list` — registry orgs + which is active, with `authType`/`domains` (secrets shown only as `hasPat`; `activeOrg` is null until a default/switch selects one).
- `org_switch` — switch active org by key (session-scoped; resets on restart).
- `org_get_current_identity` — who/where am I now (`_apis/connectionData`); also a PAT validity check.

Core:
- `core_list_projects` — lean `{id, name, state, visibility}`.

Work items:
- `wit_query_work_items` — ad-hoc WIQL, hydrated in one shot (no ID-only two-step). `responseType`
  `compact` (default) | `ids` | `full`; `top`, `project`, `fields`, `plainText`.
- `wit_get_work_item` — one item, compact by default; `expand` `none|relations|all` (relations
  trimmed to `{rel,url}`), `fields`, `plainText`.
- `wit_get_work_items_batch` — up to 200 ids, compact list.
- `wit_create_work_item` — JSON-Patch create; returns ack `{id, rev, state, url}`.
- `wit_update_work_item` — JSON-Patch update (only supplied fields); returns ack.

### Lean responses

Compact projection is `{id, type, title, state, parent, url}` — never avatars, identity descriptors,
`_links`, or relations unless explicitly asked. Write ops return `{id, rev, state, url}`. Every read
op accepts a `fields` allowlist (unioned with the compact defaults) and a `plainText` flag that
strips HTML. WIQL supports `responseType: ids | compact | full`.

## Not in v1 (TODO)

- Repos/PRs, wiki, pipelines, test-plans domains.
- **Domain enforcement** — `domains[]` is read and surfaced but not yet used to gate tools (ADO #429).
- **Tenant discovery caching** — HEAD `https://vssps.dev.azure.com/{org}` (expect 404) and read
  `x-vss-resourcetenant`, cache with a TTL. Needed once a tenant-scoped credential provider lands.
- Reparenting on update (`wit_update_work_item` currently *adds* a parent link, doesn't move it).
