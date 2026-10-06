using System.Text.Json;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>Lean projections for the pipelines domain — builds, definitions, runs, logs, artifacts.</summary>
internal static class PipelineProjection
{
	public static Dictionary<string, object?> CompactBuild(JsonElement b) => new()
	{
		["id"] = Num(b, "id"),
		["buildNumber"] = Str(b, "buildNumber"),
		["status"] = Str(b, "status"),
		["result"] = Str(b, "result"),
		["definition"] = b.TryGetProperty("definition", out JsonElement d) ? Str(d, "name") : null,
		["sourceBranch"] = Str(b, "sourceBranch"),
		["reason"] = Str(b, "reason"),
		["requestedFor"] = b.TryGetProperty("requestedFor", out JsonElement rf) ? Str(rf, "displayName") : null,
		["queueTime"] = Str(b, "queueTime"),
		["startTime"] = Str(b, "startTime"),
		["finishTime"] = Str(b, "finishTime"),
		["url"] = Web(b) ?? Str(b, "url"),
	};

	public static Dictionary<string, object?> CompactDefinition(JsonElement d) => new()
	{
		["id"] = Num(d, "id"),
		["name"] = Str(d, "name"),
		["path"] = Str(d, "path"),
		["revision"] = Num(d, "revision"),
		["type"] = Str(d, "type"),
		["queueStatus"] = Str(d, "queueStatus"),
		["url"] = Web(d) ?? Str(d, "url"),
	};

	public static Dictionary<string, object?> CompactRun(JsonElement r) => new()
	{
		["id"] = Num(r, "id"),
		["name"] = Str(r, "name"),
		["state"] = Str(r, "state"),
		["result"] = Str(r, "result"),
		["createdDate"] = Str(r, "createdDate"),
		["finishedDate"] = Str(r, "finishedDate"),
		["url"] = Web(r) ?? Str(r, "url"),
	};

	public static Dictionary<string, object?> CompactLog(JsonElement l) => new()
	{
		["id"] = Num(l, "id"),
		["lineCount"] = Num(l, "lineCount"),
		["createdOn"] = Str(l, "createdOn"),
	};

	public static Dictionary<string, object?> CompactArtifact(JsonElement a) => new()
	{
		["id"] = Num(a, "id"),
		["name"] = Str(a, "name"),
		["type"] = a.TryGetProperty("resource", out JsonElement res) ? Str(res, "type") : null,
		["downloadUrl"] = a.TryGetProperty("resource", out JsonElement r2) ? Str(r2, "downloadUrl") : null,
	};

	static string? Web(JsonElement o)
		=> o.TryGetProperty("_links", out JsonElement links) && links.TryGetProperty("web", out JsonElement web)
			&& web.TryGetProperty("href", out JsonElement h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;

	static string? Str(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
	static int? Num(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int v) ? v : null;
}
