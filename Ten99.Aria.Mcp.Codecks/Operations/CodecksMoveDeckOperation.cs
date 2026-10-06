using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Moves a deck into a space (and positions it) via <c>dispatch/decks/addToSpaceAfter</c>. Supports
/// cross-project moves (targetProjectId) and placement after a sibling deck (targetId).
/// </summary>
[Description("move a deck to a space")]
internal class CodecksMoveDeckOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksMoveDeckOperation, CodecksMoveDeckRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/decks/addToSpaceAfter";

	protected override object BuildRequest(CodecksMoveDeckRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.DeckId)) throw new ArgumentException("deckId is required.");
		if (string.IsNullOrWhiteSpace(request.TargetProjectId)) throw new ArgumentException("targetProjectId is required.");

		return new Dictionary<string, object?>
		{
			["deckIds"] = new[] { request.DeckId },
			["targetId"] = request.AfterDeckId,
			["targetProjectId"] = request.TargetProjectId,
			["targetSpaceId"] = request.TargetSpaceId,
		};
	}
}
