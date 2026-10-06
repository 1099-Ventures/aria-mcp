using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Sets a deck's cover image and/or overlay colour via <c>dispatch/decks/update</c>. When an image path
/// is given it is uploaded through the shared S3 helper and set as <c>coverFileData</c>; <c>coverColor</c>
/// sets the overlay colour on its own or alongside the image. (US 74 / 75.)
/// </summary>
internal sealed class CodecksSetDeckCoverOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksSetDeckCoverOperation>(rateLimiter, ref lastReset)
{
	protected override object BuildRequest() => throw new NotSupportedException();

	public async Task<CallToolResult> ExecuteAsync(IConfiguration config, HttpClient httpClient, string deckId, string? imagePath, string? coverColor, string? stockCover)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(deckId)) return Error("deckId is required.");
			if (string.IsNullOrWhiteSpace(imagePath) && string.IsNullOrWhiteSpace(coverColor) && string.IsNullOrWhiteSpace(stockCover))
				return Error("provide imagePath, stockCover, and/or coverColor.");
			if (!string.IsNullOrWhiteSpace(imagePath) && !string.IsNullOrWhiteSpace(stockCover))
				return Error("provide either imagePath or stockCover, not both.");

			var payload = new Dictionary<string, object?> { ["id"] = deckId };
			if (!string.IsNullOrWhiteSpace(stockCover))
			{
				if (!CodecksStockCovers.ByName.TryGetValue(stockCover, out string? url))
					return Error($"Unknown stock cover '{stockCover}'. See deck_list_stock_covers for valid names.");
				payload["coverFileData"] = new Dictionary<string, object?> { ["url"] = url, ["external"] = true, ["source"] = "cdx-cover" };
			}
			else if (!string.IsNullOrWhiteSpace(imagePath))
			{
				payload["coverFileData"] = await UploadFileAsync(config, httpClient, imagePath, affectsQuota: false);
			}
			if (!string.IsNullOrWhiteSpace(coverColor))
				payload["coverColor"] = coverColor;

			return Raw(await MakeApiRequest(config, httpClient, payload, "dispatch/decks/update"));
		}
		catch (Exception ex)
		{
			return Error(ex.Message);
		}
	}

	static CallToolResult Raw(string text) => new() { Content = [new TextContentBlock { Text = text }] };
	static CallToolResult Error(string message) => Raw(JsonSerializer.Serialize(new { success = false, error = message }));
}
