using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Deletes project tags by id via <c>dispatch/projects/deleteTags</c> (payload <c>{ ids: [...] }</c>).</summary>
[Description("delete project tags")]
internal class CodecksDeleteTagsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksDeleteTagsOperation, string[]>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/projects/deleteTags";

	protected override object BuildRequest(string[] ids)
	{
		if (ids is null || ids.Length == 0) throw new ArgumentException("at least one tag id is required.");
		return new Dictionary<string, object?> { ["ids"] = ids };
	}
}
