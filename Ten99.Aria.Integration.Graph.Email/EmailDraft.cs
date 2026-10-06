namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>
/// A message to send — provider-neutral, so both the Graph (sendMail) and IMAP (SMTP) backends compose
/// from the same shape. Mechanism only: the service sends whatever the caller hands it; policy about
/// whether/what to send belongs to the caller (agent), not this type. Attachments are deferred.
/// </summary>
public record EmailDraft
{
	/// <summary>Primary recipients (at least one required). Addresses, not display names.</summary>
	public required IReadOnlyList<string> To { get; init; }

	/// <summary>Carbon-copy recipients.</summary>
	public IReadOnlyList<string>? Cc { get; init; }

	/// <summary>Blind carbon-copy recipients.</summary>
	public IReadOnlyList<string>? Bcc { get; init; }

	/// <summary>Subject line.</summary>
	public string? Subject { get; init; }

	/// <summary>Body content, interpreted per <see cref="BodyFormat"/>.</summary>
	public required string Body { get; init; }

	/// <summary>Whether <see cref="Body"/> is plain text (default) or HTML.</summary>
	public EmailBodyFormat BodyFormat { get; init; } = EmailBodyFormat.Text;

	/// <summary>Keep a copy in Sent Items (Graph honours this directly; SMTP backends append to the Sent
	/// folder when they can). Default true.</summary>
	public bool SaveToSentItems { get; init; } = true;
}
