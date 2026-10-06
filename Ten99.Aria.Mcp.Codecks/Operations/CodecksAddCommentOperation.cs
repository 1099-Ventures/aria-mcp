using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Adds a comment to a card via <c>dispatch/resolvables/create</c> (context "comment").</summary>
[Description("add a comment to a card")]
internal class CodecksAddCommentOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksAddCommentOperation, CodecksAddCommentRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/resolvables/create";

	protected override object BuildRequest(CodecksAddCommentRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.CardId)) throw new ArgumentException("cardId is required.");
		if (string.IsNullOrWhiteSpace(request.Content)) throw new ArgumentException("content is required.");

		return new Dictionary<string, object?>
		{
			["cardId"] = request.CardId,
			["context"] = string.IsNullOrWhiteSpace(request.Context) ? "comment" : request.Context,
			["content"] = request.Content,
		};
	}
}
