using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>
/// Attachment domain (<c>attachment_*</c>). Files are uploaded via the shared S3 flow, then attached to a
/// card. Download by fetching an attachment's public <c>url</c> from attachment_list.
/// </summary>
[McpServerToolType]
public static class CodecksAttachmentTool
{
	[McpServerTool(Name = "attachment_add")]
	[Description("Attach a local file to a card. REQUIRED: cardId, filePath (path to a local file on the machine running the MCP). Uploads the file (max 10 MB) and attaches it. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> AddAttachment(IConfiguration config, HttpClient httpClient, string cardId, string filePath)
		=> new CodecksAddFileOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, cardId, filePath);

	[McpServerTool(Name = "attachment_list")]
	[Description("List a card's attachments. REQUIRED: cardId. Returns {count, attachments:[{id, title, createdAt, file:{id, name, url, size}}]}. The file url is the public download link; the attachment id is used by attachment_delete.")]
	public static Task<CallToolResult> ListAttachments(IConfiguration config, HttpClient httpClient, string cardId)
		=> new CodecksListAttachmentsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, cardId);

	[McpServerTool(Name = "attachment_delete")]
	[Description("Delete a card attachment. REQUIRED: cardId, attachmentId (the attachment id from attachment_list). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> DeleteAttachment(IConfiguration config, HttpClient httpClient, string cardId, string attachmentId)
		=> new CodecksDeleteAttachmentOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new CodecksDeleteAttachmentRequest { CardId = cardId, AttachmentId = attachmentId });
}
