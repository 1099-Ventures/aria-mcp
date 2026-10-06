using ModelContextProtocol.Protocol;
using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Lists Codecks' published workflow (Journey) templates — the community gallery. Ordered by most-cloned.
/// </summary>
[Description("list published workflow templates")]
internal class CodecksListTemplatesOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksListTemplatesOperation, int>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest(int limit)
	{
		if (limit <= 0) limit = 25;
		// $limit on this relation requires an $order. Most-cloned first makes the gallery useful.
		string key = $"publishedWorkflowTemplates({{\"$order\":\"-cloneCount\",\"$limit\":{limit}}})";
		return new
		{
			query = new
			{
				_root = new object[]
				{
					new Dictionary<string, object>
					{
						[key] = new object[] { "id", "name", "description", "authorName", "tags", "isStaffPick", "count:items", "cloneCount" },
					},
				},
			},
		};
	}

	// Published templates come back under the `workflowTemplate` map.
	protected override CallToolResult FormatResponse(string response)
		=> FormatListResponse(response, "workflowTemplate", "templates");
}
