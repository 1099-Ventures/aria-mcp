using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>Org domain (<c>org_*</c>) — account/auth check and multi-org list/switch (ADO #437).</summary>
[McpServerToolType]
public static class CodecksOrgTool
{
	[McpServerTool(Name = "org_get_account_info")]
	[Description("✅ TESTED & READY - Get the account info for the current API token (id, name, isDisabled). Serves as a token-validation / whoami check: a valid response contains a populated 'account' object. If it returns empty data, or a token_expired / missing_scope / token_account_mismatch error, the Codecks API token is invalid, expired, or lacks the required scope — update the org's token in the MCP config.")]
	public static Task<CallToolResult> GetAccountInfo(IConfiguration config, HttpClient httpClient)
		=> new CodecksGetAccountInfoOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient);

	[McpServerTool(Name = "org_list")]
	[Description("List configured Codecks organizations and which one is currently active. Switch with org_switch. Tokens are never returned — only a hasToken flag. Returns {count, activeOrg, usingLegacyArgs, orgs:[…]} — top-level count is 0 when no orgs are configured, i.e. the MCP runs in legacy single-org mode (--token/--account).")]
	public static CallToolResult ListOrgs(IConfiguration config)
	{
		CodecksConfiguration cfg = config.GetSection("Codecks").Get<CodecksConfiguration>() ?? new CodecksConfiguration();
		string? current = CodecksOrgState.CurrentKey(cfg);

		var orgs = cfg.Orgs.Select(o => new
		{
			key = o.Key,
			account = o.Account,
			hasToken = !string.IsNullOrEmpty(o.Token),
			isActive = o.Key == current
		}).ToArray();

		// #479: top-level count (0 on empty → legacy single-org mode) so callers tell empty from failure.
		var payload = new { count = orgs.Length, activeOrg = current, usingLegacyArgs = orgs.Length == 0, orgs };

		return new CallToolResult { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload) }] };
	}

	[McpServerTool(Name = "org_switch")]
	[Description("Switch the active Codecks organization by key (see org_list). Applies to subsequent calls in this MCP session; resets to the configured default org on restart.")]
	public static CallToolResult SwitchOrg(IConfiguration config, string orgKey)
	{
		CodecksConfiguration cfg = config.GetSection("Codecks").Get<CodecksConfiguration>() ?? new CodecksConfiguration();
		bool switched = CodecksOrgState.TrySwitch(cfg, orgKey);

		string text = switched
			? JsonSerializer.Serialize(new { switched = true, activeOrg = orgKey })
			: JsonSerializer.Serialize(new
			{
				switched = false,
				activeOrg = CodecksOrgState.CurrentKey(cfg),
				error = $"Unknown org key '{orgKey}'. Use org_list to see configured orgs."
			});

		return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
	}
}
