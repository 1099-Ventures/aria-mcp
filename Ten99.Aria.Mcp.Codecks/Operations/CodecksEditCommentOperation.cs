using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Edits a comment entry via <c>dispatch/resolvables/updateComment</c>.</summary>
[Description("edit a comment")]
internal class CodecksEditCommentOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksEditCommentOperation, CodecksEditCommentRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/resolvables/updateComment";

	protected override object BuildRequest(CodecksEditCommentRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.EntryId)) throw new ArgumentException("entryId is required.");
		if (string.IsNullOrWhiteSpace(request.Content)) throw new ArgumentException("content is required.");

		return new Dictionary<string, object?> { ["entryId"] = request.EntryId, ["content"] = request.Content };
	}
}
