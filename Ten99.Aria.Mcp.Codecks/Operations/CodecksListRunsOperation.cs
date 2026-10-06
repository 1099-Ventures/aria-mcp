using ModelContextProtocol.Protocol;
using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Lists the account's runs (Codecks sprints), newest first. Runs are auto-created on a schedule from a
/// sprint config (see <see cref="CodecksListRunConfigsOperation"/>).
/// </summary>
[Description("list runs (sprints)")]
internal class CodecksListRunsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksListRunsOperation>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest()
	{
		const string key = "sprints({\"$order\":\"-endDate\",\"isDeleted\":false})";
		return new
		{
			query = new
			{
				_root = new object[]
				{
					new { account = new object[]
					{
						new Dictionary<string, object>
						{
							[key] = new object[] { "id", "name", "description", "startDate", "endDate", "completedAt", "lockedAt", "accountSeq", "index", "sprintConfigId", "stats" },
						},
					}},
				},
			},
		};
	}

	protected override CallToolResult FormatResponse(string response)
		=> FormatListResponse(response, "sprint", "runs");
}
