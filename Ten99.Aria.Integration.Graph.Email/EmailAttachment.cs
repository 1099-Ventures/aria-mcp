namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>Attachment metadata (no bytes) — provider-neutral, so both the Graph and IMAP backends list
/// attachments in the same shape. <see cref="Id"/> is the backend's handle for fetching the content
/// (Graph attachment id; IMAP MIME part specifier).</summary>
public record EmailAttachmentInfo
{
	public required string Id { get; init; }
	public required string Name { get; init; }
	public string? ContentType { get; init; }

	/// <summary>Size in bytes (best-effort — the backend's reported size).</summary>
	public long Size { get; init; }

	/// <summary>Inline (e.g. an embedded image referenced by the body) vs a real file attachment.</summary>
	public bool IsInline { get; init; }
}

/// <summary>An attachment's metadata plus its decoded bytes. Only file-type attachments carry content;
/// Graph item/reference attachments surface via the metadata list but have no bytes here.</summary>
public record EmailAttachmentContent : EmailAttachmentInfo
{
	public required byte[] Content { get; init; }
}
