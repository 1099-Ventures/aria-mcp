using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Updates an existing iteration classification node:
/// PATCH <c>{project}/_apis/wit/classificationnodes/iterations/{path}</c> with a
/// <c>{name?, attributes:{startDate, finishDate}}</c> body. Renames the node (when <paramref name="name"/>
/// is supplied) and/or re-dates it. The node is addressed by its relative <paramref name="path"/> (no
/// leading project name). Returns the same lean ack as create:
/// <c>{id, identifier, name, path, startDate, finishDate, url}</c>.
/// </summary>
internal sealed class AdoIterationUpdateOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string path,
		string? name = null,
		string? startDate = null,
		string? finishDate = null)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("iteration_update", "project is required.", config);
			if (string.IsNullOrWhiteSpace(path)) return Failure("iteration_update", "path is required (the iteration's relative path, e.g. 'Release 1/Sprint A').", config);
			if (name is null && startDate is null && finishDate is null)
				return Failure("iteration_update", "Nothing to update — supply a new name and/or startDate/finishDate.", config);

			AdoOrg org = ResolveOrg(config);

			// attributes are only sent when a date is supplied — null attributes are dropped by the serializer.
			var attributes = (startDate is null && finishDate is null)
				? null
				: new { startDate, finishDate };
			string body = JsonSerializer.Serialize(new { name, attributes }, JsonRequestOptions);

			string url = $"{Uri.EscapeDataString(project)}/_apis/wit/classificationnodes/iterations/{EscapePath(path)}";
			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Patch, url, org, body);

			if (!result.Ok)
				return ApiError("iteration_update", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			return Json(Ack(doc.RootElement), config);
		}
		catch (Exception ex)
		{
			return Failure("iteration_update", ex.Message, config);
		}
	}

	/// <summary>Escapes each path segment while preserving the <c>/</c> separators.</summary>
	private static string EscapePath(string path)
		=> string.Join('/', path.Trim('/').Split('/').Select(Uri.EscapeDataString));

	private static Dictionary<string, object?> Ack(JsonElement node)
	{
		Dictionary<string, object?> ack = new()
		{
			["id"] = node.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : (int?)null,
			["identifier"] = Str(node, "identifier"),
			["name"] = Str(node, "name"),
			["path"] = Str(node, "path"),
			["url"] = Str(node, "url")
		};
		if (node.TryGetProperty("attributes", out JsonElement attrs) && attrs.ValueKind == JsonValueKind.Object)
		{
			if (Str(attrs, "startDate") is string start) ack["startDate"] = start;
			if (Str(attrs, "finishDate") is string finish) ack["finishDate"] = finish;
		}
		return ack;
	}

	private static string? Str(JsonElement obj, string name)
		=> obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
