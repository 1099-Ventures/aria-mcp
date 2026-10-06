using Ten99.Aria.Hosting.Mcp.Core;

namespace Ten99.Aria.Mcp.Email.Imap;

/// <summary>
/// Self-contained entry point for the IMAP email MCP — the per-tool packaging shape (ADO #426). A thin
/// wrapper over the shared <see cref="AriaMcpLauncher"/>: hands it this assembly's tools and lets the
/// launcher own hosting, config, auth, and the stdio transport. Packaged as a dotnet tool so it runs via
/// <c>dnx</c>; the generic host can also load this same assembly via <c>--mcp</c>.
/// </summary>
internal static class Program
{
	static Task Main(string[] args)
	{
		// The launcher wires a shared HttpClient into tool signatures (unused by the IMAP backend, but the
		// tool method shape mirrors O365). Prepend the default so a caller's explicit --usesHttp still wins.
		string[] argsWithDefaults = ["--usesHttp", "true", .. MapFriendlySwitches(args)];
		return AriaMcpLauncher.RunAsync(typeof(ImapEmailTool).Assembly, argsWithDefaults);
	}

	/// <summary>
	/// Maps friendly CLI switches to the IConfiguration keys the tools read, so config lands under the
	/// <c>Imap:Auth</c> / <c>Imap:Smtp</c> sections (env equivalents <c>Imap__Auth__Host</c>, … bind
	/// without mapping): <c>--host</c> → <c>--Imap:Auth:Host</c>, etc.
	/// </summary>
	static string[] MapFriendlySwitches(string[] args)
	{
		string[] mapped = new string[args.Length];
		for (int i = 0; i < args.Length; i++)
			mapped[i] = args[i] switch
			{
				"--host" => "--Imap:Auth:Host",
				"--port" => "--Imap:Auth:Port",
				"--username" => "--Imap:Auth:Username",
				"--password" => "--Imap:Auth:Password",
				"--authMode" => "--Imap:Auth:AuthMode",
				"--defaultMailbox" => "--Imap:Auth:DefaultMailbox",
				"--smtpHost" => "--Imap:Smtp:Host",
				"--smtpPort" => "--Imap:Smtp:Port",
				_ => args[i],
			};
		return mapped;
	}
}
