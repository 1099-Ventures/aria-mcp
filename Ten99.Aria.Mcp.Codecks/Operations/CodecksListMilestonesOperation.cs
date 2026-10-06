using ModelContextProtocol.Protocol;
using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Lists a project's milestones (account-level milestones linked to the project), ordered by date.
/// </summary>
[Description("list a project's milestones")]
internal class CodecksListMilestonesOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksListMilestonesOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest(string projectId)
	{
		string key = $"milestones({{\"$order\":\"date\",\"isDeleted\":false,\"milestoneProjects\":{{\"projectId\":[\"{projectId}\"]}}}})";
		return new
		{
			query = new
			{
				_root = new object[]
				{
					new { account = new object[]
					{
						new Dictionary<string, object> { [key] = new object[] { "id", "name", "date", "startDate", "color", "isGlobal", "accountSeq" } },
					}},
				},
			},
		};
	}

	protected override CallToolResult FormatResponse(string response)
		=> FormatListResponse(response, "milestone", "milestones");
}
