using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Deletes a Codecks deck via <c>dispatch/decks/delete</c>. Producer-tier action. The payload is just
/// the deck id (the UI's sessionId field is not required for token auth).
/// </summary>
[Description("Delete a Codecks deck")]
internal class CodecksDeleteDeckOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksDeleteDeckOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/decks/delete";

	protected override object BuildRequest(string deckId)
	{
		if (string.IsNullOrWhiteSpace(deckId))
			throw new ArgumentException("deckId is required to delete a deck.");

		return new Dictionary<string, object?> { { "id", deckId } };
	}
}
