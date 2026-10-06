# ARIA MCP servers

Open-source [Model Context Protocol](https://modelcontextprotocol.io) (MCP) servers from
[1099 Ventures](https://github.com/1099-Ventures), built in C# for .NET 10.

This repository is a **read-only mirror**, published from the canonical ARIA source. Build it
with the .NET 10 SDK and `dotnet build`; it is self-contained (no private dependencies). Each
server runs as a `dnx` tool — see its own README for cross-platform run and config details.

## Servers

| Package | What it is |
| --- | --- |
| [`Ten99.Aria.Mcp.Ado`](Ten99.Aria.Mcp.Ado/README.md) | Azure DevOps: work items, repos, PRs, pipelines, wiki, search — multi-org. |
| [`Ten99.Aria.Mcp.Codecks`](Ten99.Aria.Mcp.Codecks/README.md) | [Codecks](https://www.codecks.io) project management. |
| [`Ten99.Aria.Mcp.LocalFiles`](Ten99.Aria.Mcp.LocalFiles/README.md) | Local filesystem read/write/search/organise. |
| [`Ten99.Aria.Mcp.Email.O365`](Ten99.Aria.Mcp.Email.O365/README.md) | Microsoft 365 (Graph) mailbox read/filter/triage. |
| [`Ten99.Aria.Mcp.Email.Imap`](Ten99.Aria.Mcp.Email.Imap/README.md) | IMAP mailbox read/filter/triage. |

Shared libraries: `Ten99.Aria.Hosting.Mcp.Core` (stdio MCP host), `Ten99.Aria.Common` (generic
primitives), and `Ten99.Aria.Identity.EntraId` / `Ten99.Aria.Integration.Graph*` (auth + Graph
used by the ADO and email servers).

## Contributing

Issues and pull requests are welcome. These servers are community-maintained and provided
**as-is**, on a best-effort basis.

## License

MIT © 1099 Ventures Inc. See [LICENSE](LICENSE).
