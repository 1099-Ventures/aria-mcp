namespace Ten99.Aria.Mcp.Ado.Models;

/// <summary>
/// A single Azure DevOps organization the MCP can act against. Sourced from the global org
/// registry (<c>{home}/.claude/.ado-mcp-orgs.json</c>) — the registry lists orgs only and holds
/// no secrets. For a <see cref="AdoAuthType.Pat"/> org the token is populated separately from
/// config/env (<c>Ado:Pats:{Key}</c>), never from the registry file.
/// </summary>
public class AdoOrg
{
	/// <summary>Friendly key used to select this org (e.g. "1099") — the registry map key.</summary>
	public string Key { get; set; } = string.Empty;

	/// <summary>Azure DevOps organization name — the <c>{org}</c> in <c>https://dev.azure.com/{org}</c>.</summary>
	public string OrgName { get; set; } = string.Empty;

	/// <summary>How this org authenticates. Defaults to <see cref="AdoAuthType.Interactive"/>.</summary>
	public AdoAuthType AuthType { get; set; } = AdoAuthType.Interactive;

	/// <summary>ADO API surfaces enabled for this org (e.g. core, work-items, repositories, wiki).
	/// Read and carried today; enforcement is a follow-up (ADO #429).</summary>
	public List<string> Domains { get; set; } = [];

	/// <summary>Interactive auth: opt into Microsoft's first-party ADO public client id (pre-consented,
	/// so it works on tenants where you can't authorize your own app registration). Registry key
	/// <c>useMicrosoftClientId</c>. Mutually exclusive with <see cref="OverrideClientId"/>, which wins.</summary>
	public bool UseMicrosoftClientId { get; set; }

	/// <summary>Interactive auth: an explicit public-client app id to use, overriding both the global
	/// <c>Ado:Auth:ClientId</c> and <see cref="UseMicrosoftClientId"/> (highest precedence — lets a
	/// consumer fold in their own app). Registry key <c>overrideClientId</c>.</summary>
	public string? OverrideClientId { get; set; }

	/// <summary>Per-org Personal Access Token for <see cref="AdoAuthType.Pat"/> orgs. NOT stored in
	/// the registry — populated from <c>Ado:Pats:{Key}</c> (env <c>Ado__Pats__{Key}</c>) at load.
	/// Sent as <c>Authorization: Basic base64(":" + pat)</c>.</summary>
	public string? Pat { get; set; }
}
