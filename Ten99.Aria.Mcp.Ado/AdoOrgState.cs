using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado;

/// <summary>
/// Process-wide active-org selection for the ADO MCP. The org list is read fresh from the registry
/// on each call; only the current selection is retained in memory and resets when the MCP process
/// restarts (no cross-restart persistence by design). There is <b>no root fallback</b>: when no
/// per-project default is configured, the process starts with no active org and the first call must
/// <c>ado_switch_organization</c>. Mirrors <c>CodecksOrgState</c> so multi-org is first-class state.
/// </summary>
internal static class AdoOrgState
{
	private static readonly object _gate = new();
	private static string? _currentKey;

	/// <summary>The active org key: the per-project default (<c>Ado:DefaultOrg</c>) until switched,
	/// or null when neither a default nor a switch has selected one.</summary>
	public static string? CurrentKey(AdoConfiguration config)
	{
		if (config.Orgs is not { Count: > 0 })
			return null;

		lock (_gate)
		{
			// No fallback to the first org — an unset default means "no active org".
			_currentKey ??= config.DefaultOrg;
			return _currentKey;
		}
	}

	/// <summary>Resolves the active org, or null when none is selected or the selected key is
	/// not in the registry (no silent fallback to an arbitrary org).</summary>
	public static AdoOrg? Resolve(AdoConfiguration config)
	{
		string? key = CurrentKey(config);
		if (key is null)
			return null;

		return config.Orgs.FirstOrDefault(o => o.Key == key);
	}

	/// <summary>Switches the active org. Returns false when the key is not a configured org.</summary>
	public static bool TrySwitch(AdoConfiguration config, string key)
	{
		if (config.Orgs?.Any(o => o.Key == key) != true)
			return false;

		lock (_gate)
			_currentKey = key;

		return true;
	}
}
