using System.Net.Http.Headers;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Auth;

/// <summary>
/// Supplies the HTTP Authorization header for a given org. v1 shipped a PAT provider; the AAD
/// providers (interactive now, managed-identity / service-principal next) live behind this same
/// seam. Async because token acquisition is I/O (AAD + tenant discovery). See ADO #423.
/// </summary>
internal interface IAdoAuthProvider
{
	/// <summary>Builds the Authorization header for <paramref name="org"/>, or throws when it cannot.</summary>
	Task<AuthenticationHeaderValue> GetAuthHeaderAsync(AdoOrg org, AdoAuthContext context, CancellationToken ct = default);
}

/// <summary>Ambient dependencies a provider may need: the shared <see cref="HttpClient"/> (for tenant
/// discovery) and the resolved <see cref="AdoAuthSettings"/> (Entra app client id, cache dir).</summary>
internal sealed record AdoAuthContext(HttpClient Http, AdoAuthSettings Settings);
