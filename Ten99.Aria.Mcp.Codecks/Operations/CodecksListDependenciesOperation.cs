using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Reads a card's dependencies: <c>inDeps</c> (cards it depends on / is blocked by) and <c>outDeps</c>
/// (cards that depend on it / it blocks), plus the derived <c>hasBlockingDeps</c> / <c>isBlockingDep</c>.
/// </summary>
[Description("list a card's dependencies")]
internal class CodecksListDependenciesOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksListDependenciesOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest(string cardId) => new
	{
		query = new Dictionary<string, object>
		{
			[$"card({cardId})"] = new object[]
			{
				"cardId", "title", "hasBlockingDeps", "isBlockingDep",
				new Dictionary<string, object> { ["inDeps"] = new[] { "cardId", "title", "derivedStatus" } },
				new Dictionary<string, object> { ["outDeps"] = new[] { "cardId", "title", "derivedStatus" } },
			},
		},
	};
}
