namespace Ten99.Aria.Mcp.Codecks.Models;

/// <summary>Request to delete a card attachment (by attachment id).</summary>
internal class CodecksDeleteAttachmentRequest
{
	public string CardId { get; set; } = string.Empty;
	public string AttachmentId { get; set; } = string.Empty;
}
