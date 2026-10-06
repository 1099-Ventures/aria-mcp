using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations
{
	internal class CodecksGetUserDetailOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		: CodecksOperationBaseExtended<CodecksGetUserDetailOperation, CodecksDetailRequest>(rateLimiter, ref lastReset)
	{
		protected override object BuildRequest(CodecksDetailRequest request)
		{
			// Omit primaryEmail.email: it needs the userEmail:read scope, which org/producer tokens
			// don't carry, and requesting it 403s the whole query. id/name/fullName are readable by
			// any token. (US 645, task 708 — same scope-safe pattern as org_get_account_info.)
			return new
			{
				query = new Dictionary<string, object>
				{
					[request.ToEntityQuery("user")] = new object[] { "id", "name", "fullName" }
				}
			};
		}
	}
}
