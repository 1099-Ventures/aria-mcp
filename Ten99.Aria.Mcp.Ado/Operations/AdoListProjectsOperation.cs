using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// GET <c>_apis/projects</c> for the active org, projected to a lean <c>{id, name, state, visibility}</c>
/// per project (no _links, no descriptions unless <paramref name="includeDescription"/>).
/// </summary>
internal sealed class AdoListProjectsOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(IConfiguration config, HttpClient httpClient, bool includeDescription = false)
	{
		try
		{
			AdoOrg org = ResolveOrg(config);
			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Get, "_apis/projects", org);
			if (!result.Ok)
				return ApiError("list_projects", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			JsonElement root = doc.RootElement;

			List<object> projects = [];
			if (root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement p in value.EnumerateArray())
				{
					Dictionary<string, object?> item = new()
					{
						["id"] = Str(p, "id"),
						["name"] = Str(p, "name"),
						["state"] = Str(p, "state"),
						["visibility"] = Str(p, "visibility")
					};
					if (includeDescription)
						item["description"] = Str(p, "description");
					projects.Add(item);
				}
			}

			return Json(new { count = projects.Count, projects }, config);
		}
		catch (Exception ex)
		{
			return Failure("list_projects", ex.Message, config);
		}
	}

	private static string? Str(JsonElement obj, string name)
		=> obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
