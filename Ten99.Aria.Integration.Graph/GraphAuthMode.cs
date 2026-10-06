namespace Ten99.Aria.Integration.Graph;

public enum GraphAuthMode
{
	ClientCredential,
	DelegatedToken,
	ManagedIdentity,

	/// <summary>Desktop interactive browser sign-in (public client) with a persistent token cache —
	/// for MCP/agent processes acting as the signed-in user (delegated Mail scopes). Distinct from
	/// <see cref="DelegatedToken"/>, which is the confidential-client web-app auth-code flow.</summary>
	Interactive
}
