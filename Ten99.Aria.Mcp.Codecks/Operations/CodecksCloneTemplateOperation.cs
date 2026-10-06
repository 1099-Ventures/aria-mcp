using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Clones a published workflow (Journey) template into a deck, creating the journey's template items
/// there. Reads the template's deckLabelMap first, then maps every template deck onto the single
/// targetDeckId for the clone dispatch.
/// </summary>
internal sealed class CodecksCloneTemplateOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksCloneTemplateOperation>(rateLimiter, ref lastReset)
{
	protected override object BuildRequest() => throw new NotSupportedException();

	public async Task<CallToolResult> ExecuteAsync(IConfiguration config, HttpClient httpClient, string templateId, string targetDeckId, string? mode)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(templateId)) return Error("templateId is required.");
			if (string.IsNullOrWhiteSpace(targetDeckId)) return Error("targetDeckId is required.");
			mode = string.IsNullOrWhiteSpace(mode) ? "replace" : mode;

			// 1. Read the template's decks (deckLabelMap) so we can map each onto the target deck.
			string key = $"publishedWorkflowTemplates({{\"id\":\"{templateId}\"}})";
			var detailQuery = new
			{
				query = new
				{
					_root = new object[] { new Dictionary<string, object> { [key] = new object[] { "id", "deckLabelMap" } } },
				},
			};
			string detailResponse = await MakeApiRequest(config, httpClient, detailQuery, "");
			if (!TryGetTemplateDeckIds(detailResponse, templateId, out List<string> templateDeckIds, out string? err))
				return Error(err!);

			Dictionary<string, object?> targetDeckMap = [];
			foreach (string deckId in templateDeckIds)
				targetDeckMap[deckId] = targetDeckId;

			// 2. Clone.
			var payload = new Dictionary<string, object?>
			{
				["templateId"] = templateId,
				["targetDeckId"] = targetDeckId,
				["mode"] = mode,
				["targetDeckMap"] = targetDeckMap,
			};
			string cloneResponse = await MakeApiRequest(config, httpClient, payload, "dispatch/workflowTemplates/clone");
			if (!IsDispatchSuccess(cloneResponse))
				return Raw(cloneResponse);

			return Raw(JsonSerializer.Serialize(
				new { success = true, templateId, targetDeckId, mode, mappedTemplateDecks = templateDeckIds.Count },
				ResponseOptions(config)));
		}
		catch (Exception ex)
		{
			return Error(ex.Message);
		}
	}

	static bool TryGetTemplateDeckIds(string response, string templateId, out List<string> deckIds, out string? error)
	{
		deckIds = [];
		error = null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			JsonElement root = doc.RootElement;
			if (root.TryGetProperty("success", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
			{ error = response; return false; }
			if (!root.TryGetProperty("workflowTemplate", out JsonElement tmpls) || !tmpls.TryGetProperty(templateId, out JsonElement t))
			{ error = $"Template {templateId} not found. {response}"; return false; }
			if (t.TryGetProperty("deckLabelMap", out JsonElement dlm) && dlm.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty p in dlm.EnumerateObject())
					deckIds.Add(p.Name);
			if (deckIds.Count == 0)
			{ error = $"Template {templateId} has no decks in deckLabelMap; cannot build the clone mapping."; return false; }
			return true;
		}
		catch (Exception ex)
		{
			error = $"Failed to parse template detail: {ex.Message}";
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
