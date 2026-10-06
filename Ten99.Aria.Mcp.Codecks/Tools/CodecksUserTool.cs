using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>User domain (<c>user_*</c>) — workspace users (ADO #437).</summary>
[McpServerToolType]
public static class CodecksUserTool
{
	[McpServerTool(Name = "user_list")]
	[Description("✅ TESTED & READY - List all users in the Codecks workspace with pagination support. Returns {count, users:[{id, name, fullName}], accountRole} — top-level count is 0 on empty; the accountRole map resolves each user's role. REQUIRED: accountId (the account GUID, from org_get_account_info — not the subdomain).")]
	public static Task<CallToolResult> ListUsers(IConfiguration config, HttpClient httpClient, string accountId, int offset = 0)
		=> new CodecksListUsersOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, new CodecksDetailRequest(accountId, offset));

	[McpServerTool(Name = "user_get_detail")]
	[Description("✅ TESTED & READY - Get detailed information about a specific user in the Codecks workspace")]
	public static Task<CallToolResult> GetUserDetail(IConfiguration config, HttpClient httpClient, string userId, int offset = 0)
		=> new CodecksGetUserDetailOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset).Execute(config, httpClient, new CodecksDetailRequest(userId, offset));
}
