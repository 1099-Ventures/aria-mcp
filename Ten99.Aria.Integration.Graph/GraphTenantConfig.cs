namespace Ten99.Aria.Integration.Graph;

/// <summary>
/// Configuration for a specific Microsoft 365 tenant's Graph API access.
/// Supports client credential, delegated token (MSAL), and managed identity auth.
/// </summary>
public record GraphTenantConfig
{
	public required string TenantId { get; init; }
	public required string ClientId { get; init; }
	public string? ClientSecret { get; init; }
	public string[] DefaultScopes { get; init; } = ["https://graph.microsoft.com/.default"];
	public GraphAuthMode AuthMode { get; init; } = GraphAuthMode.ClientCredential;

	/// <summary>
	/// Mailbox/UPN for delegated flows. Required when AuthMode is DelegatedToken.
	/// </summary>
	public string? UserIdentifier { get; init; }

	/// <summary>
	/// Delegated permission scopes for user token flows.
	/// </summary>
	public string[] DelegatedScopes { get; init; } =
		["Mail.ReadWrite", "Mail.ReadWrite.Shared", "Mail.Send", "offline_access"];
}
