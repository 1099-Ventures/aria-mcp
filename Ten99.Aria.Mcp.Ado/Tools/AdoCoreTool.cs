using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Ado.Operations;

namespace Ten99.Aria.Mcp.Ado;

/// <summary>Core tools — projects, teams, and other org-level structure.</summary>
[McpServerToolType]
public static class AdoCoreTool
{
	[McpServerTool(Name = "core_list_projects")]
	[Description("List projects in the active organization. Returns lean {id, name, state, visibility}. OPTIONAL: includeDescription (bool, default false).")]
	public static Task<CallToolResult> ListProjects(IConfiguration config, HttpClient httpClient, bool includeDescription = false)
		=> new AdoListProjectsOperation().Execute(config, httpClient, includeDescription);
}
