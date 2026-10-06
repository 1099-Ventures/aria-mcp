using ModelContextProtocol.Protocol;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations
{
	internal class CodecksListCardsByDeckOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		: CodecksOperationBaseExtended<CodecksListCardsByDeckOperation, CodecksDetailRequest>(rateLimiter, ref lastReset)
	{
		protected override object BuildRequest(CodecksDetailRequest request)
		{
			return new
			{
				query = new Dictionary<string, object>
				{
					[request.ToEntityQuery("deck")] = new object[]
					{
						"id", "title",
						new Dictionary<object, object>()
						{
							[Filters.FilterCardsPaginated(request.Offset)] = new object[]
							{
								// Core Card List Fields - using cardId for cards
								"cardId", "accountSeq", "title", "content", "createdAt", "lastUpdatedAt",
								"effort", "status", "derivedStatus", "isDoc", "tags", "milestone", "sprint",
								"parentCard", "priority", "assignee", "creator", "dueDate", "childCardInfo",
								"checkboxInfo", "checkboxStats", "hasBlockingDeps", "mentionedUsers",

								// Count Fields 
								"count:handCards",
								"count:attachments",
								"count:upvotes",
								"count:cardOrders",
							
								// Context and Relations
								new { assignee = new string[] { "id", "name", "fullName" } },
								new { creator = new string[] { "id", "name", "fullName" } },
								new { deck = new string[] { "id", "title" } },
								new { milestone = new string [] {"id", "name", "date" } } ,
							},
						},
						"count:cards"
					},
				}
			};
		}

		// #479: flatten the `card` map to {count, cards[]}, keeping deck + relation maps for resolution.
		protected override CallToolResult FormatResponse(string response)
			=> FormatListResponse(response, "card", "cards");
	}
}