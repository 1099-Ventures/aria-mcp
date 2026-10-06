using Ten99.Aria.Hosting.Mcp.Core;

namespace Ten99.Aria.Mcp.Codecks;

/// <summary>
/// Self-contained entry point for the Codecks MCP — the per-tool packaging shape (ADO #430),
/// mirroring Ten99.Aria.Mcp.Ado. A one-line <c>Main</c> over the shared <see cref="AriaMcpLauncher"/>;
/// the generic host can still load this assembly via <c>--mcp</c>. Packaged as a dnx tool.
/// </summary>
internal static class Program
{
	static Task Main(string[] args)
	{
		// Codecks talks to the Codecks HTTP API, so the shared HttpClient is required. Prepend the
		// default so a caller's explicit --usesHttp still wins (later CLI value overrides).
		string[] argsWithDefaults = ["--usesHttp", "true", .. args];
		return AriaMcpLauncher.RunAsync(typeof(Tools.CodecksCardTool).Assembly, argsWithDefaults);
	}
}
