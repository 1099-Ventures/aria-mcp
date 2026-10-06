namespace Ten99.Aria.Mcp.Ado.Models;

/// <summary>
/// Cross-cutting auth settings, bound from <c>Ado:Auth</c>. Not per-org (that's <see cref="AdoOrg.AuthType"/>);
/// these are the shared knobs the AAD providers need — the Entra app client id and where the
/// persistent token/credential cache lives.
/// </summary>
public class AdoAuthSettings
{
	/// <summary>Client id of the multi-tenant public-client Entra app used for interactive sign-in.
	/// Required for <c>authType=interactive</c> orgs. Bound from <c>Ado:Auth:ClientId</c> /
	/// env <c>Ado__Auth__ClientId</c>.</summary>
	public string? ClientId { get; set; }

	/// <summary>Optional override for the auth cache directory. Defaults to
	/// <c>{home}/.claude/ten99-aria-mcp/auth</c>.</summary>
	public string? CacheDirectory { get; set; }

	/// <summary>Tenant id for <c>authType=servicePrincipal</c> orgs. If unset, the tenant is discovered
	/// per-org from Azure DevOps (the AadTenantResolver). Bound from <c>Ado:Auth:TenantId</c> /
	/// env <c>Ado__Auth__TenantId</c>.</summary>
	public string? TenantId { get; set; }

	/// <summary>Client secret for <c>authType=servicePrincipal</c> orgs (paired with <see cref="ClientId"/>).
	/// Supply this OR a client certificate (<see cref="ClientCertThumbprint"/> / <see cref="ClientCertPath"/>).
	/// Bound from <c>Ado:Auth:ClientSecret</c> / env <c>Ado__Auth__ClientSecret</c>; keep it out of the org
	/// registry (which stays secret-free).</summary>
	public string? ClientSecret { get; set; }

	/// <summary>Service-principal client certificate by store thumbprint (CurrentUser then LocalMachine
	/// "My"). Preferred over <see cref="ClientSecret"/> for unattended auth; a certificate wins over a secret
	/// when both are set. Bound from <c>Ado:Auth:ClientCertThumbprint</c>.</summary>
	public string? ClientCertThumbprint { get; set; }

	/// <summary>Service-principal client certificate from a file — PFX/PKCS#12, or a PEM holding the cert +
	/// private key. Alternative to <see cref="ClientCertThumbprint"/>. Bound from <c>Ado:Auth:ClientCertPath</c>.</summary>
	public string? ClientCertPath { get; set; }

	/// <summary>Password for an encrypted PFX given by <see cref="ClientCertPath"/> (ignored for PEM).
	/// Bound from <c>Ado:Auth:ClientCertPassword</c>.</summary>
	public string? ClientCertPassword { get; set; }

	/// <summary>True when a client certificate source (thumbprint or file) is configured.</summary>
	public bool HasClientCertificate =>
		!string.IsNullOrWhiteSpace(ClientCertThumbprint) || !string.IsNullOrWhiteSpace(ClientCertPath);

	/// <summary>Optional user-assigned managed-identity client id for <c>authType=managedIdentity</c> orgs.
	/// Omit for a system-assigned identity (the usual deployed case — a silent login, no id needed). Bound
	/// from <c>Ado:Auth:ManagedIdentityClientId</c> / env <c>Ado__Auth__ManagedIdentityClientId</c>.</summary>
	public string? ManagedIdentityClientId { get; set; }
}
