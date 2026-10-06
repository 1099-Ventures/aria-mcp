using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Deletes an iteration classification node:
/// DELETE <c>{project}/_apis/wit/classificationnodes/iterations/{path}?$reclassifyId={id}</c>. When the
/// node still has work items scheduled against it, ADO requires <paramref name="reclassifyId"/> — the id
/// of the iteration those work items are moved to; the delete fails otherwise. A successful delete returns
/// no content, so this returns a lean ack <c>{success, deletedPath, reclassifyId?}</c>.
/// </summary>
internal sealed class AdoIterationDeleteOperation : AdoOperationBase
{
	public async Task<CallToolResult> Execute(
		IConfiguration config,
		HttpClient httpClient,
		string project,
		string path,
		int? reclassifyId = null)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(project)) return Failure("iteration_delete", "project is required.", config);
			if (string.IsNullOrWhiteSpace(path)) return Failure("iteration_delete", "path is required (the iteration's relative path, e.g. 'Release 1/Sprint A').", config);

			AdoOrg org = ResolveOrg(config);

			string url = $"{Uri.EscapeDataString(project)}/_apis/wit/classificationnodes/iterations/{EscapePath(path)}";
			IEnumerable<KeyValuePair<string, string>>? query = reclassifyId is int rid
				? [new("$reclassifyId", rid.ToString())]
				: null;

			ApiResult result = await SendAsync(config, httpClient, HttpMethod.Delete, url, org, query: query);

			if (!result.Ok)
				return ApiError("iteration_delete", result, config);

			Dictionary<string, object?> ack = new() { ["success"] = true, ["deletedPath"] = path };
			if (reclassifyId is int reclassified) ack["reclassifyId"] = reclassified;
			return Json(ack, config);
		}
		catch (Exception ex)
		{
			return Failure("iteration_delete", ex.Message, config);
		}
	}

	/// <summary>Escapes each path segment while preserving the <c>/</c> separators.</summary>
	private static string EscapePath(string path)
		=> string.Join('/', path.Trim('/').Split('/').Select(Uri.EscapeDataString));
}
