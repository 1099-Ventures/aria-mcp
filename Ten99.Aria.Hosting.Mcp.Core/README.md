# Ten99.Aria.Hosting.Mcp.Core

A reusable host for running a [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server over stdio, used by the ARIA MCP servers.

## What it does

`AriaMcpLauncher` boots an MCP server from a tool assembly in one line, wiring the stdio transport and sensible host defaults via `AddMcpDefaults()`:

- stderr-safe console logging (stdout is the MCP wire),
- typed `[ConfigurationSection]` binding from an `options` key,
- user-secrets (DEBUG) / NoOp secret resolution,
- an optional `HttpClient` (`--usesHttp true`).

```csharp
// Program.cs of a per-tool MCP package
static Task Main(string[] args) =>
    AriaMcpLauncher.RunAsync(typeof(SomeTool).Assembly, args);
```

The generic host form, `AriaMcpLauncher.RunAsync(args)`, instead resolves the tool assembly from a `--mcp <dll>` argument.

Depends only on `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Http`, `ModelContextProtocol`, and `Ten99.Aria.Common`. No cloud SDKs.

## License

MIT © 1099 Ventures Inc
