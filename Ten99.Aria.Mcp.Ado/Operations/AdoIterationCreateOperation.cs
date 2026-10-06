using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Creates (or updates) an iteration classification node:
/// POST <c>{project}/_apis/wit/classificationnodes/iterations/{parentPath}</c> with a
/// <c>{name, attributes:{startDate, finishDate}}</c> body. When <paramref name="parentPath"/> is
/// supplied the new node is nested under it; otherwise it is created at the project root.
/// Returns a lean ack <c>{id, identifier, name, path, startDate, finishDate, url}</c>.
/// </summary>
internal sealed class AdoIterationCreateOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string name,
		string? parentPath = null,
		string? startDate = null,
		string? finishDate = null)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("iteration_create", "project is required.", config);
			if (string.IsNullOrWhiteSpace(name)) return Failure("iteration_create", "name is required.", config);

			AdoOrg org = ResolveOrg(config);

			// attributes are only sent when a date is supplied — null attributes are dropped by the serializer.
			var attributes = (startDate is null && finishDate is null)
				? null
				: new { startDate, finishDate };
			string body = JsonSerializer.Serialize(new { name, attributes }, JsonRequestOptions);

			// {parentPath} is the URL segment identifying the parent node (relative to the project, no
			// leading project name). Each segment is escaped so names with spaces/slashes round-trip.
			string basePath = $"{Uri.EscapeDataString(project)}/_apis/wit/classificationnodes/iterations";
			string path = string.IsNullOrWhiteSpace(parentPath)
				? basePath
				: $"{basePath}/{EscapePath(parentPath)}";

			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Post, path, org, body);

			if (!result.Ok)
				return ApiError("iteration_create", result, config);

			using JsonDocument doc = JsonDocument.Parse(result.Body);
			return Json(Ack(doc.RootElement), config);
		}
		catch (Exception ex)
		{
			return Failure("iteration_create", ex.Message, config);
		}
	}

	/// <summary>Escapes each path segment while preserving the <c>/</c> separators.</summary>
	private static string EscapePath(string parentPath)
		=> string.Join('/', parentPath.Trim('/').Split('/').Select(Uri.EscapeDataString));

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
