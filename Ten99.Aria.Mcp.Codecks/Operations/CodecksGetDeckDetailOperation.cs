using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations
{
	internal class CodecksGetDeckDetailOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		: CodecksOperationBaseExtended<CodecksGetDeckDetailOperation, CodecksDetailRequest>(rateLimiter, ref lastReset)
	{
		protected override object BuildRequest(CodecksDetailRequest request)
		{
			return new
			{
				query = new Dictionary<string, object>
				{
					[request.ToEntityQuery("deck")] = new object[]
					{
						// Core verified fields from Deck-Fields.md
						"id", "title", "description", "createdAt", "sortValue",
						"accountSeq", "isDeleted", "hasGuardians", "spaceId",
						
						// API documented fields (conservative selection)
						"accountSeq", "isDeleted", "handSyncEnabled", "hasGuardians",
						
						// Relations
						new { project = new string[] { "id", "name" } },
						new { creator = new string[] { "id", "name" } },
						
						// Count fields (verified working)
						"count:cards", "count:guardians",
						
						// Card preview using direct cards query (filters don't work for cards)
						new Dictionary<object, object>
						{
							["cards"] = new object[]
							{
								"cardId", "title", "effort", "createdAt",
								new { assignee = new string[] { "id", "name" } }
							}
						}
					}
				}
			};
		}
	}
}