using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Adds an emoji reaction to a comment entry via <c>dispatch/resolvables/addReaction</c>.</summary>
[Description("react to a comment")]
internal class CodecksReactCommentOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksReactCommentOperation, CodecksReactCommentRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/resolvables/addReaction";

	protected override object BuildRequest(CodecksReactCommentRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.EntryId)) throw new ArgumentException("entryId is required.");
		if (string.IsNullOrWhiteSpace(request.Emoji)) throw new ArgumentException("emoji is required.");

		return new Dictionary<string, object?>
		{
			["entryId"] = request.EntryId,
			["value"] = new Dictionary<string, object?> { ["type"] = "emoji", ["value"] = request.Emoji },
		};
	}
}
