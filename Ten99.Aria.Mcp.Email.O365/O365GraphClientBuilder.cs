using Microsoft.Graph;
using Ten99.Aria.Identity.EntraId;
using Ten99.Aria.Mcp.Email.O365.Configuration;
using AriaGraphClientFactory = Ten99.Aria.Integration.Graph.GraphClientFactory;

namespace Ten99.Aria.Mcp.Email.O365;

/// <summary>
/// Builds the authenticated <see cref="GraphServiceClient"/> from <c>O365:Auth</c> config across the three
/// auth modes — shared by the tool path and the health path so their validation/wiring can't drift.
/// </summary>
internal static class O365GraphClientBuilder
{
	// App-only flows (service principal, managed identity) use application permissions via /.default;
	// interactive uses the delegated scope set (config.ResolveDelegatedScopes()).
	private static readonly string[] AppOnlyScopes = ["https://graph.microsoft.com/.default"];

	public static async Task<GraphServiceClient> BuildAsync(O365EmailConfiguration config, string mailbox, CancellationToken ct)
	{
		EntraAuthMode mode = ValidateConfig(config);

		return mode switch
		{
			EntraAuthMode.Interactive => await AriaGraphClientFactory.CreateWithInteractiveBrowserAsync(
				config.ClientId!, config.ResolveDelegatedScopes(),
				cacheName: InteractiveAuth.CacheName(config.DefaultMailbox ?? mailbox),
				tenantId: config.TenantId, loginHint: config.DefaultMailbox ?? mailbox, ct: ct),

			EntraAuthMode.ServicePrincipal => AriaGraphClientFactory.CreateWithServicePrincipal(new EntraCredentialOptions
			{
				TenantId = config.TenantId,
				ClientId = config.ClientId,
				// Certificate wins over secret inside the factory; resolve the configured cert source (if any).
				Certificate = EntraCertificateResolver.Resolve(config.ClientCertThumbprint, config.ClientCertPath, config.ClientCertPassword),
				ClientSecret = config.ClientSecret,
			}, AppOnlyScopes),

			// System-assigned when ClientId is omitted; user-assigned when it names the identity.
			EntraAuthMode.ManagedIdentity => AriaGraphClientFactory.CreateWithManagedIdentity(config.ClientId, AppOnlyScopes),

			_ => throw new InvalidOperationException($"Unsupported auth mode: {mode}"),
		};
	}

	/// <summary>Map the config string to a mode. Default (unset) is service principal; <c>clientcredential</c>
	/// is accepted as a back-compat alias.</summary>
	public static EntraAuthMode ParseAuthMode(string? authMode) => authMode?.Trim().ToLowerInvariant() switch
	{
		"interactive" => EntraAuthMode.Interactive,
		"managedidentity" or "managed" or "mi" => EntraAuthMode.ManagedIdentity,
		"serviceprincipal" or "sp" or "clientcredential" or "client_credential" or null or "" => EntraAuthMode.ServicePrincipal,
		var other => throw new InvalidOperationException(
			$"Unknown authMode '{other}'. Use: interactive | servicePrincipal | managedIdentity."),
	};

	/// <summary>Validate the per-mode credential requirements and return the parsed mode. The single source
	/// of truth for "what does this auth mode need" — shared by the build path and the health check. Managed
	/// identity has NO required fields (system-assigned is a silent login), so it must never be routed
	/// through the service-principal guard. Does not check the mailbox — the caller owns that.</summary>
	public static EntraAuthMode ValidateConfig(O365EmailConfiguration c)
	{
		EntraAuthMode mode = ParseAuthMode(c.AuthMode);
		switch (mode)
		{
			case EntraAuthMode.Interactive:
				Require(c.ClientId, "clientId", mode);
				break;
			case EntraAuthMode.ServicePrincipal:
				Require(c.TenantId, "tenantId", mode);
				Require(c.ClientId, "clientId", mode);
				// A secret OR a certificate (thumbprint/file) satisfies SP auth.
				if (string.IsNullOrWhiteSpace(c.ClientSecret) && !c.HasClientCertificate)
					throw new InvalidOperationException(
						"servicePrincipal auth needs a clientSecret or a client certificate " +
						"(clientCertThumbprint / clientCertPath).");
				break;
			case EntraAuthMode.ManagedIdentity:
				break; // nothing required (ClientId optional, for user-assigned)
		}
		return mode;
	}

	private static void Require(string? value, string name, EntraAuthMode mode)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidOperationException($"{name} is required for {mode} auth.");
	}
}
