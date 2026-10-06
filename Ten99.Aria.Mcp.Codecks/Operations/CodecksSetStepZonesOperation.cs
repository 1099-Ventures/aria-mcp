using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Sets a journey deck's step zones (the <c>workflowItemOrderLabels</c> array) via
/// <c>dispatch/decks/update</c>. Index 0 is the implicit default/ungrouped zone (null); the named zones
/// follow in order. This is a full replace of the named zones.
/// </summary>
[Description("set a journey deck's step zones")]
internal class CodecksSetStepZonesOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksSetStepZonesOperation, CodecksSetStepZonesRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/decks/update";

	protected override object BuildRequest(CodecksSetStepZonesRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.DeckId)) throw new ArgumentException("deckId is required.");

		var labels = new List<object?> { null }; // default/ungrouped zone
		foreach (string zone in request.Zones)
			labels.Add(zone);

		return new Dictionary<string, object?> { ["id"] = request.DeckId, ["workflowItemOrderLabels"] = labels };
	}
}
