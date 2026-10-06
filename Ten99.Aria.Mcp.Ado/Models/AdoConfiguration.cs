using Ten99.Aria.Common.Attributes;

/*
 Settings-only. The org LIST comes from the global registry file, not from here:
   {home}/.claude/.ado-mcp-orgs.json   (map of key -> { orgName, authType, domains[] }, no secrets)

 This section carries only the per-instance settings, sourced through the normal config chain
 (a --config json, env vars, or CLI switches):
   {
     "Ado": {
       "DefaultOrg": "1099",              // or CLI --default-org 1099  /  env Ado__DefaultOrg
       "RegistryPath": "/alt/path.json",  // optional override of the {home}/.claude default
       "IndentResponses": false,
       "Pats": { "1099": "<pat>" }        // only for authType=pat orgs; env Ado__Pats__1099
     }
   }
 */

namespace Ten99.Aria.Mcp.Ado.Models
{
	[ConfigurationSection("Ado")]
	public class AdoConfiguration
	{
		/// <summary>Key of the org selected on startup. NO fallback — when unset the MCP starts with
		/// no active org and the first call must <c>ado_switch_organization</c>. Set via
		/// <c>--default-org</c> (mapped to <c>Ado:DefaultOrg</c>) or env <c>Ado__DefaultOrg</c>.</summary>
		public string? DefaultOrg { get; set; }

		/// <summary>Optional override of the global registry path. Defaults to
		/// <c>{home}/.claude/.ado-mcp-orgs.json</c> when unset.</summary>
		public string? RegistryPath { get; set; }

		/// <summary>The available organizations. Populated from the registry file at load
		/// (see <c>AdoConfigLoader</c>); not bound from config directly.</summary>
		public List<AdoOrg> Orgs { get; set; } = [];

		/// <summary>PATs for <c>authType=pat</c> orgs, keyed by org key. Kept out of the registry;
		/// supplied via <c>Ado:Pats:{Key}</c> / env <c>Ado__Pats__{Key}</c>.</summary>
		public Dictionary<string, string> Pats { get; set; } = [];

		/// <summary>Cross-cutting auth settings (Entra app client id, cache dir) for the AAD providers.</summary>
		public AdoAuthSettings Auth { get; set; } = new();

		/// <summary>When true, responses are pretty-printed for debugging; defaults to compact.</summary>
		public bool IndentResponses { get; set; }
	}
}
