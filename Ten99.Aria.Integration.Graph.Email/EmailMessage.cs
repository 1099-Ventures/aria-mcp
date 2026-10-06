namespace Ten99.Aria.Integration.Graph.Email;

public record EmailMessage
{
	public required string Id { get; init; }
	public required string Subject { get; init; }
	public required string FromAddress { get; init; }
	public required string FromName { get; init; }
	public required DateTimeOffset ReceivedDateTime { get; init; }
	public required string BodyPreview { get; init; }
	public string? BodyContent { get; init; }

	/// <summary>Body format when <see cref="BodyContent"/> is populated — "text" or "html". Null on lean list results.</summary>
	public string? BodyContentType { get; init; }
	public bool IsRead { get; init; }
	public bool IsFlagged { get; init; }
	public IReadOnlyList<string> Categories { get; init; } = [];
	public IReadOnlyList<string> ToRecipients { get; init; } = [];

	/// <summary>CC recipients. Always populated on a single-message read; on lists only when the caller
	/// opts in (keeps lists lean). Empty otherwise.</summary>
	public IReadOnlyList<string> CcRecipients { get; init; } = [];
	public bool HasAttachments { get; init; }
	public string? ConversationId { get; init; }

	/// <summary>RFC 5322 Message-ID — stable across mailbox copies, so the dedup key. (<see cref="Id"/> is
	/// per mailbox copy: the same message in two mailboxes has different <see cref="Id"/>s.)</summary>
	public string? InternetMessageId { get; init; }

	/// <summary>Id of the folder the message currently sits in.</summary>
	public string? ParentFolderId { get; init; }
	public string? WebLink { get; init; }

	/// <summary>Value of the caller-requested extended property (single-message read only), null when not
	/// requested or not present. The caller named the property, so only its value is returned.</summary>
	public string? PropertyValue { get; init; }
}
