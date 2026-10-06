using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Applies a journey to a card via <c>dispatch/workflows/apply</c>: the card becomes the Hero and the
/// given workflow items are generated as real cards under it. Returns the created card ids.
/// </summary>
[Description("apply a journey to a card")]
internal class CodecksApplyJourneyOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksApplyJourneyOperation, CodecksApplyJourneyRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/workflows/apply";

	protected override object BuildRequest(CodecksApplyJourneyRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.CardId)) throw new ArgumentException("cardId is required.");
		if (request.ItemIds is null || request.ItemIds.Length == 0) throw new ArgumentException("itemIds is required.");

		return new Dictionary<string, object?> { ["cardId"] = request.CardId, ["itemIds"] = request.ItemIds };
	}
}
