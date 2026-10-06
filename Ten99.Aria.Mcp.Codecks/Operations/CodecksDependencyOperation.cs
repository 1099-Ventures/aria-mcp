using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Adds or removes a card dependency by read-modify-writing the chosen relation on the card via
/// <c>dispatch/cards/update</c>: <c>inDeps</c> (the cards it depends on / is blocked by) or <c>outDeps</c>
/// (the cards it blocks). Read-modify-write so one change doesn't clobber the rest; the inverse relation
/// on the other card reflects automatically.
/// </summary>
internal sealed class CodecksDependencyOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksDependencyOperation>(rateLimiter, ref lastReset)
{
	internal enum Mode { Add, Remove }

	protected override object BuildRequest() => throw new NotSupportedException();

	/// <param name="field">The relation to edit: "inDeps" (cardId is blocked by otherCardId) or "outDeps" (cardId blocks otherCardId).</param>
	public async Task<CallToolResult> ExecuteAsync(IConfiguration config, HttpClient httpClient, Mode mode, string field, string cardId, string otherCardId)
	{
		try
		{
			if (field is not ("inDeps" or "outDeps")) return Error("field must be 'inDeps' or 'outDeps'.");
			if (string.IsNullOrWhiteSpace(cardId)) return Error("cardId is required.");
			if (string.IsNullOrWhiteSpace(otherCardId)) return Error("the other card id is required.");
			if (string.Equals(cardId, otherCardId, StringComparison.OrdinalIgnoreCase))
				return Error("a card cannot depend on itself.");

			// Read the current relation set.
			var readQuery = new
			{
				query = new Dictionary<string, object>
				{
					[$"card({cardId})"] = new object[] { new Dictionary<string, object> { [field] = new[] { "cardId" } } },
				},
			};
			string readResponse = await MakeApiRequest(config, httpClient, readQuery, "");
			if (!TryGetRelation(readResponse, cardId, field, out List<string> ids, out string? err))
				return Error(err!);

			bool changed;
			if (mode == Mode.Add)
			{
				changed = !ids.Contains(otherCardId);
				if (changed) ids.Add(otherCardId);
			}
			else
			{
				changed = ids.Remove(otherCardId);
			}

			if (!changed)
				return Raw(JsonSerializer.Serialize(new Dictionary<string, object?>
				{
					["success"] = true,
					["cardId"] = cardId,
					["field"] = field,
					[field] = ids,
					["note"] = mode == Mode.Add ? "already present" : "was not present",
				}, ResponseOptions(config)));

			var payload = new Dictionary<string, object?> { ["id"] = cardId, [field] = ids };
			string writeResponse = await MakeApiRequest(config, httpClient, payload, "dispatch/cards/update");
			if (!IsDispatchSuccess(writeResponse))
				return Raw(writeResponse);

			return Raw(JsonSerializer.Serialize(new Dictionary<string, object?> { ["success"] = true, ["cardId"] = cardId, ["field"] = field, [field] = ids }, ResponseOptions(config)));
		}
		catch (Exception ex)
		{
			return Error(ex.Message);
		}
	}

	static bool TryGetRelation(string response, string cardId, string field, out List<string> ids, out string? error)
	{
		ids = [];
		error = null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("success", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
			{ error = response; return false; }
			if (!root.TryGetProperty("card", out JsonElement cards) || cards.ValueKind != JsonValueKind.Object)
			{ error = $"No card data in response. {response}"; return false; }
			if (!cards.TryGetProperty(cardId, out JsonElement card) || card.ValueKind != JsonValueKind.Object)
			{ error = $"Card {cardId} not found — check the id and the token's project scope."; return false; }
			if (card.TryGetProperty(field, out JsonElement rel) && rel.ValueKind == JsonValueKind.Array)
				foreach (JsonElement e in rel.EnumerateArray())
					if (e.ValueKind == JsonValueKind.String) ids.Add(e.GetString()!);
			return true;
		}
		catch (Exception ex)
		{
			error = $"Failed to parse {field}: {ex.Message}";
			return false;
		}
	}

	static bool IsDispatchSuccess(string response)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("actionId", out _);
		}
		catch { return false; }
	}

	static CallToolResult Raw(string text) => new() { Content = [new TextContentBlock { Text = text }] };
	static CallToolResult Error(string message) => Raw(JsonSerializer.Serialize(new { success = false, error = message }));
}
