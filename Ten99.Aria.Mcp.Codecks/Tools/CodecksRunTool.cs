using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>
/// Run domain (<c>run_*</c>) — Codecks sprints. V1 is read-only: list runs and their schedules. Runs are
/// auto-created from a sprint config. Assign a card to a run via <c>card_update</c>'s sprintId. Creating or
/// editing run configs is a future admin-tier capability.
/// </summary>
[McpServerToolType]
public static class CodecksRunTool
{
	[McpServerTool(Name = "run_list")]
	[Description("List the account's runs (Codecks sprints), newest first. Returns {count, runs:[{id, name, description, startDate, endDate, completedAt, lockedAt, accountSeq, index, sprintConfigId, stats}]}. Runs are auto-created from a run config (see run_config_list). Assign a card to a run with card_update's sprintId.")]
	public static Task<CallToolResult> ListRuns(IConfiguration config, HttpClient httpClient)
		=> new CodecksListRunsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient);

	[McpServerTool(Name = "run_config_list")]
	[Description("List the account's run (sprint) configs — the schedules that auto-generate runs. Returns {count, runConfigs:[{id, name, color, isGlobal, sprintDurationWeeks, sprintStartWeekday, endHour, endHourTimezone, upcomingSprints, runLabelTemplate, moveOnFinish, autoAssignNewCard, autoAssignStartedCard, stopOn}]}. Read-only (creating/editing configs is a future admin-tier capability).")]
	public static Task<CallToolResult> ListRunConfigs(IConfiguration config, HttpClient httpClient)
		=> new CodecksListRunConfigsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient);
}
