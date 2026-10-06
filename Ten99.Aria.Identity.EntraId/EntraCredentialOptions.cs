using System.Security.Cryptography.X509Certificates;

namespace Ten99.Aria.Identity.EntraId;

/// <summary>Inputs for building an Entra <see cref="Azure.Core.TokenCredential"/>. Which fields are
/// required depends on the <see cref="EntraAuthMode"/> (see each field). Resource/scope is supplied at
/// call time, not here — the same credential can be used against Graph, Azure DevOps, etc.</summary>
public record EntraCredentialOptions
{
	/// <summary>Client id, interpreted by mode: the app-registration id for <see cref="EntraAuthMode.Interactive"/>
	/// and <see cref="EntraAuthMode.ServicePrincipal"/>; the user-assigned identity's client id for
	/// <see cref="EntraAuthMode.ManagedIdentity"/>. Omit for a **system-assigned** managed identity (the
	/// only mode where it's optional).</summary>
	public string? ClientId { get; init; }

	/// <summary>Tenant id. Required for <see cref="EntraAuthMode.ServicePrincipal"/>. For
	/// <see cref="EntraAuthMode.Interactive"/> leave null with a multi-tenant app (the login hint's domain
	/// routes via home-realm discovery); pass a tenant only to pin a specific/guest tenant. Ignored for
	/// managed identity.</summary>
	public string? TenantId { get; init; }

	/// <summary>Service-principal client secret. Supply this or <see cref="Certificate"/>.</summary>
	public string? ClientSecret { get; init; }

	/// <summary>Service-principal client certificate (preferred over a secret for unattended). Wins over
	/// <see cref="ClientSecret"/> when both are set.</summary>
	public X509Certificate2? Certificate { get; init; }

	// --- Interactive only ---

	/// <summary>Token-cache + AuthenticationRecord key. Must be unique per sign-in account so side-by-side
	/// processes for different accounts don't collide on one cache.</summary>
	public string? CacheName { get; init; }

	/// <summary>Directory where the AuthenticationRecord is persisted (the token cache itself lives in the
	/// OS keyring). Defaults under <c>~/.claude/ten99-aria-mcp/auth</c>.</summary>
	public string? CacheDir { get; init; }

	/// <summary>Optional exact file name (within <see cref="CacheDir"/>) for the persisted
	/// <see cref="Azure.Identity.AuthenticationRecord"/>. When null the factory derives one as
	/// <c>{CacheName}.{tenantSeg}.authrecord.json</c>. Set it only to keep a caller's pre-existing record
	/// layout (e.g. the ADO MCP's <c>{tenant}[.{clientId}].authrecord.json</c>) so existing sign-ins
	/// resolve without re-prompting.</summary>
	public string? AuthRecordFileName { get; init; }

	/// <summary>Account UPN to pre-select at sign-in. Home-realm discovery on its domain routes to the
	/// right tenant — this, not <see cref="TenantId"/>, is what makes a multi-tenant app sign the correct
	/// account in. Without it the browser rides whatever session is already open.</summary>
	public string? LoginHint { get; init; }
}
