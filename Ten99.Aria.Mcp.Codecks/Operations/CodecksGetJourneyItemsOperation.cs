using ModelContextProtocol.Protocol;
using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Reads the workflow items of a journey deck (a deck that holds workflow items), so the item ids can be
/// fed to journey_apply. Works for both handcrafted journeys and ones cloned from a published template.
/// </summary>
[Description("get a journey deck's workflow items")]
internal class CodecksGetJourneyItemsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksGetJourneyItemsOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest(string deckId)
	{
		const string itemsKey = "workflowItems({\"$order\":\"sortValue\"})";
		return new
		{
			query = new Dictionary<string, object>
			{
				[$"deck({deckId})"] = new object[]
				{
					new Dictionary<string, object> { [itemsKey] = new object[] { "itemId", "title", "label", "content", "effort", "priority", "targetDeck" } },
				},
			},
		};
	}

	// Items come back under the `workflowItem` map.
	protected override CallToolResult FormatResponse(string response)
		=> FormatListResponse(response, "workflowItem", "items");
}
