using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Text.Json;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Lists the decks that are journeys — i.e. hold one or more workflow items — across the token's projects,
/// so an agent can find a handcrafted (or previously cloned) journey to read and apply.
/// </summary>
[Description("list journey decks (decks with workflow items)")]
internal class CodecksListJourneyDecksOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksListJourneyDecksOperation>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest() => new
	{
		query = new
		{
			_root = new object[]
			{
				new { account = new object[]
				{
					new { projects = new object[]
					{
						"id",
						new { decks = new[] { "id", "title", "count:workflowItems", "projectId", "isDeleted" } },
					}},
				}},
			},
		},
	};

	// Keep only non-deleted decks that actually have workflow items.
	protected override CallToolResult FormatResponse(string response)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("success", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
				return new() { Content = [new TextContentBlock { Text = response }] };

			List<object> journeys = [];
			if (root.TryGetProperty("deck", out JsonElement decks) && decks.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty p in decks.EnumerateObject())
				{
					JsonElement d = p.Value;
					bool deleted = d.TryGetProperty("isDeleted", out JsonElement del) && del.ValueKind == JsonValueKind.True;
					int count = d.TryGetProperty("count:workflowItems", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
					if (deleted || count == 0) continue;
					journeys.Add(new Dictionary<string, object?>
					{
						["id"] = d.TryGetProperty("id", out var id) ? id.GetString() : null,
						["title"] = d.TryGetProperty("title", out var t) ? t.GetString() : null,
						["projectId"] = d.TryGetProperty("projectId", out var pr) ? pr.GetString() : null,
						["count:workflowItems"] = count,
					});
				}

			var result = new Dictionary<string, object> { ["count"] = journeys.Count, ["journeys"] = journeys };
			return new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(result, JsonResponseOptions) }] };
		}
		catch (JsonException)
		{
			return new() { Content = [new TextContentBlock { Text = response }] };
		}
	}
}
