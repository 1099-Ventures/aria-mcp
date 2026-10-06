using ModelContextProtocol.Protocol;
using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Lists the account's run (sprint) configs — the schedules that auto-generate runs: cadence, start
/// weekday, end time, upcoming count, label template, and auto-assign behaviour.
/// </summary>
[Description("list run (sprint) configs")]
internal class CodecksListRunConfigsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksListRunConfigsOperation>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest() => new
	{
		query = new
		{
			_root = new object[]
			{
				new { account = new object[]
				{
					new { sprintConfigs = new[]
					{
						"id", "name", "color", "isGlobal", "sprintDurationWeeks", "sprintStartWeekday",
						"endHour", "endHourTimezone", "upcomingSprints", "runLabelTemplate", "moveOnFinish",
						"autoAssignNewCard", "autoAssignStartedCard", "stopOn",
					}},
				}},
			},
		},
	};

	protected override CallToolResult FormatResponse(string response)
		=> FormatListResponse(response, "sprintConfig", "runConfigs");
}
