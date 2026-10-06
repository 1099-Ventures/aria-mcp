using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Updates (renames / re-describes) an existing Codecks deck via <c>dispatch/decks/update</c>.
/// Producer-tier action.
/// </summary>
[Description("Update (rename) an existing Codecks deck")]
internal class CodecksUpdateDeckOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksUpdateDeckOperation, CodecksUpdateDeckRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/decks/update";

	protected override object BuildRequest(CodecksUpdateDeckRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.DeckId))
			throw new ArgumentException("deckId is required to update a deck.");
		if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.Description))
			throw new ArgumentException("Provide at least one of title or description to update.");

		var payload = new Dictionary<string, object?> { { "id", request.DeckId } };

		if (!string.IsNullOrWhiteSpace(request.Title))
			payload["title"] = request.Title;

		if (!string.IsNullOrWhiteSpace(request.Description))
			payload["description"] = request.Description;

		return payload;
	}
}
