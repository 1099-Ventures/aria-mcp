using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>
/// Milestone domain (<c>milestone_*</c>). Milestones are account-level, linked to projects. Assign a card
/// to a milestone via <c>card_update</c>'s milestoneId.
/// </summary>
[McpServerToolType]
public static class CodecksMilestoneTool
{
	[McpServerTool(Name = "milestone_list")]
	[Description("List a project's milestones (ordered by date). REQUIRED: projectId. Returns {count, milestones:[{id, name, date, startDate, color, isGlobal, accountSeq}]}.")]
	public static Task<CallToolResult> ListMilestones(IConfiguration config, HttpClient httpClient, string projectId)
		=> new CodecksListMilestonesOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, projectId);

	[McpServerTool(Name = "milestone_create")]
	[Description("Create a milestone. REQUIRED: name (string), date (YYYY-MM-DD), color (token, e.g. 'blue' or 'pink'; the API validates), projectIds (string array - the projects it covers). OPTIONAL: startDate (YYYY-MM-DD), isGlobal (bool, default false - account-wide). The accountId is resolved from the token. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> CreateMilestone(IConfiguration config, HttpClient httpClient, string name, string date, string color, string[] projectIds, string? startDate = null, bool isGlobal = false)
		=> new CodecksCreateMilestoneOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, new CodecksCreateMilestoneRequest { Name = name, Date = date, Color = color, ProjectIds = projectIds ?? [], StartDate = startDate, IsGlobal = isGlobal });

	[McpServerTool(Name = "milestone_update")]
	[Description("Update a milestone. REQUIRED: id. OPTIONAL (provide at least one): name, date (YYYY-MM-DD), startDate (YYYY-MM-DD), color (token), isGlobal (bool), projectIds (string array - replaces the project links), manualOrderLabels (string array - the milestone board's grouping columns). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> UpdateMilestone(IConfiguration config, HttpClient httpClient, string id, string? name = null, string? date = null, string? startDate = null, string? color = null, bool? isGlobal = null, string[]? projectIds = null, string[]? manualOrderLabels = null)
		=> new CodecksUpdateMilestoneOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksUpdateMilestoneRequest { Id = id, Name = name, Date = date, StartDate = startDate, Color = color, IsGlobal = isGlobal, ProjectIds = projectIds, ManualOrderLabels = manualOrderLabels });

	[McpServerTool(Name = "milestone_delete")]
	[Description("Delete a milestone by id. REQUIRED: id. Permanent. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> DeleteMilestone(IConfiguration config, HttpClient httpClient, string id)
		=> new CodecksDeleteMilestoneOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, id);
}
