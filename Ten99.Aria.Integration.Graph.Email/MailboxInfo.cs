namespace Ten99.Aria.Integration.Graph.Email;

public record MailboxInfo
{
	public required string Mailbox { get; init; }
	public required string DisplayName { get; init; }
	public int UnreadCount { get; init; }
	public int TotalCount { get; init; }
}
