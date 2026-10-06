using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Edits a project tag via <c>dispatch/projects/updateTag</c>. Only supplied fields are sent.</summary>
[Description("update a project tag")]
internal class CodecksUpdateTagOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksUpdateTagOperation, CodecksUpdateTagRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/projects/updateTag";

	protected override object BuildRequest(CodecksUpdateTagRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Id)) throw new ArgumentException("id is required.");

		var payload = new Dictionary<string, object?> { ["id"] = request.Id };
		if (request.Tag is not null) payload["tag"] = request.Tag;
		if (request.Description is not null) payload["description"] = request.Description;
		if (request.Color is not null) payload["color"] = request.Color;
		if (request.Emoji is not null) payload["emoji"] = request.Emoji;

		if (payload.Count == 1)
			throw new ArgumentException("Provide at least one of tag, description, color, or emoji to update.");

		return payload;
	}
}
