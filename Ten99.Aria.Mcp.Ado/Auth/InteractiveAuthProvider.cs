using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Azure.Core;
using Ten99.Aria.Identity.EntraId;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Auth;

/// <summary>
/// Interactive (browser) AAD auth for the Azure DevOps REST resource. First sign-in per tenant pops
/// a browser once; the tokens live in an encrypted, persistent, cross-platform cache and an
/// <see cref="Azure.Identity.AuthenticationRecord"/> is saved so later process starts acquire silently
/// until the refresh token expires. The persistent-cache/record plumbing lives in the shared
/// <see cref="EntraCredentialFactory"/> (also used by the Graph MCP); this provider only resolves the
/// per-org client id + tenant and keys a credential per (client, tenant). Cross-tenant: the tenant is
/// discovered per org (<see cref="AadTenantResolver"/>). Defaults to a Microsoft public-client app id;
/// a consumer can supply their own via <c>Ado:Auth:ClientId</c> or per-org <c>overrideClientId</c>. See ADO #423.
/// </summary>
internal sealed class InteractiveAuthProvider : IAdoAuthProvider
{
	// Azure DevOps resource id; '.default' requests the Entra app's consented ADO permissions.
	const string AdoScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
	const string CacheName = "ten99-aria-ado-mcp";

	// Microsoft's first-party ADO public client (as used by Microsoft's own azure-devops-mcp) — it is
	// pre-consented, so it authenticates on tenants where you can't authorize your own app registration.
	// Used as the built-in default identity so the MCP authenticates with zero external config, and as the
	// per-org useMicrosoftClientId target. A consumer who wants their own Entra app supplies it via global
	// Ado:Auth:ClientId or per-org overrideClientId, which take precedence. Same ADO .default scope.
	const string MsAdoClientId = "0d50963b-7bb9-4fe7-94c7-a99af00b5136";

	static readonly ConcurrentDictionary<string, TokenCredential> _byTenant = new(StringComparer.OrdinalIgnoreCase);
	static readonly SemaphoreSlim _authGate = new(1, 1);

	public async Task<AuthenticationHeaderValue> GetAuthHeaderAsync(AdoOrg org, AdoAuthContext context, CancellationToken ct = default)
	{
		// Client-id precedence (per-org):
		// overrideClientId > useMicrosoftClientId > global Ado:Auth:ClientId > Microsoft public default.
		// The default is always non-empty, so interactive auth never lacks a client id. A consumer who
		// wants their own Entra app sets Ado:Auth:ClientId (global) or overrideClientId (per-org).
		string clientId =
			!string.IsNullOrWhiteSpace(org.OverrideClientId) ? org.OverrideClientId!.Trim()
			: org.UseMicrosoftClientId ? MsAdoClientId
			: !string.IsNullOrWhiteSpace(context.Settings.ClientId) ? context.Settings.ClientId!.Trim()
			: MsAdoClientId;

		string? tenantId = await AadTenantResolver.ResolveAsync(context.Http, org.OrgName, ct);

		TokenCredential credential = await GetOrCreateCredentialAsync(clientId!, tenantId, context.Settings, ct);
		AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([AdoScope], tenantId: tenantId), ct);
		return new AuthenticationHeaderValue("Bearer", token.Token);
	}

	static async Task<TokenCredential> GetOrCreateCredentialAsync(
		string clientId, string? tenantId, AdoAuthSettings settings, CancellationToken ct)
	{
		string key = $"{clientId}|{tenantId ?? "organizations"}";
		if (_byTenant.TryGetValue(key, out TokenCredential? existing))
			return existing;

		await _authGate.WaitAsync(ct);
		try
		{
			if (_byTenant.TryGetValue(key, out existing))
				return existing;

			var credential = await EntraCredentialFactory.CreateInteractiveAsync(new EntraCredentialOptions
			{
				ClientId = clientId,
				TenantId = tenantId,
				CacheName = CacheName,
				CacheDir = ResolveCacheDir(settings),
				AuthRecordFileName = RecordFileName(clientId, tenantId, settings),
			}, [AdoScope], ct);

			_byTenant[key] = credential;
			return credential;
		}
		finally
		{
			_authGate.Release();
		}
	}

	// Record file is per (tenant, client id). Keep the legacy tenant-only name for the global-default
	// client so existing orgs don't re-auth; discriminate by client id for MS/override clients so two
	// apps on the same tenant get separate records and never clobber each other's account.
	static string RecordFileName(string clientId, string? tenantId, AdoAuthSettings settings)
	{
		string tenantSeg = tenantId ?? "organizations";
		// The global-default client is the one configured via Ado:Auth:ClientId; keep the legacy
		// tenant-only record name for it so a configured consumer never re-auths across restarts.
		bool isGlobalClient =
			string.Equals(clientId, settings.ClientId, StringComparison.OrdinalIgnoreCase);
		return isGlobalClient ? $"{tenantSeg}.authrecord.json" : $"{tenantSeg}.{clientId}.authrecord.json";
	}

	static string ResolveCacheDir(AdoAuthSettings settings)
		=> string.IsNullOrWhiteSpace(settings.CacheDirectory)
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "ten99-aria-mcp", "auth")
			: settings.CacheDirectory!;
}
