using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;
using Ten99.Aria.Mcp.Ado.Operations;

namespace Ten99.Aria.Mcp.Ado;

/// <summary>
/// Multi-org meta tools — list/switch organizations and identity. Native multi-org state lives in
/// <see cref="AdoOrgState"/>. One tool class per domain (org / core / work-items / … pipelines,
/// iterations) so the surface stays readable as it grows.
/// </summary>
[McpServerToolType]
public static class AdoOrgTool
{
	[McpServerTool(Name = "org_list")]
	[Description("List Azure DevOps organizations from the registry and which one is active. Switch with org_switch. Secrets and client-id guids are never returned — only a hasPat flag and a clientIdSource label ('default' = global Ado:Auth:ClientId | 'microsoft' = MS first-party ADO app | 'override' = the org's own overrideClientId). activeOrg is null when no per-project default is set (call org_switch first).")]
	public static CallToolResult ListOrganizations(IConfiguration config)
	{
		AdoConfiguration cfg = AdoConfigLoader.Load(config);
		string? current = AdoOrgState.CurrentKey(cfg);

		var orgs = cfg.Orgs.Select(o => new
		{
			key = o.Key,
			orgName = o.OrgName,
			authType = o.AuthType.ToString(),
			// Which client id interactive auth will use — friendly label, never the raw guid.
			clientIdSource = !string.IsNullOrWhiteSpace(o.OverrideClientId) ? "override"
				: o.UseMicrosoftClientId ? "microsoft"
				: "default",
			domains = o.Domains,
			hasPat = !string.IsNullOrEmpty(o.Pat),
			isActive = o.Key == current
		}).ToArray();

		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { activeOrg = current, orgs }) }]
		};
	}

	[McpServerTool(Name = "org_switch")]
	[Description("Switch the active Azure DevOps organization by key (see org_list). Applies to subsequent calls in this session; resets to the configured default org on restart.")]
	public static CallToolResult SwitchOrganization(IConfiguration config, string orgKey)
	{
		AdoConfiguration cfg = AdoConfigLoader.Load(config);
		bool switched = AdoOrgState.TrySwitch(cfg, orgKey);

		string text = switched
			? JsonSerializer.Serialize(new { switched = true, activeOrg = orgKey })
			: JsonSerializer.Serialize(new
			{
				switched = false,
				activeOrg = AdoOrgState.CurrentKey(cfg),
				error = $"Unknown org key '{orgKey}'. Use org_list to see configured orgs."
			});

		return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
	}

	[McpServerTool(Name = "org_get_current_identity")]
	[Description("Who/where am I now — returns the active org and the PAT's authenticated user {org, orgName, userId, displayName} via _apis/connectionData. Also a PAT validity check.")]
	public static Task<CallToolResult> GetCurrentIdentity(IConfiguration config, HttpClient httpClient)
		=> new AdoGetCurrentIdentityOperation().Execute(config, httpClient);
}
