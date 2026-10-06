using ModelContextProtocol.Protocol;
using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Lists a card's comments. Comments are resolvableEntries (the messages) grouped into resolvables
/// (threads). Returns the entries with their content/author, plus the thread state (isClosed) so a caller
/// can correlate by resolvableId.
/// </summary>
[Description("list a card's comments")]
internal class CodecksListCommentsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksListCommentsOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest(string cardId)
	{
		const string entriesKey = "resolvableEntries({\"$order\":[\"createdAt\"]})";
		return new
		{
			query = new Dictionary<string, object>
			{
				[$"card({cardId})"] = new object[]
				{
					new Dictionary<string, object>
					{
						[entriesKey] = new object[]
						{
							"entryId", "content", "createdAt", "lastChangedAt", "resolvableId",
							new Dictionary<string, object> { ["author"] = new[] { "id", "name" } },
						},
					},
					new Dictionary<string, object> { ["resolvables"] = new object[] { "id", "context", "isClosed", "createdAt" } },
				},
			},
		};
	}

	protected override CallToolResult FormatResponse(string response)
		=> FormatListResponse(response, "resolvableEntry", "comments");
}
