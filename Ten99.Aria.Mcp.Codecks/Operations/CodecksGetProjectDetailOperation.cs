using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations
{
	internal class CodecksGetProjectDetailOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		: CodecksOperationBaseExtended<CodecksGetProjectDetailOperation, CodecksDetailRequest>(rateLimiter, ref lastReset)
	{
		protected override object BuildRequest(CodecksDetailRequest request)
		{
			return new
			{
				query = new Dictionary<string, object>
				{
					[request.ToEntityQuery("project")] = new object[]
					{
						// Core fields (verified working)
						"id", "name", "createdAt", "defaultUserAccess", "isPublic",
						"publicHeading", "publicMessage", "visibility",
						
						// Enhanced fields (API documented)
						"accountSeq", "markerColor", "allowUpvotes", "commentsArePublic", "spaces",
						
						// Count fields for related data
						"count:decks", "count:explicitProjectUsers", "count:activities", "count:tags",
						
						// Account relation
						new { account = new string[] { "id", "name" } },
						
						// Deck details with enhanced filter
						new Dictionary<object, object>
						{
							[Filters.FilterDecks] = new object[] {
								"id", "title", "description", "createdAt", "sortValue", "spaceId",
								"count:cards",
								new { creator = new string[] { "id", "name" } }
							}
						}
					}
				}
			};
		}
	}
}