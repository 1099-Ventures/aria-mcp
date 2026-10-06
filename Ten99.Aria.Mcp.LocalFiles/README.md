# Ten99.Aria.Mcp.LocalFiles

A filesystem [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server: read, write, search, and organise files and directories on the machine the server runs on. The tooling half for Claude Desktop/Code and ARIA agents.

All tools take **absolute paths**, and a blocked-paths guard refuses operations under sensitive system locations.

## Running it

Packaged as a .NET tool, run over stdio via `dnx`. Requires the .NET 10 SDK. The first run fetches the package from NuGet (`--yes` accepts the prompt). No configuration or credentials — it acts on the absolute paths you pass to each tool.

- **macOS / Linux:** `dotnet dnx Ten99.Aria.Mcp.LocalFiles --yes`
- **Windows:** `dnx Ten99.Aria.Mcp.LocalFiles --yes` (the standalone `dnx` command ships on the PATH with the SDK on Windows; on macOS/Linux use `dotnet dnx`)

Register it with an MCP client:

```json
{
  "mcpServers": {
    "localfiles": {
      "command": "dotnet",
      "args": ["dnx", "Ten99.Aria.Mcp.LocalFiles", "--yes"]
    }
  }
}
```

(On Windows you can set `"command": "dnx"` and drop the leading `"dnx"` arg.)

## Tools

- **Read & inspect** — `list_directory`, `read_file`, `get_file_info`, `file_exists`, `directory_exists`, `get_directory_summary`, `search_file`
- **Write & edit** — `write_file`, `modify_file_lines`
- **Organise** — `create_directory`, `create_directory_structure`, `rename_file`, `copy_file`, `move_file`, `batch_move_files`
- **Delete** — `delete_file`, `delete_directory` (with safety checks)

Batch tools (e.g. `batch_move_files`) return a per-item `{success, error}` envelope so a partial failure reports which paths failed rather than all-or-nothing.

## Status

Community-maintained and provided **as-is**, on a best-effort basis. Issues and pull requests are welcome.

## License

MIT © 1099 Ventures Inc. See [LICENSE](../LICENSE).
