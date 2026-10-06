using Azure.Core;

namespace Ten99.Aria.Integration.Graph;

/// <summary>
/// Simple TokenCredential that returns a pre-acquired access token.
/// Used to create a GraphServiceClient from an MSAL authentication result.
/// </summary>
internal class BearerTokenCredential(string accessToken, DateTimeOffset expiresOn) : TokenCredential
{
	public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
		=> new(accessToken, expiresOn);

	public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
		=> ValueTask.FromResult(new AccessToken(accessToken, expiresOn));
}
