using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Turns a raw ADO work item into a lean, projected object. Context efficiency is a first-class
/// goal of this rewrite (see the C#-Rewrite discussion "Context efficiency"): the compact shape is
/// <c>{id, type, title, state, parent, url}</c> — never avatars, identity descriptors, <c>_links</c>,
/// or relations unless explicitly asked. HTML descriptions are stripped on request.
/// </summary>
internal static partial class WorkItemProjection
{
	/// <summary>The compact default field set requested from the API and echoed back.</summary>
	public static readonly string[] DefaultFields =
	[
		"System.Id", "System.WorkItemType", "System.Title", "System.State", "System.Parent"
	];

	/// <summary>Fields to request from the API: the compact set unioned with any caller allowlist.</summary>
	public static string[] FieldsToRequest(IEnumerable<string>? extra)
	{
		HashSet<string> set = new(DefaultFields, StringComparer.OrdinalIgnoreCase);
		if (extra is not null)
			foreach (string f in extra)
				if (!string.IsNullOrWhiteSpace(f))
					set.Add(f.Trim());
		return [.. set];
	}

	/// <summary>Splits a comma/space separated fields option into a clean list, or null when empty.</summary>
	public static List<string>? ParseFields(string? fields)
	{
		if (string.IsNullOrWhiteSpace(fields))
			return null;
		List<string> list = [.. fields.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
		return list.Count > 0 ? list : null;
	}

	/// <summary>
	/// Projects a single work-item element (having <c>id</c>, <c>rev</c>, <c>fields</c>, <c>url</c>)
	/// into the compact shape. Extra allowlisted fields are appended under their reference name.
	/// </summary>
	public static Dictionary<string, object?> Compact(JsonElement item, IEnumerable<string>? extraFields = null, bool plainText = false)
	{
		JsonElement fields = item.TryGetProperty("fields", out JsonElement f) ? f : default;

		Dictionary<string, object?> result = new()
		{
			["id"] = GetInt(item, "id") ?? FieldInt(fields, "System.Id"),
			["type"] = FieldString(fields, "System.WorkItemType", plainText),
			["title"] = FieldString(fields, "System.Title", plainText),
			["state"] = FieldString(fields, "System.State", plainText),
			["parent"] = FieldInt(fields, "System.Parent"),
			["url"] = item.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null
		};

		if (extraFields is not null)
		{
			foreach (string field in extraFields)
			{
				if (Array.Exists(DefaultFields, d => string.Equals(d, field, StringComparison.OrdinalIgnoreCase)))
					continue; // already projected
				result[field] = ExtractFieldValue(fields, field, plainText);
			}
		}

		return result;
	}

	/// <summary>Minimal write/link ack: <c>{id, rev, state}</c> (+url). Full body only on explicit expand.</summary>
	public static Dictionary<string, object?> Ack(JsonElement item)
	{
		JsonElement fields = item.TryGetProperty("fields", out JsonElement f) ? f : default;
		return new Dictionary<string, object?>
		{
			["id"] = GetInt(item, "id"),
			["rev"] = GetInt(item, "rev"),
			["state"] = FieldString(fields, "System.State", false),
			["url"] = item.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null
		};
	}

	private static int? GetInt(JsonElement obj, string name)
		=> obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number
			? e.GetInt32() : null;

	private static JsonElement? GetField(JsonElement fields, string name)
		=> fields.ValueKind == JsonValueKind.Object && fields.TryGetProperty(name, out JsonElement e) ? e : null;

	private static string? FieldString(JsonElement fields, string name, bool plainText)
	{
		if (GetField(fields, name) is not JsonElement e)
			return null;
		string? s = e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString();
		return plainText ? StripHtml(s) : s;
	}

	private static int? FieldInt(JsonElement fields, string name)
		=> GetField(fields, name) is JsonElement e && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null;

	/// <summary>Extracts an arbitrary field, collapsing identity objects to their display name.</summary>
	private static object? ExtractFieldValue(JsonElement fields, string name, bool plainText)
	{
		if (GetField(fields, name) is not JsonElement e)
			return null;

		switch (e.ValueKind)
		{
			case JsonValueKind.String:
				string? s = e.GetString();
				return plainText ? StripHtml(s) : s;
			case JsonValueKind.Number:
				return e.TryGetInt64(out long l) ? l : e.GetDouble();
			case JsonValueKind.True:
			case JsonValueKind.False:
				return e.GetBoolean();
			case JsonValueKind.Object:
				// Identity fields (AssignedTo, CreatedBy, ...) — keep only displayName, drop avatars/_links.
				if (e.TryGetProperty("displayName", out JsonElement dn) && dn.ValueKind == JsonValueKind.String)
					return dn.GetString();
				return e.ToString();
			default:
				return null;
		}
	}

	/// <summary>Cheap HTML-to-text: strip tags and decode a few common entities. Good enough for lean output.</summary>
	public static string? StripHtml(string? html)
	{
		if (string.IsNullOrEmpty(html))
			return html;
		string text = TagRegex().Replace(html, " ");
		text = text.Replace("&nbsp;", " ")
			.Replace("&amp;", "&")
			.Replace("&lt;", "<")
			.Replace("&gt;", ">")
			.Replace("&quot;", "\"")
			.Replace("&#39;", "'");
		return WhitespaceRegex().Replace(text, " ").Trim();
	}

	[GeneratedRegex("<[^>]+>")]
	private static partial Regex TagRegex();

	[GeneratedRegex("\\s+")]
	private static partial Regex WhitespaceRegex();
}
