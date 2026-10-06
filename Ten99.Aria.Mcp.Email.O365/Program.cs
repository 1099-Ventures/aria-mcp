using Ten99.Aria.Hosting.Mcp.Core;

namespace Ten99.Aria.Mcp.Email.O365;

/// <summary>
/// Self-contained entry point for the O365 email MCP — the per-tool packaging shape (ADO #426).
/// A thin wrapper over the shared <see cref="AriaMcpLauncher"/>: hands it this assembly's tools and
/// lets the launcher own hosting, config, auth, and the stdio transport. Packaged as a dotnet tool
/// so it runs via <c>dnx</c>; the generic host can also load this same assembly via <c>--mcp</c>.
/// </summary>
internal static class Program
{
	static Task Main(string[] args)
	{
		// O365 tools call Microsoft Graph, so the shared HttpClient is required. Prepend the default
		// so a caller's explicit --usesHttp still wins (later CLI value overrides).
		string[] argsWithDefaults = ["--usesHttp", "true", .. MapFriendlySwitches(args)];
		return AriaMcpLauncher.RunAsync(typeof(O365EmailTool).Assembly, argsWithDefaults);
	}

	/// <summary>
	/// Maps friendly CLI switches to the IConfiguration keys the tools read, so config lands under the
	/// <c>O365:Auth</c> section (env equivalents <c>O365__Auth__ClientId</c>, … bind without mapping):
	/// <c>--clientId</c> → <c>--O365:Auth:ClientId</c>, etc.
	/// </summary>
	static string[] MapFriendlySwitches(string[] args)
	{
		string[] mapped = new string[args.Length];
		for (int i = 0; i < args.Length; i++)
			mapped[i] = args[i] switch
			{
				"--tenantId" => "--O365:Auth:TenantId",
				"--clientId" => "--O365:Auth:ClientId",
				"--clientSecret" => "--O365:Auth:ClientSecret",
				"--clientCertThumbprint" => "--O365:Auth:ClientCertThumbprint",
				"--clientCertPath" => "--O365:Auth:ClientCertPath",
				"--clientCertPassword" => "--O365:Auth:ClientCertPassword",
				"--defaultMailbox" => "--O365:Auth:DefaultMailbox",
				"--mailboxes" => "--O365:Auth:Mailboxes",
				"--authMode" => "--O365:Auth:AuthMode",
				_ => args[i],
			};
		return mapped;
	}
}
