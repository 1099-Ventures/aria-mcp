using Azure.Core;
using Azure.Identity;

namespace Ten99.Aria.Identity.EntraId;

/// <summary>
/// Builds Microsoft Entra ID <see cref="TokenCredential"/>s, resource-agnostic — the caller uses the
/// credential against whatever API it needs (Graph via <c>GraphServiceClient</c>, Azure DevOps via a
/// Bearer token for the ADO resource, etc.). Consolidates the interactive persistent-cache /
/// <see cref="AuthenticationRecord"/> logic that used to be duplicated across the Graph and ADO MCPs.
/// </summary>
public static class EntraCredentialFactory
{
	/// <summary>Build the credential for <paramref name="mode"/>. <paramref name="interactiveScopes"/> is
	/// used only by <see cref="EntraAuthMode.Interactive"/> (the one-time sign-in) and ignored otherwise.</summary>
	public static Task<TokenCredential> CreateAsync(
		EntraAuthMode mode,
		EntraCredentialOptions options,
		string[]? interactiveScopes = null,
		CancellationToken ct = default)
		=> mode switch
		{
			EntraAuthMode.Interactive => CreateInteractiveAsync(options, interactiveScopes ?? [], ct),
			EntraAuthMode.ServicePrincipal => Task.FromResult(CreateServicePrincipal(options)),
			EntraAuthMode.ManagedIdentity => Task.FromResult(CreateManagedIdentity(options)),
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Entra auth mode."),
		};

	/// <summary>App-only service principal. Uses the certificate when one is supplied (preferred for
	/// unattended), otherwise the client secret — the shape is inferred, not a separate mode.</summary>
	public static TokenCredential CreateServicePrincipal(EntraCredentialOptions o)
	{
		Require(o.TenantId, nameof(o.TenantId), EntraAuthMode.ServicePrincipal);
		Require(o.ClientId, nameof(o.ClientId), EntraAuthMode.ServicePrincipal);
		if (o.Certificate is not null)
			return new ClientCertificateCredential(o.TenantId, o.ClientId, o.Certificate);
		Require(o.ClientSecret, "ClientSecret (or Certificate)", EntraAuthMode.ServicePrincipal);
		return new ClientSecretCredential(o.TenantId, o.ClientId, o.ClientSecret);
	}

	/// <summary>Azure managed identity — system-assigned when <see cref="EntraCredentialOptions.ClientId"/>
	/// is omitted (a silent login), user-assigned when it names the identity's client id.</summary>
	public static TokenCredential CreateManagedIdentity(EntraCredentialOptions o)
		=> new ManagedIdentityCredential(
			string.IsNullOrWhiteSpace(o.ClientId)
				? ManagedIdentityId.SystemAssigned
				: ManagedIdentityId.FromUserAssignedClientId(o.ClientId));

	/// <summary>
	/// Desktop interactive browser sign-in (public client, no secret). First run pops a browser once;
	/// tokens live in an encrypted, persistent, cross-platform cache (DPAPI / Keychain / libsecret) and an
	/// <see cref="AuthenticationRecord"/> is saved so later launches acquire silently until the refresh
	/// token expires. <paramref name="scopes"/> are used for that one-time sign-in.
	/// </summary>
	public static async Task<TokenCredential> CreateInteractiveAsync(
		EntraCredentialOptions o,
		string[] scopes,
		CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(o.ClientId))
			throw new InvalidOperationException("ClientId is required for interactive auth.");
		if (string.IsNullOrWhiteSpace(o.CacheName))
			throw new InvalidOperationException("CacheName is required for interactive auth (isolates the token cache per account).");

		string cacheDir = string.IsNullOrWhiteSpace(o.CacheDir)
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "ten99-aria-mcp", "auth")
			: o.CacheDir;
		Directory.CreateDirectory(cacheDir);

		// tenantSeg mirrors the tenant handling: pinned tenant, else the multi-tenant "organizations".
		string tenantSeg = string.IsNullOrWhiteSpace(o.TenantId) ? "organizations" : o.TenantId;
		string recordFile = string.IsNullOrWhiteSpace(o.AuthRecordFileName)
			? $"{o.CacheName}.{tenantSeg}.authrecord.json"
			: o.AuthRecordFileName;
		string recordPath = Path.Combine(cacheDir, recordFile);

		var options = new InteractiveBrowserCredentialOptions
		{
			ClientId = o.ClientId,
			TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = o.CacheName },
		};
		if (!string.IsNullOrWhiteSpace(o.TenantId))
			options.TenantId = o.TenantId;
		if (!string.IsNullOrWhiteSpace(o.LoginHint))
			options.LoginHint = o.LoginHint;

		// Reuse a saved account so we don't prompt again across restarts.
		AuthenticationRecord? record = await TryLoadAuthRecordAsync(recordPath, ct);
		if (record is not null)
			options.AuthenticationRecord = record;

		var credential = new InteractiveBrowserCredential(options);

		if (record is null)
		{
			// First sign-in: prompt once, then persist the account record for silent re-acquire.
			AuthenticationRecord created = await credential.AuthenticateAsync(new TokenRequestContext(scopes), ct);
			await SaveAuthRecordAsync(recordPath, created, ct);
		}

		return credential;
	}

	private static async Task<AuthenticationRecord?> TryLoadAuthRecordAsync(string path, CancellationToken ct)
	{
		if (!File.Exists(path))
			return null;
		try
		{
			await using FileStream stream = File.OpenRead(path);
			return await AuthenticationRecord.DeserializeAsync(stream, ct);
		}
		catch
		{
			return null; // corrupt/incompatible — fall back to a fresh interactive sign-in.
		}
	}

	private static async Task SaveAuthRecordAsync(string path, AuthenticationRecord record, CancellationToken ct)
	{
		await using FileStream stream = File.Create(path);
		await record.SerializeAsync(stream, ct);
	}

	private static void Require(string? value, string name, EntraAuthMode mode)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidOperationException($"{name} is required for {mode} auth.");
	}
}
