using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Updates a milestone via <c>dispatch/milestones/update</c>. Only the supplied fields are sent (id is
/// always required).
/// </summary>
[Description("update a milestone")]
internal class CodecksUpdateMilestoneOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksUpdateMilestoneOperation, CodecksUpdateMilestoneRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/milestones/update";

	protected override object BuildRequest(CodecksUpdateMilestoneRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Id)) throw new ArgumentException("id is required.");

		var payload = new Dictionary<string, object?> { ["id"] = request.Id };
		if (request.Name is not null) payload["name"] = request.Name;
		if (request.Date is not null) payload["date"] = request.Date;
		if (request.StartDate is not null) payload["startDate"] = request.StartDate;
		if (request.Color is not null) payload["color"] = request.Color;
		if (request.IsGlobal.HasValue) payload["isGlobal"] = request.IsGlobal.Value;
		if (request.ProjectIds is not null) payload["projectIds"] = request.ProjectIds;
		if (request.ManualOrderLabels is not null) payload["manualOrderLabels"] = request.ManualOrderLabels;

		if (payload.Count == 1)
			throw new ArgumentException("Provide at least one field to update.");

		return payload;
	}
}
