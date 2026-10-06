using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Adds a tag (label) to a Codecks project via <c>dispatch/projects/addTag</c>. Tags are project-level,
/// not a standalone entity. Producer-tier action.
/// </summary>
[Description("add a project tag")]
internal class CodecksAddTagOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksAddTagOperation, CodecksAddTagRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/projects/addTag";

	protected override object BuildRequest(CodecksAddTagRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.ProjectId)) throw new ArgumentException("projectId is required.");
		if (string.IsNullOrWhiteSpace(request.Tag)) throw new ArgumentException("tag is required.");

		return new Dictionary<string, object?> { ["projectId"] = request.ProjectId, ["tag"] = request.Tag };
	}
}
