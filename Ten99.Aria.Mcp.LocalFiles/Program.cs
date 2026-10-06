using Ten99.Aria.Hosting.Mcp.Core;

namespace Ten99.Aria.Mcp.LocalFiles;

/// <summary>
/// Self-contained entry point for the LocalFiles MCP — the per-tool packaging shape (ADO #430),
/// mirroring Ten99.Aria.Mcp.Ado. A one-line <c>Main</c> over the shared <see cref="AriaMcpLauncher"/>;
/// the generic host can still load this assembly via <c>--mcp</c>. Packaged as a dnx tool.
/// No HTTP or Graph — local filesystem only.
/// </summary>
internal static class Program
{
	static Task Main(string[] args) => AriaMcpLauncher.RunAsync(typeof(FileTool).Assembly, args);
}
