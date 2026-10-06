using ModelContextProtocol.Protocol;
using System.ComponentModel;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Lists a card's attachments (id + file name/url/size). The url is the public download link.</summary>
[Description("list a card's attachments")]
internal class CodecksListAttachmentsOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksListAttachmentsOperation, string>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "";

	protected override object BuildRequest(string cardId) => new
	{
		query = new Dictionary<string, object>
		{
			[$"card({cardId})"] = new object[]
			{
				new Dictionary<string, object>
				{
					["attachments"] = new object[]
					{
						"id", "title", "createdAt",
						new Dictionary<string, object> { ["file"] = new[] { "id", "name", "url", "size" } },
					},
				},
			},
		},
	};

	protected override CallToolResult FormatResponse(string response)
		=> FormatListResponse(response, "attachment", "attachments");
}
