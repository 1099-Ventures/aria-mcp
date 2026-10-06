using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>Deletes a card attachment via <c>dispatch/attachments/delete</c> (by attachment id + card).</summary>
[Description("delete a card attachment")]
internal class CodecksDeleteAttachmentOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksDeleteAttachmentOperation, CodecksDeleteAttachmentRequest>(rateLimiter, ref lastReset)
{
	protected override string Endpoint => "dispatch/attachments/delete";

	protected override object BuildRequest(CodecksDeleteAttachmentRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.CardId)) throw new ArgumentException("cardId is required.");
		if (string.IsNullOrWhiteSpace(request.AttachmentId)) throw new ArgumentException("attachmentId is required.");
		return new Dictionary<string, object?> { ["cardId"] = request.CardId, ["id"] = request.AttachmentId };
	}
}
