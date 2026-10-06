using System.Net.Http.Headers;
using Azure.Core;
using Ten99.Aria.Identity.EntraId;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Auth;

/// <summary>
/// Managed-identity auth for the Azure DevOps REST resource — for agents deployed on Azure. Uses the
/// shared <see cref="EntraCredentialFactory"/>: system-assigned by default (a silent login, no client
/// id), or user-assigned when <c>Ado:Auth:ManagedIdentityClientId</c> names the identity. The identity
/// must be added to the target Azure DevOps organization and granted access. No tenant discovery — a
/// managed identity is scoped to its own tenant. See ADO #431.
/// </summary>
internal sealed class ManagedIdentityAuthProvider : IAdoAuthProvider
{
	const string AdoScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";

	static TokenCredential? _credential;
	static readonly object _gate = new();

	public async Task<AuthenticationHeaderValue> GetAuthHeaderAsync(AdoOrg org, AdoAuthContext context, CancellationToken ct = default)
	{
		// System-assigned needs nothing; user-assigned needs only its client id — never routed through the
		// SP secret/tenant guard.
		TokenCredential credential = _credential ??= BuildCredential(context.Settings);
		AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([AdoScope]), ct);
		return new AuthenticationHeaderValue("Bearer", token.Token);
	}

	static TokenCredential BuildCredential(AdoAuthSettings settings)
	{
		lock (_gate)
			return _credential ??= EntraCredentialFactory.CreateManagedIdentity(new EntraCredentialOptions
			{
				ClientId = settings.ManagedIdentityClientId, // null => system-assigned
			});
	}
}
