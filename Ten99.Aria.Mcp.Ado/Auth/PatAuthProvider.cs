using System.Net.Http.Headers;
using System.Text;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Auth;

/// <summary>
/// PAT auth: Personal Access Token per org, sent as <c>Authorization: Basic base64(":" + pat)</c>
/// (the Azure DevOps REST convention — empty username, PAT as password). Synchronous under the hood;
/// exposed async to satisfy the shared <see cref="IAdoAuthProvider"/> seam.
/// </summary>
internal sealed class PatAuthProvider : IAdoAuthProvider
{
	public Task<AuthenticationHeaderValue> GetAuthHeaderAsync(AdoOrg org, AdoAuthContext context, CancellationToken ct = default)
	{
		if (string.IsNullOrEmpty(org.Pat))
			throw new InvalidOperationException(
				$"No PAT for org '{org.Key}' ({org.OrgName}). Set Ado:Pats:{org.Key} (env Ado__Pats__{org.Key}) — the registry stays secret-free.");

		string basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{org.Pat}"));
		return Task.FromResult(new AuthenticationHeaderValue("Basic", basic));
	}
}
