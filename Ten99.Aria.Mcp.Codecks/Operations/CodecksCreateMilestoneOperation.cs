using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Creates a milestone via <c>dispatch/milestones/create</c>. Milestones are account-level and linked to
/// projects; the required accountId is resolved from the token (not supplied by the caller).
/// </summary>
internal sealed class CodecksCreateMilestoneOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksCreateMilestoneOperation>(rateLimiter, ref lastReset)
{
	protected override object BuildRequest() => throw new NotSupportedException();

	public async Task<CallToolResult> ExecuteAsync(IConfiguration config, HttpClient httpClient, CodecksCreateMilestoneRequest request)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(request.Name)) return Error("name is required.");
			if (string.IsNullOrWhiteSpace(request.Date)) return Error("date is required (YYYY-MM-DD).");
			if (string.IsNullOrWhiteSpace(request.Color)) return Error("color is required (e.g. 'blue', 'pink').");
			if (!request.IsGlobal && (request.ProjectIds is null || request.ProjectIds.Length == 0))
				return Error("at least one projectId is required (or set isGlobal=true).");

			string? accountId = await GetAccountIdAsync(config, httpClient);
			if (accountId is null) return Error("Could not resolve the account id for the token.");

			var payload = new Dictionary<string, object?>
			{
				["name"] = request.Name,
				["date"] = request.Date,
				["startDate"] = request.StartDate, // null when omitted, matching the UI payload
				["color"] = request.Color,
				["projectIds"] = request.ProjectIds ?? [],
				["accountId"] = accountId,
				["isGlobal"] = request.IsGlobal,
			};
			return Raw(await MakeApiRequest(config, httpClient, payload, "dispatch/milestones/create"));
		}
		catch (Exception ex)
		{
			return Error(ex.Message);
		}
	}

	async Task<string?> GetAccountIdAsync(IConfiguration config, HttpClient httpClient)
	{
		var query = new { query = new { _root = new object[] { new { account = new[] { "id" } } } } };
		string response = await MakeApiRequest(config, httpClient, query, "");
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			if (doc.RootElement.TryGetProperty("account", out JsonElement acc) && acc.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty p in acc.EnumerateObject())
					return p.Name;
		}
		catch { /* fall through */ }
		return null;
	}

	static CallToolResult Raw(string text) => new() { Content = [new TextContentBlock { Text = text }] };
	static CallToolResult Error(string message) => Raw(JsonSerializer.Serialize(new { success = false, error = message }));
}
