using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Deletes a milestone via <c>dispatch/milestones/delete</c> (payload is just the id).</summary>
[Description("delete a milestone")]
internal class CodecksDeleteMilestoneOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksDeleteMilestoneOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/milestones/delete";

	protected override object BuildRequest(string id)
	{
		if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required.");
		return new Dictionary<string, object?> { ["id"] = id };
	}
}
