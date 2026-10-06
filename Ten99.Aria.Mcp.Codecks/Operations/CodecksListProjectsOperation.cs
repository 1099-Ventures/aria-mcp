using ModelContextProtocol.Protocol;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations
{
	internal class CodecksListProjectsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		: CodecksOperationBaseExtended<CodecksListProjectsOperation, CodecksDetailRequest>(rateLimiter, ref lastReset)
	{
		protected override object BuildRequest(CodecksDetailRequest listRequest)
		{
			// Always return project stats (count:decks). Inlined deck objects are opt-in via
			// IncludeDecks and trimmed to actionable fields (no bulky description) to stay under
			// the MCP token cap. spaceId is kept so callers can scope deck/card searches to a space.
			List<object> projectFields = new()
			{
				"id", "name", "createdAt", "defaultUserAccess", "isPublic", "publicHeading",
				"publicMessage", "visibility", "spaces", "count:decks",
			};

			if (listRequest.IncludeDecks)
			{
				projectFields.Add(new Dictionary<object, object>
				{
					[Filters.FilterDecks] = new string[] { "id", "title", "spaceId", "count:cards" },
				});
			}

			int limit = listRequest.Limit ?? 20;

			return new
			{
				query = new Dictionary<string, object>
				{
					[listRequest.ToEntityQuery("account")] = new object[]
					{
						new Dictionary<object, object?>
						{
							[Filters.FilterProjectsPaginated(listRequest.Offset, limit)] = projectFields.ToArray(),
						},
						"count:projects",
					},
				},
			};
		}

		// #479: flatten the `project` map to {count, projects[]}, keeping the account map.
		protected override CallToolResult FormatResponse(string response)
			=> FormatListResponse(response, "project", "projects");
	}
}