using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Lists a project's iteration classification nodes as a tree:
/// GET <c>{project}/_apis/wit/classificationnodes/iterations?$depth={depth}</c>.
/// Returns <c>{count, items[]}</c> where items are the top-level iterations (the project root's
/// children), each projected recursively to <c>{id, identifier, name, path, hasChildren, startDate,
/// finishDate, children[]}</c>. count is the number of top-level iterations (0 on empty).
/// </summary>
internal sealed class AdoIterationListOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(IConfiguration config, HttpClient httpClient, string project, int depth = 5)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("iteration_list", "project is required.", config);

			AdoOrg org = ResolveOrg(config);

			string path = $"{Uri.EscapeDataString(project)}/_apis/wit/classificationnodes/iterations";
			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Get, path, org,
				query: [new("$depth", depth.ToString())]);

			if (!result.Ok)
				return ApiError("iteration_list", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			JsonElement root = doc.RootElement;

			List<object> items = [];
			if (root.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
				foreach (JsonElement child in children.EnumerateArray())
					items.Add(Project(child));

			return Json(new { count = items.Count, items }, config);
		}
		catch (Exception ex)
		{
			return Failure("iteration_list", ex.Message, config);
		}
	}

	/// <summary>Recursively projects a classification node to the lean iteration shape.</summary>
	private static Dictionary<string, object?> Project(JsonElement node)
	{
		Dictionary<string, object?> item = new()
		{
			["id"] = Num(node, "id"),
			["identifier"] = Str(node, "identifier"),
			["name"] = Str(node, "name"),
			["path"] = Str(node, "path"),
			["hasChildren"] = node.TryGetProperty("hasChildren", out JsonElement hc) && hc.ValueKind is JsonValueKind.True or JsonValueKind.False
				? hc.GetBoolean() : (bool?)null
		};

		if (node.TryGetProperty("attributes", out JsonElement attrs) && attrs.ValueKind == JsonValueKind.Object)
		{
			if (Str(attrs, "startDate") is string start) item["startDate"] = start;
			if (Str(attrs, "finishDate") is string finish) item["finishDate"] = finish;
		}

		if (node.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
		{
			List<object> kids = [];
			foreach (JsonElement child in children.EnumerateArray())
				kids.Add(Project(child));
			if (kids.Count > 0)
				item["children"] = kids;
		}

		return item;
	}

	private static string? Str(JsonElement obj, string name)
		=> obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

	private static int? Num(JsonElement obj, string name)
		=> obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null;
}
