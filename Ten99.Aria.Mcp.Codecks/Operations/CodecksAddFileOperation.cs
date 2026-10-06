using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Attaches a local file to a card: uploads it via the shared S3 helper, then
/// <c>dispatch/cards/addFile</c> with the uploaded-file reference.
/// </summary>
internal sealed class CodecksAddFileOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksAddFileOperation>(rateLimiter, ref lastReset)
{
	protected override object BuildRequest() => throw new NotSupportedException();

	public async Task<CallToolResult> ExecuteAsync(IConfiguration config, HttpClient httpClient, string cardId, string filePath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(cardId)) return Error("cardId is required.");
			Dictionary<string, object?> fileData = await UploadFileAsync(config, httpClient, filePath, affectsQuota: true);
			var payload = new Dictionary<string, object?> { ["cardId"] = cardId, ["fileData"] = fileData };
			return Raw(await MakeApiRequest(config, httpClient, payload, "dispatch/cards/addFile"));
		}
		catch (Exception ex)
		{
			return Error(ex.Message);
		}
	}

	static CallToolResult Raw(string text) => new() { Content = [new TextContentBlock { Text = text }] };
	static CallToolResult Error(string message) => Raw(JsonSerializer.Serialize(new { success = false, error = message }));
}
