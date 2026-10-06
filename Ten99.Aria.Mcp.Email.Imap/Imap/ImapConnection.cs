using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using Ten99.Aria.Mcp.Email.Imap.Configuration;

namespace Ten99.Aria.Mcp.Email.Imap.Imap
{
	/// <summary>
	/// Opens short-lived, authenticated MailKit clients. IMAP is stateful and connection-oriented
	/// (unlike Graph's stateless REST), so every operation connects, authenticates, does its work, and
	/// disconnects — the caller <c>using</c>-scopes the returned client. Keeping the client per-operation
	/// keeps the model simple and correct at the cost of a connect per call; a pooled variant keyed by
	/// mailbox+credential is a later optimisation.
	///
	/// Only <c>basic</c> auth (LOGIN/PLAIN over TLS) is implemented in P0. The XOAUTH2 SASL path is a
	/// deliberate seam (see <see cref="AuthenticateAsync"/>) — the modern bearer flow Gmail and
	/// Exchange-Online-over-IMAP require — left as a documented P1 follow-up.
	/// </summary>
	internal static class ImapConnection
	{
		public static async Task<ImapClient> OpenImapAsync(ImapEmailConfiguration config, CancellationToken ct)
		{
			string host = Require(config.Host, "Imap:Auth:Host");

			var client = new ImapClient();
			try
			{
				var options = config.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
				await client.ConnectAsync(host, config.Port, options, ct);
				await AuthenticateAsync(client, config, ct);
				return client;
			}
			catch
			{
				client.Dispose();
				throw;
			}
		}

		public static async Task<SmtpClient> OpenSmtpAsync(ImapEmailConfiguration config, CancellationToken ct)
		{
			// SMTP submission host defaults to the IMAP host when not separately configured.
			string host = Require(string.IsNullOrWhiteSpace(config.SmtpHost) ? config.Host : config.SmtpHost, "Imap:Smtp:Host");

			var client = new SmtpClient();
			try
			{
				var options = config.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
				await client.ConnectAsync(host, config.SmtpPort, options, ct);
				await AuthenticateAsync(client, config, ct);
				return client;
			}
			catch
			{
				client.Dispose();
				throw;
			}
		}

		// Shared auth for both ImapClient and SmtpClient (both are MailKit.MailService). BASIC only in P0;
		// XOAUTH2 is the seam for the P1 bearer flow (MailKit's SaslMechanismOAuth2 slots in here, with
		// token acquisition via MSAL/Azure.Identity for O365 or Google's auth lib for Gmail).
		private static async Task AuthenticateAsync(MailService client, ImapEmailConfiguration config, CancellationToken ct)
		{
			if (config.IsXOAuth2)
				throw new NotSupportedException(
					"XOAUTH2 auth is not implemented in P0 — only 'basic' (username/password over TLS) is available. "
					+ "The SASL OAuth2 bearer path (Gmail, Exchange-Online-over-IMAP) is a documented P1 follow-up; "
					+ "set Imap:Auth:AuthMode=basic for now.");

			string username = Require(config.Username, "Imap:Auth:Username");
			string password = Require(config.Password, "Imap:Auth:Password");
			await client.AuthenticateAsync(username, password, ct);
		}

		/// <summary>The IMAP extensions negotiated with the server — so <c>email_health</c> can advertise
		/// which substitutes are live (native MOVE vs COPY+expunge, QRESYNC delta, SPECIAL-USE folders,
		/// Gmail extensions, …). The agent reads these to know what the backend can actually do.</summary>
		public static IReadOnlyList<string> DescribeCapabilities(ImapClient client)
		{
			var caps = client.Capabilities;
			var list = new List<string>();
			void Add(string name, ImapCapabilities flag)
			{
				if (caps.HasFlag(flag)) list.Add(name);
			}

			Add("IMAP4rev1", ImapCapabilities.IMAP4rev1);
			Add("MOVE", ImapCapabilities.Move);
			Add("UIDPLUS", ImapCapabilities.UidPlus);
			Add("SPECIAL-USE", ImapCapabilities.SpecialUse);
			Add("CREATE-SPECIAL-USE", ImapCapabilities.CreateSpecialUse);
			Add("CONDSTORE", ImapCapabilities.CondStore);
			Add("QRESYNC", ImapCapabilities.QuickResync);
			Add("SORT", ImapCapabilities.Sort);
			Add("THREAD", ImapCapabilities.Thread);
			Add("ESEARCH", ImapCapabilities.ESearch);
			Add("IDLE", ImapCapabilities.Idle);
			Add("X-GM-EXT-1", ImapCapabilities.GMailExt1);
			return list;
		}

		private static string Require(string? value, string key)
			=> string.IsNullOrWhiteSpace(value)
				? throw new InvalidOperationException($"Missing required IMAP setting '{key}'.")
				: value.Trim();
	}
}
