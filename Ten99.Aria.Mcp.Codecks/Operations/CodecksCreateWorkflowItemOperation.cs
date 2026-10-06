using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Adds a workflow (journey) item to a deck via <c>dispatch/workflows/createItem</c>. A deck that holds
/// workflow items is a Journey. <c>userId</c> is omitted — the server infers the actor from the token.
/// </summary>
[Description("create a workflow (journey) item")]
internal class CodecksCreateWorkflowItemOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksCreateWorkflowItemOperation, CodecksCreateWorkflowItemRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/workflows/createItem";

	protected override object BuildRequest(CodecksCreateWorkflowItemRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.DeckId)) throw new ArgumentException("deckId is required.");
		if (string.IsNullOrWhiteSpace(request.Content)) throw new ArgumentException("content is required.");

		var payload = new Dictionary<string, object?>
		{
			["deckId"] = request.DeckId,
			["content"] = request.Content,
			["masterTags"] = Array.Empty<object>(),
			["childCards"] = Array.Empty<object>(),
			["inDeps"] = Array.Empty<object>(),
			["outDeps"] = Array.Empty<object>(),
		};
		if (!string.IsNullOrWhiteSpace(request.AssigneeId)) payload["assigneeId"] = request.AssigneeId;
		if (request.Effort.HasValue) payload["effort"] = request.Effort.Value;
		if (!string.IsNullOrWhiteSpace(request.Priority)) payload["priority"] = request.Priority;
		if (!string.IsNullOrWhiteSpace(request.Label)) payload["label"] = request.Label;
		if (!string.IsNullOrWhiteSpace(request.TargetDeckId)) payload["targetDeckId"] = request.TargetDeckId;
		return payload;
	}
}
