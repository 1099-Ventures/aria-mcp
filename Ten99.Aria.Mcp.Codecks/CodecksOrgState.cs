using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks;

/// <summary>
/// Process-wide active-org selection for the Codecks MCP. The org list is read fresh from
/// configuration on each call; only the current selection is retained in memory and resets to
/// the default org when the MCP process restarts (no cross-restart persistence by design).
/// </summary>
internal static class CodecksOrgState
{
	private static readonly object _gate = new();
	private static string? _currentKey;

	/// <summary>The active org key, or null when no orgs are configured.</summary>
	public static string? CurrentKey(CodecksConfiguration config)
	{
		if (config.Orgs is not { Count: > 0 })
			return null;

		lock (_gate)
		{
			_currentKey ??= config.DefaultOrg ?? config.Orgs[0].Key;
			return _currentKey;
		}
	}

	/// <summary>Resolves the active org's account slug and token, or null when no orgs are configured.</summary>
	public static (string Account, string? Token)? Resolve(CodecksConfiguration config)
	{
		string? key = CurrentKey(config);
		if (key is null)
			return null;

		CodecksOrg org = config.Orgs.FirstOrDefault(o => o.Key == key) ?? config.Orgs[0];
		return (org.Account, org.Token);
	}

	/// <summary>Switches the active org. Returns false when the key is not a configured org.</summary>
	public static bool TrySwitch(CodecksConfiguration config, string key)
	{
		if (config.Orgs?.Any(o => o.Key == key) != true)
			return false;

		lock (_gate)
			_currentKey = key;

		return true;
	}
}
