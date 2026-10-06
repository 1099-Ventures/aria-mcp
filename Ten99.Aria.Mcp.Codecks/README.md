# Ten99.Aria.Mcp.Codecks

A [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server for [Codecks](https://www.codecks.io), the project-management tool for game teams. It gives an MCP-capable agent a full project-management surface over a Codecks workspace: cards, decks, spaces, tags, milestones, runs, journeys, comments, attachments, and dependencies.

It talks to the current Codecks API (`api.codecks.io`) with a Bearer token. Codecks' API documentation is still thin in places, so parts of this server were built by observing the calls the Codecks web app makes, an approach the Codecks team themselves suggested. It is not an official Codecks product.

## Status

Community-maintained and provided **as-is**, on a best-effort basis. It drives some endpoints that are not formally documented, so a Codecks change could break a tool between releases. Issues and pull requests are welcome; please don't treat it as a supported product with an SLA.

## Install

Packaged as a .NET tool (`aria-codecks-mcp`) on NuGet, run over stdio via `dnx`. You need the .NET 10 SDK.

Register it with your MCP client. On **macOS / Linux** use `dotnet dnx`; on **Windows** the standalone `dnx` command ships on the PATH with the SDK, so you can set `"command": "dnx"` and drop the leading `"dnx"` arg.

```json
{
  "mcpServers": {
    "codecks": {
      "command": "dotnet",
      "args": ["dnx", "Ten99.Aria.Mcp.Codecks", "--yes", "--", "--config", "/path/to/codecks.config.json"]
    }
  }
}
```

Command-line equivalents: **macOS / Linux** `dotnet dnx Ten99.Aria.Mcp.Codecks --yes -- --config /path/to/codecks.config.json`; **Windows** `dnx Ten99.Aria.Mcp.Codecks --yes -- --config C:\path\to\codecks.config.json`.

## Authentication

Codecks issues API tokens in your workspace settings. Two kinds work:

- `cdxat_…` — an **organisation** token (recommended for an agent).
- `cdxut_…` — a **personal** token.

The token is sent as `Authorization: Bearer <token>`; the workspace slug is sent as the `X-Account` header.

### Config file (recommended, supports multiple workspaces)

Pass `--config <path>` to a JSON file. `DefaultOrg` selects the active workspace at startup; `org_switch` changes it at runtime (see Multi-workspace below).

```json
{
  "Codecks": {
    "DefaultOrg": "my-studio",
    "Orgs": [
      { "Key": "my-studio",  "Account": "my-studio",  "Token": "cdxat_…" },
      { "Key": "side-project", "Account": "side-project", "Token": "cdxat_…" }
    ]
  }
}
```

- `Account` is the workspace slug (sent as `X-Account`).
- `Key` is the friendly name you pass to `org_switch`.
- Keep real tokens out of source control; point `--config` at a file outside your repo.

### Single workspace

For a single workspace you can skip the file and pass `--token <cdxat_…> --account <slug>` instead.

## Tools

58 tools, grouped by domain. Reads return a trimmed shape with a top-level `count`; use the matching `*_get_detail` tool for full data.

**Cards** — `card_search`, `card_get_detail`, `card_list_by_deck`, `card_create`, `card_update`
`card_get_detail` accepts either a card UUID or the human card number (`#accountSeq`). `card_create` / `card_update` take tags (`masterTags`), milestone, run/sprint, due date, assignee, effort, and priority.

**Card workflow** — `card_block`, `card_unblock`, `card_request_review`, `card_resolve_review`
Block/review states are driven by resolvable threads; resolving a review can optionally mark the card done.

**Dependencies** — `card_list_dependencies`, `card_add_dependency`, `card_remove_dependency`
Add or remove from either direction (`blocks` / `blockedBy`); a single call updates both cards.

**Comments** — `comment_list`, `comment_add`, `comment_edit`, `comment_react`, `comment_close`, `comment_reopen`

**Attachments** — `attachment_add`, `attachment_list`, `attachment_delete`
Uploads go through Codecks' signed S3 flow.

**Decks** — `deck_get_detail`, `deck_list_by_project`, `deck_create`, `deck_update`, `deck_delete`, `deck_set_cover`, `deck_list_stock_covers`, `deck_move`

**Spaces** — `space_create`, `space_update`, `space_reorder`, `space_delete`

**Tags** — `tag_list`, `tag_add`, `tag_update`, `tag_remove`
Project-level tag CRUD; apply tags to cards via `masterTags` on `card_create` / `card_update`.

**Milestones** — `milestone_list`, `milestone_create`, `milestone_update`, `milestone_delete`

**Runs (sprints)** — `run_list`, `run_config_list` (read)

**Journeys (workflows)** — `journey_list_templates`, `journey_get_template`, `journey_clone_template`, `journey_add_item`, `journey_set_step_zones`, `journey_list_decks`, `journey_get_items`, `journey_apply`

**Projects** — `project_list`, `project_get_detail`

**Users** — `user_list`, `user_get_detail`

**Workspace / org** — `org_get_account_info`, `org_list`, `org_switch`

## Multi-workspace

With several `Orgs` configured, `org_list` shows them and `org_switch` changes the active one for subsequent calls. `org_get_account_info` reports the current workspace and account id.

## Rate limiting

Codecks allows 40 requests per 5 seconds. The server throttles itself to stay within that limit, so bursts of tool calls wait rather than fail.

## Built on

[`Ten99.Aria.Hosting.Mcp.Core`](https://www.nuget.org/packages/Ten99.Aria.Hosting.Mcp.Core) (the stdio MCP host) and [`Ten99.Aria.Common`](https://www.nuget.org/packages/Ten99.Aria.Common) (generic primitives).

## License

MIT © 1099 Ventures Inc. See [LICENSE](../LICENSE).
