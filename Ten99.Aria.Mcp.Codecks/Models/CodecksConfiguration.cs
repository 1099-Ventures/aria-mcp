using Ten99.Aria.Common.Attributes;

/*
 {
    "Codecks": {
      "DefaultOrg": "acme-studio",
      "Orgs": [
        { "Key": "acme-studio", "Account": "acme-studio", "Token": "cdxat_<org-api-token>" },
        { "Key": "acme-game", "Account": "<second-workspace-slug>", "Token": "cdxat_<org-api-token>" }
      ]
    }
  }
 */

namespace Ten99.Aria.Mcp.Codecks.Models
{
	[ConfigurationSection("Codecks")]
	public class CodecksConfiguration
	{
		/// <summary>Key of the org selected on startup. Falls back to the first configured org when unset.</summary>
		public string? DefaultOrg { get; set; }

		/// <summary>Configured organizations. When empty, the MCP uses the legacy --token/--account args.</summary>
		public List<CodecksOrg> Orgs { get; set; } = [];
	}
}