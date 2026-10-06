namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>Outcome of a send. <see cref="MessageId"/> is the RFC5322 Message-ID where the backend can
/// supply it (SMTP sets it; Graph's fire-and-forget <c>sendMail</c> does not surface one), so it is
/// best-effort — a null id does not mean the send failed.</summary>
public record SendResult
{
	public bool Success { get; init; }

	/// <summary>The sent message's Message-ID header, when the backend exposes it; otherwise null.</summary>
	public string? MessageId { get; init; }

	/// <summary>Error message when <see cref="Success"/> is false; null on success.</summary>
	public string? Error { get; init; }
}
