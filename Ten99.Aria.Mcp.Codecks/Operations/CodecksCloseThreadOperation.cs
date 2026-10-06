using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Closes a resolvable (comment / block / review) via <c>dispatch/resolvables/close</c>. Optionally marks
/// the card done (used when closing a review).
/// </summary>
[Description("close a resolvable")]
internal class CodecksCloseThreadOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksCloseThreadOperation, CodecksCloseResolvableRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/resolvables/close";

	protected override object BuildRequest(CodecksCloseResolvableRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.ResolvableId)) throw new ArgumentException("resolvableId is required.");

		var payload = new Dictionary<string, object?> { ["id"] = request.ResolvableId };
		if (request.MarkCardDone.HasValue)
			payload["markCardDone"] = request.MarkCardDone.Value;
		return payload;
	}
}
