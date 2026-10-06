using Ten99.Aria.Hosting.Mcp.Core;

namespace Ten99.Aria.Mcp.Ado;

/// <summary>
/// Self-contained entry point for the ADO MCP — the per-tool packaging shape. A thin wrapper over
/// the shared <see cref="AriaMcpLauncher"/>: it hands the launcher this assembly's tools and lets
/// the launcher own hosting, config, auth, and the stdio transport. Packaged as a dotnet tool so it
/// can be run via <c>dnx</c> from anywhere; the generic host can still load this same assembly via
/// <c>--mcp</c>. See ADO #426.
/// </summary>
internal static class Program
{
	static Task Main(string[] args)
	{
		// ADO tools always hit the REST API, so the shared HttpClient is required. Prepend the
		// default so a caller's explicit --usesHttp still wins (later CLI value overrides).
		string[] argsWithDefaults = ["--usesHttp", "true", .. MapFriendlySwitches(args)];
		return AriaMcpLauncher.RunAsync(typeof(AdoCoreTool).Assembly, argsWithDefaults);
	}

	/// <summary>
	/// Maps friendly CLI switches to the IConfiguration keys the launcher binds:
	/// <c>--default-org</c> → <c>--Ado:DefaultOrg</c>, <c>--registry</c> → <c>--Ado:RegistryPath</c>.
	/// The env equivalents (<c>Ado__DefaultOrg</c>, <c>Ado__RegistryPath</c>) bind without mapping.
	/// </summary>
	static string[] MapFriendlySwitches(string[] args)
	{
		string[] mapped = new string[args.Length];
		for (int i = 0; i < args.Length; i++)
			mapped[i] = args[i] switch
			{
				"--default-org" => "--Ado:DefaultOrg",
				"--registry" => "--Ado:RegistryPath",
				_ => args[i],
			};
		return mapped;
	}
}
