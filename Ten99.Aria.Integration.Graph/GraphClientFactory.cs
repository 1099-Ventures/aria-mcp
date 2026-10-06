using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Ten99.Aria.Common.Interfaces;
using Ten99.Aria.Identity.EntraId;

namespace Ten99.Aria.Integration.Graph;

/// <summary>
/// Factory for creating authenticated GraphServiceClient instances. The Entra credential itself comes
/// from the shared <see cref="EntraCredentialFactory"/>; this class only wraps it in a GraphServiceClient
/// (plus the Graph-specific delegated/auth-code flows below).
/// </summary>
public class GraphClientFactory
{
	/// <summary>
	/// Sync factory for client credential and managed identity flows.
	/// Retained for backward compatibility — these flows don't need async token acquisition.
	/// </summary>
	public static GraphServiceClient Create(GraphTenantConfig config)
	{
		return config.AuthMode switch
		{
			GraphAuthMode.ManagedIdentity => CreateWithDefaultCredential(config.DefaultScopes),
			GraphAuthMode.ClientCredential => CreateWithClientCredentials(
				config.TenantId, config.ClientId, config.ClientSecret!, config.DefaultScopes),
			_ => throw new InvalidOperationException(
				$"AuthMode {config.AuthMode} requires async creation. Use CreateAsync() instead.")
		};
	}

	/// <summary>
	/// Async factory that handles all auth modes including delegated token flows.
	/// Returns a result type — delegated flows may require re-authorization.
	/// </summary>
	public static async Task<GraphAuthResult> CreateAsync(
		GraphTenantConfig config,
		ITokenStore? tokenStore = null,
		CancellationToken ct = default)
	{
		switch (config.AuthMode)
		{
			case GraphAuthMode.ClientCredential:
				if (string.IsNullOrWhiteSpace(config.ClientSecret))
					return GraphAuthResult.Failure("ClientSecret is required for ClientCredential auth mode.");
				return GraphAuthResult.Success(
					CreateWithClientCredentials(config.TenantId, config.ClientId, config.ClientSecret, config.DefaultScopes));

			case GraphAuthMode.ManagedIdentity:
				return GraphAuthResult.Success(
					CreateWithDefaultCredential(config.DefaultScopes));

			case GraphAuthMode.DelegatedToken:
				if (string.IsNullOrWhiteSpace(config.UserIdentifier))
					return GraphAuthResult.Failure("UserIdentifier is required for DelegatedToken auth mode.");
				if (tokenStore is null)
					return GraphAuthResult.Failure("ITokenStore is required for DelegatedToken auth mode.");
				return await CreateWithDelegatedTokenAsync(config, tokenStore, ct);

			default:
				return GraphAuthResult.Failure($"Unknown auth mode: {config.AuthMode}");
		}
	}

	public static GraphServiceClient CreateWithDefaultCredential(string[]? scopes = null)
	{
		var credential = new DefaultAzureCredential();
		return new GraphServiceClient(credential, scopes ?? ["https://graph.microsoft.com/.default"]);
	}

	public static GraphServiceClient CreateWithClientCredentials(
		string tenantId, string clientId, string clientSecret, string[]? scopes = null)
	{
		var credential = EntraCredentialFactory.CreateServicePrincipal(new EntraCredentialOptions
		{
			TenantId = tenantId,
			ClientId = clientId,
			ClientSecret = clientSecret,
		});
		return new GraphServiceClient(credential, scopes ?? ["https://graph.microsoft.com/.default"]);
	}

	/// <summary>Service-principal Graph client from prebuilt credential options — supports either a client
	/// secret or a client certificate (the factory prefers the certificate when both are present). Lets a
	/// caller wire cert-based SP without this factory knowing how the cert was sourced.</summary>
	public static GraphServiceClient CreateWithServicePrincipal(EntraCredentialOptions options, string[]? scopes = null)
	{
		var credential = EntraCredentialFactory.CreateServicePrincipal(options);
		return new GraphServiceClient(credential, scopes ?? ["https://graph.microsoft.com/.default"]);
	}

	/// <summary>Azure managed identity — system-assigned when <paramref name="userAssignedClientId"/> is
	/// null (a silent login from the runtime's identity), user-assigned when it names the identity. App-only,
	/// so scopes default to Graph <c>/.default</c> (application permissions).</summary>
	public static GraphServiceClient CreateWithManagedIdentity(string? userAssignedClientId = null, string[]? scopes = null)
	{
		var credential = EntraCredentialFactory.CreateManagedIdentity(new EntraCredentialOptions { ClientId = userAssignedClientId });
		return new GraphServiceClient(credential, scopes ?? ["https://graph.microsoft.com/.default"]);
	}

	/// <summary>
	/// Desktop interactive browser sign-in (public client, no secret) for delegated Graph access —
	/// the MCP/agent acts as the signed-in user. First run pops a browser once; tokens live in an
	/// encrypted, persistent, cross-platform cache (DPAPI / Keychain / libsecret) and an
	/// <see cref="AuthenticationRecord"/> is saved so later launches acquire silently until the
	/// refresh token expires. Same pattern as the ADO MCP's interactive auth.
	/// </summary>
	/// <param name="scopes">Delegated Graph scopes, e.g. <c>https://graph.microsoft.com/Mail.ReadWrite</c>
	/// (do not include <c>offline_access</c> — Azure.Identity requests it automatically).</param>
	/// <param name="cacheName">Token-cache name (isolates this MCP's cache).</param>
	/// <param name="cacheDir">Where the AuthenticationRecord is persisted. Defaults under ~/.claude.</param>
	/// <param name="tenantId">Optional. With a multi-tenant (cross-tenant) app, leave null — the
	/// signed-in user's home tenant is used ("organizations"). Pass a tenant only to target a
	/// specific or guest tenant (e.g. a shared mailbox in another tenant).</param>
	/// <param name="loginHint">Optional account UPN to pre-select at sign-in. This — not the tenant — is
	/// what makes the email sufficient for multi-tenancy: Azure AD does home-realm discovery on the UPN's
	/// domain and routes to the right tenant, so a cross-tenant app signs the correct account in without a
	/// tenantId. Without a hint the browser rides whatever session is already open (the wrong account when
	/// several MCP instances target different mailboxes).</param>
	public static async Task<GraphServiceClient> CreateWithInteractiveBrowserAsync(
		string clientId, string[] scopes, string cacheName,
		string? tenantId = null, string? loginHint = null, string? cacheDir = null, CancellationToken ct = default)
	{
		// Keep the Graph-specific authrecord location (.../auth/graph) so existing cached records resolve.
		cacheDir ??= Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".claude", "ten99-aria-mcp", "auth", "graph");

		var credential = await EntraCredentialFactory.CreateInteractiveAsync(new EntraCredentialOptions
		{
			ClientId = clientId,
			TenantId = tenantId,
			LoginHint = loginHint,
			CacheName = cacheName,
			CacheDir = cacheDir,
		}, scopes, ct);

		return new GraphServiceClient(credential, scopes);
	}

	/// <summary>
	/// Creates a GraphServiceClient using a stored delegated token via MSAL.
	/// Uses MSAL's token cache serialization backed by ITokenStore for automatic refresh.
	/// </summary>
	public static async Task<GraphAuthResult> CreateWithDelegatedTokenAsync(
		GraphTenantConfig config,
		ITokenStore tokenStore,
		CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(config.UserIdentifier))
			return GraphAuthResult.Failure("UserIdentifier is required for delegated token flows.");

		try
		{
			var app = ConfidentialClientApplicationBuilder
				.Create(config.ClientId)
				.WithClientSecret(config.ClientSecret)
				.WithTenantId(config.TenantId)
				.Build();

			// Wire MSAL's token cache to our ITokenStore
			app.UserTokenCache.SetBeforeAccessAsync(async args =>
			{
				var cached = await tokenStore.GetAsync(args.SuggestedCacheKey, ct);
				if (cached is not null)
					args.TokenCache.DeserializeMsalV3(cached);
			});
			app.UserTokenCache.SetAfterAccessAsync(async args =>
			{
				if (args.HasStateChanged)
					await tokenStore.StoreAsync(args.SuggestedCacheKey, args.TokenCache.SerializeMsalV3(), ct);
			});

			// Try to acquire token silently using cached refresh token
			var account = await app.GetAccountAsync(config.UserIdentifier);

			if (account is null)
				return GraphAuthResult.Reauth("No cached account found. User needs to authorize via OAuth.");

			var result = await app.AcquireTokenSilent(config.DelegatedScopes, account)
				.ExecuteAsync(ct);

			var client = new GraphServiceClient(
				new BearerTokenCredential(result.AccessToken, result.ExpiresOn));

			return GraphAuthResult.Success(client);
		}
		catch (MsalUiRequiredException ex)
		{
			return GraphAuthResult.Reauth($"Token expired or revoked: {ex.Message}");
		}
		catch (Exception ex)
		{
			return GraphAuthResult.Failure($"Delegated token acquisition failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Acquires tokens via authorization code (used during OAuth callback).
	/// Stores the resulting tokens in the MSAL cache via ITokenStore.
	/// </summary>
	public static async Task<GraphAuthResult> AcquireTokenByAuthorizationCodeAsync(
		GraphTenantConfig config,
		ITokenStore tokenStore,
		string authorizationCode,
		string redirectUri,
		CancellationToken ct = default)
	{
		try
		{
			var app = ConfidentialClientApplicationBuilder
				.Create(config.ClientId)
				.WithClientSecret(config.ClientSecret)
				.WithTenantId(config.TenantId)
				.WithRedirectUri(redirectUri)
				.Build();

			// Wire token cache
			app.UserTokenCache.SetBeforeAccessAsync(async args =>
			{
				var cached = await tokenStore.GetAsync(args.SuggestedCacheKey, ct);
				if (cached is not null)
					args.TokenCache.DeserializeMsalV3(cached);
			});
			app.UserTokenCache.SetAfterAccessAsync(async args =>
			{
				if (args.HasStateChanged)
					await tokenStore.StoreAsync(args.SuggestedCacheKey, args.TokenCache.SerializeMsalV3(), ct);
			});

			var result = await app.AcquireTokenByAuthorizationCode(config.DelegatedScopes, authorizationCode)
				.ExecuteAsync(ct);

			var client = new GraphServiceClient(
				new BearerTokenCredential(result.AccessToken, result.ExpiresOn));

			return GraphAuthResult.Success(client);
		}
		catch (Exception ex)
		{
			return GraphAuthResult.Failure($"Authorization code exchange failed: {ex.Message}");
		}
	}
}
