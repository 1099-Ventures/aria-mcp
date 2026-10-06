using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Creates a new deck in a Codecks project via <c>dispatch/decks/create</c>. Producer-tier action.
/// </summary>
[Description("Create a new deck in a Codecks project")]
internal class CodecksCreateDeckOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksCreateDeckOperation, CodecksCreateDeckRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/decks/create";

	protected override object BuildRequest(CodecksCreateDeckRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.ProjectId))
			throw new ArgumentException("projectId is required to create a deck.");
		if (string.IsNullOrWhiteSpace(request.Title))
			throw new ArgumentException("title is required to create a deck.");

		var payload = new Dictionary<string, object?>
		{
			{ "projectId", request.ProjectId },
			{ "title", request.Title },
		};

		if (!string.IsNullOrWhiteSpace(request.Description))
			payload["description"] = request.Description;

		if (request.SpaceId.HasValue)
			payload["spaceId"] = request.SpaceId.Value;

		return payload;
	}
}
