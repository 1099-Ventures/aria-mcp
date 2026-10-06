using ModelContextProtocol.Protocol;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations
{
	internal class CodecksListDecksByProjectOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		: CodecksOperationBaseExtended<CodecksListDecksByProjectOperation, CodecksDetailRequest>(rateLimiter, ref lastReset)
	{
		protected override object BuildRequest(CodecksDetailRequest request)
		{
			return new
			{
				query = new Dictionary<string, object>
				{
					[request.ToEntityQuery("project")] = new object[]
					{
						new Dictionary<object, object?>
						{
							[Filters.FilterDecksPaginated(request.Offset)] = new object[]
							{
								// Core deck list fields - trimmed for performance
								"id", "title", "createdAt", "sortValue",
								"accountSeq", "isDeleted", "hasGuardians",
								"spaceId",
								
								// Strategic count fields (verified working)
								"count:cards", "count:guardians",
								
								// Minimal relations for context only
								new { project = new string[] { "id", "name" } },
								new { creator = new string[] { "id", "name" } }
							},
						},
						"count:decks" // Total deck count for the project
					}
				}
			};
		}

		// #479: flatten the `deck` map to {count, decks[]}, keeping project + creator maps for resolution.
		protected override CallToolResult FormatResponse(string response)
			=> FormatListResponse(response, "deck", "decks");
	}
}