using Ten99.Aria.Common.Attributes;

namespace Ten99.Aria.Mcp.Email.Imap.Configuration
{
	/// <summary>
	/// IMAP/SMTP connection + operator settings for the IMAP email MCP. Lives under the
	/// <c>Imap:Auth</c> section (SMTP submission settings sit alongside under <c>Imap:Smtp</c>),
	/// supplied via env (<c>Imap__Auth__Host</c>, …), CLI switches, or user secrets — the same
	/// pattern as the O365 MCP's <c>O365:Auth</c>.
	/// </summary>
	[ConfigurationSection("Imap:Auth")]
	public class ImapEmailConfiguration
	{
		/// <summary>IMAP server host, e.g. <c>imap.fastmail.com</c>, <c>outlook.office365.com</c>,
		/// <c>imap.gmail.com</c>, or a self-hosted Dovecot address.</summary>
		public string? Host { get; set; }

		/// <summary>IMAP port. Default <c>993</c> (implicit TLS). Use <c>143</c> with <see cref="UseSsl"/>
		/// false for STARTTLS / cleartext servers.</summary>
		public int Port { get; set; } = 993;

		/// <summary>Connect with implicit TLS (SSL-on-connect, port 993). Default <c>true</c>. When
		/// false, MailKit upgrades via STARTTLS where the server advertises it.</summary>
		public bool UseSsl { get; set; } = true;

		/// <summary>Login username — usually the full email address for the account.</summary>
		public string? Username { get; set; }

		/// <summary>Login password (or app-specific password). Required for <c>basic</c> auth. Never
		/// returned by any tool.</summary>
		public string? Password { get; set; }

		/// <summary>Auth mode: <c>basic</c> (default — LOGIN/PLAIN over TLS with
		/// <see cref="Username"/>/<see cref="Password"/>) or <c>xoauth2</c> (SASL OAuth2 bearer — the
		/// modern path for Gmail / Exchange-Online-over-IMAP; a documented P1 follow-up, not yet
		/// implemented).</summary>
		public string? AuthMode { get; set; } = "basic";

		/// <summary>True when <see cref="AuthMode"/> selects the XOAUTH2 SASL flow.</summary>
		public bool IsXOAuth2 => string.Equals(AuthMode?.Trim(), "xoauth2", System.StringComparison.OrdinalIgnoreCase);

		/// <summary>The account's own address — the default for the <c>mailbox</c> tool param and the
		/// envelope From on <c>email_send</c>. Unlike Graph, IMAP has no per-request mailbox re-targeting:
		/// one credential authenticates exactly one account, so this is that account's address.</summary>
		public string? DefaultMailbox { get; set; }

		/// <summary>When true (the default), <c>BodyPreview</c> on list results is scrubbed of
		/// bearer-shaped secrets (magic/reset links, one-time codes) before it leaves the process —
		/// previews flow to a model on every page, so this is default-on transport hygiene (#508).
		/// Operator toggle, not a per-call/model-controlled flag. Set
		/// <c>Imap__Auth__RedactBodyPreview=false</c> to disable.</summary>
		public bool RedactBodyPreview { get; set; } = true;

		/// <summary>Operator switch for the <c>email_send</c> tool. Default <c>false</c> — outbound send is
		/// <b>off unless an operator opts in</b> (a safe default for an irreversible, outward action). Set
		/// <c>Imap__Auth__AllowSend=true</c> to enable sending. Once enabled the MCP stays a mechanism and
		/// does not itself decide whether to send (that policy belongs to the agent); this is the operator's
		/// on/off gate, not a per-call/model-controlled flag.</summary>
		public bool AllowSend { get; set; } = false;

		/// <summary>Max attachment size (bytes) that <c>email_read_attachment</c> will return inline as base64.
		/// Above this the caller must pass <c>saveToDirectory</c> to write the file to disk instead (no size
		/// limit). Default 30 MB. Base64 inflates ~33%, so large inline fetches are heavy on the model context;
		/// prefer <c>saveToDirectory</c> for big files. Set via <c>Imap__Auth__MaxInlineAttachmentBytes</c>.</summary>
		public long MaxInlineAttachmentBytes { get; set; } = 30L * 1024 * 1024;

		// --- SMTP submission (the send half). Runtime keys live under Imap:Smtp; these typed properties
		//     back the user-secrets convenience path. ---

		/// <summary>SMTP submission host for <c>email_send</c>, e.g. <c>smtp.fastmail.com</c>. Falls back
		/// to <see cref="Host"/> when unset. Runtime key <c>Imap:Smtp:Host</c>.</summary>
		public string? SmtpHost { get; set; }

		/// <summary>SMTP submission port. Default <c>587</c> (STARTTLS submission). Use <c>465</c> with
		/// <see cref="SmtpUseStartTls"/> false for implicit TLS. Runtime key <c>Imap:Smtp:Port</c>.</summary>
		public int SmtpPort { get; set; } = 587;

		/// <summary>Upgrade the SMTP connection via STARTTLS (port 587). Default <c>true</c>. When false,
		/// MailKit connects with implicit TLS (SSL-on-connect, port 465). Runtime key
		/// <c>Imap:Smtp:UseStartTls</c>.</summary>
		public bool SmtpUseStartTls { get; set; } = true;
	}
}
