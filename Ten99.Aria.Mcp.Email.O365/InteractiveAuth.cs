namespace Ten99.Aria.Mcp.Email.O365;

/// <summary>
/// Shared derivation of interactive-auth parameters from the configured account, so every code path
/// (tools and operations) isolates its token cache the same way. The cache name MUST be unique per
/// sign-in account — otherwise side-by-side MCP instances for different mailboxes collide on one cache
/// and clobber each other's auth (#509). The account UPN also serves as the sign-in login hint.
/// </summary>
internal static class InteractiveAuth
{
	/// <summary>Per-account token-cache name. Keyed by the sign-in account (the configured mailbox),
	/// sanitized to a cache/file-safe segment.</summary>
	public static string CacheName(string? account)
	{
		string id = string.IsNullOrWhiteSpace(account) ? "default" : account.Trim().ToLowerInvariant();
		string safe = new([.. id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-')]);
		return $"aria-o365-mcp.{safe}";
	}
}
