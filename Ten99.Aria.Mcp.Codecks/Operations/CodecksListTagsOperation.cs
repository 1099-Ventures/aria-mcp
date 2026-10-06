using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// <c>tag_list</c> — project-scoped tags. Codecks models tags as a <c>projectTag[]</c> array on the
/// project entity; the label lives in the <c>tag</c> field (there is no <c>name</c>). We project the
/// normalized <c>projectTag</c> map into a flat <c>{count, tags[]}</c> (ADO #83).
/// </summary>
internal class CodecksListTagsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksListTagsOperation, CodecksDetailRequest>(rateLimiter, ref lastReset)
{
	// projectTag fields (Codecks 500s on unknown fields): `tag` is the label, plus color/emoji/description.
	private static readonly string[] TagFields = ["tag", "color", "emoji", "description"];

	protected override object BuildRequest(CodecksDetailRequest request)
	{
		var query = new Dictionary<string, object>
		{
			[$"project({request.Id})"] = new object[]
			{
				new Dictionary<object, object> { ["tags"] = TagFields }
			}
		};
		return new { query };
	}

	protected override CallToolResult FormatResponse(string response)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			JsonElement root = doc.RootElement;
			List<object> tags = [];
			if (root.TryGetProperty("projectTag", out JsonElement tagMap) && tagMap.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty entry in tagMap.EnumerateObject())
				{
					JsonElement t = entry.Value;
					tags.Add(new
					{
						id = entry.Name,
						label = Str(t, "tag"),
						color = Str(t, "color"),
						emoji = Str(t, "emoji"),
						description = Str(t, "description"),
					});
				}
			string text = JsonSerializer.Serialize(new { count = tags.Count, tags }, JsonResponseOptions);
			return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
		}
		catch (JsonException)
		{
			return base.FormatResponse(response); // unexpected shape — fall back to the raw payload
		}
	}

	static string? Str(JsonElement o, string n) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(n, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
