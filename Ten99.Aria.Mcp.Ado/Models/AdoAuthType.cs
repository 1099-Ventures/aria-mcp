namespace Ten99.Aria.Mcp.Ado.Models;

/// <summary>
/// How the MCP authenticates to an organization. Declared per-org in the registry
/// (<c>~/.claude/.ado-mcp-orgs.json</c>) as <c>authType</c>. v1 implements only <see cref="Pat"/>;
/// the rest are recognised so registries stay forward-compatible, but resolving auth for them
/// throws "not yet implemented" until ADO #423 lands the providers.
/// </summary>
public enum AdoAuthType
{
	/// <summary>Personal Access Token (secret sourced from config/env, never the registry).</summary>
	Pat,

	/// <summary>Interactive browser OAuth (cross-tenant). Implemented in #423.</summary>
	Interactive,

	/// <summary>Managed identity (DefaultAzureCredential) for deployed agents. Implemented in #423.</summary>
	ManagedIdentity,

	/// <summary>Service principal (autonomous, no browser). Implemented in #423.</summary>
	ServicePrincipal,
}

public static class AdoAuthTypeParser
{
	/// <summary>Parses the registry's <c>authType</c> string. Unknown/blank values map to
	/// <see cref="AdoAuthType.Interactive"/> (the registry's prevailing default).</summary>
	public static AdoAuthType Parse(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		"pat" => AdoAuthType.Pat,
		"interactive" or "oauth" or "web" => AdoAuthType.Interactive,
		"managedidentity" or "managed-identity" or "msi" => AdoAuthType.ManagedIdentity,
		"serviceprincipal" or "service-principal" or "sp" => AdoAuthType.ServicePrincipal,
		_ => AdoAuthType.Interactive,
	};
}
