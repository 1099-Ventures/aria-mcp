using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Gets one published workflow (Journey) template by id, including its items, step-zone labels and
/// deck map (needed to clone it).
/// </summary>
[Description("get a published workflow template")]
internal class CodecksGetTemplateOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksGetTemplateOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest(string templateId)
	{
		string key = $"publishedWorkflowTemplates({{\"id\":\"{templateId}\"}})";
		const string itemsKey = "items({\"$order\":\"sortValue\"})";
		return new
		{
			query = new
			{
				_root = new object[]
				{
					new Dictionary<string, object>
					{
						[key] = new object[]
						{
							"id", "name", "description", "authorName", "tags", "isStaffPick",
							"lastUpdatedAt", "itemOrderLabels", "deckLabelMap", "cloneCount",
							new Dictionary<string, object> { [itemsKey] = new object[] { "orderLabel", "id", "title", "effort", "priority" } },
						},
					},
				},
			},
		};
	}
}
