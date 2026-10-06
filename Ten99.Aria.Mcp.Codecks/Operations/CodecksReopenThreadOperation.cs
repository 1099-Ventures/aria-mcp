using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Reopens a comment thread (resolvable) via <c>dispatch/resolvables/reopen</c>.</summary>
[Description("reopen a comment thread")]
internal class CodecksReopenThreadOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksReopenThreadOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/resolvables/reopen";

	protected override object BuildRequest(string resolvableId)
	{
		if (string.IsNullOrWhiteSpace(resolvableId)) throw new ArgumentException("resolvableId is required.");
		return new Dictionary<string, object?> { ["id"] = resolvableId };
	}
}
