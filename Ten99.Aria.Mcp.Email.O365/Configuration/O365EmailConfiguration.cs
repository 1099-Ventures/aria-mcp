using System.Linq;
using Ten99.Aria.Common.Attributes;

namespace Ten99.Aria.Mcp.Email.O365.Configuration
{
	[ConfigurationSection("O365:Auth")]
	public class O365EmailConfiguration
	{
		public string? TenantId { get; set; }
		public string? ClientId { get; set; }

		/// <summary>Client secret for the <c>servicePrincipal</c> (app-only) auth mode. Supply this OR a
		/// client certificate (<see cref="ClientCertThumbprint"/> / <see cref="ClientCertPath"/>). Not used
		/// by <c>interactive</c> or <c>managedIdentity</c>.</summary>
		public string? ClientSecret { get; set; }

		/// <summary>Service-principal client certificate by store thumbprint (searched in the CurrentUser
		/// then LocalMachine "My" store). Preferred over <see cref="ClientSecret"/> for unattended auth; a
		/// certificate wins over a secret when both are set. Alternative to <see cref="ClientCertPath"/>.</summary>
		public string? ClientCertThumbprint { get; set; }

		/// <summary>Service-principal client certificate from a file — PFX/PKCS#12, or a PEM holding the cert
		/// + private key. Use <see cref="ClientCertPassword"/> for an encrypted PFX. Alternative to
		/// <see cref="ClientCertThumbprint"/>.</summary>
		public string? ClientCertPath { get; set; }

		/// <summary>Password for an encrypted PFX given by <see cref="ClientCertPath"/> (ignored for PEM).</summary>
		public string? ClientCertPassword { get; set; }

		/// <summary>True when a client certificate source (thumbprint or file) is configured.</summary>
		public bool HasClientCertificate =>
			!string.IsNullOrWhiteSpace(ClientCertThumbprint) || !string.IsNullOrWhiteSpace(ClientCertPath);

		public string? DefaultMailbox { get; set; }

		/// <summary>Operator-declared estate: additional mailbox UPNs (comma/space/semicolon-separated) that
		/// <c>email_read_mailboxes</c> should probe for reachability, on top of <see cref="DefaultMailbox"/>.
		/// Graph has no reliable "shared mailboxes I can access" enumeration, so the discoverable set is
		/// operator-declared rather than auto-discovered. Set via <c>O365__Auth__Mailboxes</c>.</summary>
		public string? Mailboxes { get; set; }

		/// <summary>When true (the default), <c>BodyPreview</c> on list results is scrubbed of bearer-shaped
		/// secrets (magic/reset links, one-time codes) before it leaves the process — previews flow to a
		/// model on every page, so this is default-on transport hygiene (#508). Operator toggle, not a
		/// per-call/model-controlled flag. Set <c>O365__Auth__RedactBodyPreview=false</c> to disable.</summary>
		public bool RedactBodyPreview { get; set; } = true;

		/// <summary>Operator switch for the <c>email_send</c> tool. Default <c>false</c> — outbound send is
		/// <b>off unless an operator opts in</b> (a deliberately safe default for an irreversible, outward
		/// action). Set <c>O365__Auth__AllowSend=true</c> to enable sending. The MCP stays a mechanism — once
		/// enabled it does not itself decide whether to send (that policy belongs to the agent); this is the
		/// operator's on/off gate, not a per-call/model-controlled flag.</summary>
		public bool AllowSend { get; set; } = false;

		/// <summary>Max attachment size (bytes) that <c>email_read_attachment</c> will return inline as base64.
		/// Above this the caller must pass <c>saveToDirectory</c> to write the file to disk instead (which has
		/// no size limit). Default 30 MB — the common O365 tenant attachment ceiling. Base64 inflates ~33%, so
		/// large inline fetches are heavy on the model context; prefer <c>saveToDirectory</c> for big files.
		/// Set via <c>O365__Auth__MaxInlineAttachmentBytes</c>.</summary>
		public long MaxInlineAttachmentBytes { get; set; } = 30L * 1024 * 1024;

		/// <summary>Auth mode: <c>clientcredential</c> (default — app-only / SP, needs ClientSecret +
		/// admin consent) or <c>interactive</c> (desktop browser sign-in as the signed-in user, delegated
		/// Mail scopes, persistent token cache — for triaging your own mailbox from Claude Desktop/Code).</summary>
		public string? AuthMode { get; set; }

		/// <summary>True when <see cref="AuthMode"/> selects the interactive delegated flow.</summary>
		public bool IsInteractive => string.Equals(AuthMode?.Trim(), "interactive", System.StringComparison.OrdinalIgnoreCase);

		/// <summary>REPLACE the default delegated scope set entirely with exactly these. Short names
		/// (<c>Mail.ReadWrite</c>) or full URLs; an element may itself be space/comma-separated. Use it for a
		/// restricted tenant where an admin should approve a smaller, exact ask (e.g.
		/// <c>["Mail.ReadWrite","User.Read"]</c> for your own mailbox only). Mutually exclusive with
		/// <see cref="AdditionalScopes"/>.</summary>
		public string[]? Scopes { get; set; }

		/// <summary>ADD these to the default delegated scope set — the common case (e.g.
		/// <c>["MailboxSettings.ReadWrite"]</c> for inbox rules) without re-listing the defaults. Short names or
		/// full URLs; an element may itself be space/comma-separated. Mutually exclusive with
		/// <see cref="Scopes"/>.</summary>
		public string[]? AdditionalScopes { get; set; }

		/// <summary>Delegated Graph Mail scopes for interactive sign-in (offline_access is added
		/// automatically by Azure.Identity). Covers read + modify (mark read, move, delete) + send.</summary>
		public static readonly string[] DefaultDelegatedScopes =
		[
			"https://graph.microsoft.com/Mail.ReadWrite",
			"https://graph.microsoft.com/Mail.ReadWrite.Shared",
			"https://graph.microsoft.com/Mail.Send",
			// Signed-in user's own profile via /me (display name). No admin consent. Shared/other mailboxes
			// need User.ReadBasic.All — deferred to #513.
			"https://graph.microsoft.com/User.Read",
		];

		/// <summary>The delegated scopes to actually request. <see cref="Scopes"/> replaces the default set;
		/// <see cref="AdditionalScopes"/> extends it; the two are mutually exclusive. Names are normalised —
		/// bare names get the Graph resource prefix, delimited elements are split, duplicates dropped.</summary>
		public string[] ResolveDelegatedScopes()
		{
			var replace = Normalize(Scopes);
			var add = Normalize(AdditionalScopes);

			if (replace.Length > 0 && add.Length > 0)
				throw new System.InvalidOperationException(
					"O365:Auth:Scopes and O365:Auth:AdditionalScopes are mutually exclusive — Scopes replaces the "
					+ "default set, AdditionalScopes extends it. Set one, not both.");

			if (replace.Length > 0)
				return replace;
			if (add.Length > 0)
				return [.. DefaultDelegatedScopes.Concat(add).Distinct()];
			return DefaultDelegatedScopes;
		}

		// Flatten (an element may be space/comma-separated), prefix bare names with the Graph resource, and
		// dedupe — so ["Mail.ReadWrite","MailboxSettings.ReadWrite"] and ["Mail.ReadWrite MailboxSettings.ReadWrite"]
		// resolve identically.
		private static string[] Normalize(string[]? raw)
			=> raw is null || raw.Length == 0
				? []
				: [.. raw
					.Where(s => !string.IsNullOrWhiteSpace(s))
					.SelectMany(s => s.Split([' ', ','], System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
					.Select(s => s.Contains("://") ? s : $"https://graph.microsoft.com/{s}")
					.Distinct()];
	}
}