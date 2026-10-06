using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// "Who / where am I now" — GET <c>_apis/connectionData</c> for the active org and project the
/// authenticated user to a lean <c>{org, orgName, userId, displayName}</c> (no avatars/_links).
/// Doubles as a PAT validity check.
/// </summary>
internal sealed class AdoGetCurrentIdentityOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(IConfiguration config, HttpClient httpClient)
	{
		try
		{
			AdoConfiguration cfg = LoadConfig(config);
			AdoOrg org = ResolveOrg(config);

			// _apis/connectionData is preview-only — it rejects the GA 7.1 api-version.
			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Get, "_apis/connectionData", org, apiVersion: "7.1-preview");
			if (!result.Ok)
				return ApiError("get_current_identity", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			JsonElement root = doc.RootElement;

			string? userId = null, displayName = null;
			if (root.TryGetProperty("authenticatedUser", out JsonElement user) && user.ValueKind == JsonValueKind.Object)
			{
				if (user.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
					userId = id.GetString();
				if (user.TryGetProperty("providerDisplayName", out JsonElement dn) && dn.ValueKind == JsonValueKind.String)
					displayName = dn.GetString();
			}

			return Json(new
			{
				org = AdoOrgState.CurrentKey(cfg),
				orgName = org.OrgName,
				userId,
				displayName
			}, config);
		}
		catch (Exception ex)
		{
			return Failure("get_current_identity", ex.Message, config);
		}
	}
}
