using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

[Description("getting account info")]
internal class CodecksGetAccountInfoOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksGetAccountInfoOperation>(rateLimiter, ref lastReset)
{
	protected override object BuildRequest()
	{
		// Validate/whoami via the account, not loggedInUser: an org token (cdxat_) has no logged-in
		// user, and reading user.* needs the user:readSelf scope it doesn't carry. Querying account
		// fields works for both org and personal tokens. (US 645, task 708.)
		return new
		{
			query = new
			{
				_root = new object[]
				{
					new
					{
						account = new string[] { "id", "name", "isDisabled" },
					}
				}
			}
		};
	}
}