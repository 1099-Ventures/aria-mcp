using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Azure.Core;
using Ten99.Aria.Identity.EntraId;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Auth;

/// <summary>
/// Service-principal (client-credential) auth for the Azure DevOps REST resource — autonomous, no
/// browser. Uses the shared <see cref="EntraCredentialFactory"/> to build a client-secret credential
/// from the global <c>Ado:Auth</c> settings (client id + secret), and the tenant is either configured
/// (<c>Ado:Auth:TenantId</c>) or discovered per-org (<see cref="AadTenantResolver"/>). The SP must be a
/// member of, or guest in, the org's tenant with the org authorising it. See ADO #431.
/// </summary>
internal sealed class ServicePrincipalAuthProvider : IAdoAuthProvider
{
	const string AdoScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";

	static readonly ConcurrentDictionary<string, TokenCredential> _byTenant = new(StringComparer.OrdinalIgnoreCase);

	public async Task<AuthenticationHeaderValue> GetAuthHeaderAsync(AdoOrg org, AdoAuthContext context, CancellationToken ct = default)
	{
		AdoAuthSettings s = context.Settings;
		if (string.IsNullOrWhiteSpace(s.ClientId))
			throw new InvalidOperationException(
				$"Service-principal auth for org '{org.Key}' has no client id — set Ado:Auth:ClientId " +
				"(env Ado__Auth__ClientId) to the service-principal app id. See ADO #431.");
		if (string.IsNullOrWhiteSpace(s.ClientSecret) && !s.HasClientCertificate)
			throw new InvalidOperationException(
				$"Service-principal auth for org '{org.Key}' has no credential — set Ado:Auth:ClientSecret or a " +
				"client certificate (Ado:Auth:ClientCertThumbprint / Ado:Auth:ClientCertPath). The registry stays " +
				"secret-free. See ADO #431.");

		// Tenant: configured wins; otherwise discover it from the org (client-credential needs a concrete
		// tenant — it can't ride the multi-tenant "organizations" authority the interactive flow uses).
		string? tenantId = !string.IsNullOrWhiteSpace(s.TenantId)
			? s.TenantId
			: await AadTenantResolver.ResolveAsync(context.Http, org.OrgName, ct);
		if (string.IsNullOrWhiteSpace(tenantId))
			throw new InvalidOperationException(
				$"Could not resolve a tenant for org '{org.Key}' ({org.OrgName}). Set Ado:Auth:TenantId " +
				"(env Ado__Auth__TenantId) for service-principal auth. See ADO #431.");

		string key = $"{s.ClientId}|{tenantId}";
		TokenCredential credential = _byTenant.GetOrAdd(key, _ =>
			EntraCredentialFactory.CreateServicePrincipal(new EntraCredentialOptions
			{
				TenantId = tenantId,
				ClientId = s.ClientId,
				// Certificate wins over secret inside the factory; resolve the configured cert source (if any).
				Certificate = EntraCertificateResolver.Resolve(s.ClientCertThumbprint, s.ClientCertPath, s.ClientCertPassword),
				ClientSecret = s.ClientSecret,
			}));

		AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([AdoScope], tenantId: tenantId), ct);
		return new AuthenticationHeaderValue("Bearer", token.Token);
	}
}
