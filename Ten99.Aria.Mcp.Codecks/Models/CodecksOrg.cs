namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>
/// A single Codecks organization the MCP can act against. Configured via the
/// <c>Codecks:Orgs</c> array in the file supplied by the host's <c>--config</c> argument.
/// </summary>
public class CodecksOrg
{
	/// <summary>Friendly key used to select this org (e.g. "acme-studio").</summary>
	public string Key { get; set; } = string.Empty;

	/// <summary>Codecks workspace slug, sent as the optional <c>X-Account</c> header (must match the
	/// token's organisation when present).</summary>
	public string Account { get; set; } = string.Empty;

	/// <summary>Per-org Codecks API token (<c>cdxat_</c> organisation or <c>cdxut_</c> personal), sent as
	/// <c>Authorization: Bearer</c>. Replaces the retired X-Auth-Token credential.</summary>
	public string? Token { get; set; }
}
