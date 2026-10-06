using ModelContextProtocol.Protocol;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations
{
	internal class CodecksListUsersOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		: CodecksOperationBaseExtended<CodecksListUsersOperation, CodecksDetailRequest>(rateLimiter, ref lastReset)
	{
		static readonly CodecksFilterKey FilterRoleCounts = new("count:roles", new
		{
			role = new string[] { "owner", "admin", "staff", "guest" }
		});

		protected override object BuildRequest(CodecksDetailRequest listRequest)
		{
			CodecksFilterKey filterObject = new("roles", new CodecksFilter
			{
				Limit = 50,
				Offset = listRequest.Offset,
				Order = ["createdAt"]
			});

			return new
			{
				query = new Dictionary<string, object>
				{
					[listRequest.ToEntityQuery("account")] = new object[]
					{
						new Dictionary<object, object?>
						{
							[filterObject] = new object[]
							{
								"createdAt", "role",
								new { user = new string[] { "id", "name", "fullName"} }
							},
						},
						FilterRoleCounts,
						"count:roles",
					}
				}
			};
		}

		// #479: flatten the `user` map to {count, users[]}, keeping accountRole for role resolution.
		protected override CallToolResult FormatResponse(string response)
			=> FormatListResponse(response, "user", "users");
	}
}
