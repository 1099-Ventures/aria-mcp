using System.Reflection;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using Ten99.Aria.Common.Secrets;
using Ten99.Aria.Mcp.Email.Imap.Configuration;

namespace Ten99.Aria.Mcp.Email.Imap.Operations
{
	public class ImapEmailHealthOperation : ImapEmailOperationBase<ImapEmailHealthOperation>
	{
		public ImapEmailHealthOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
			: base(rateLimiter, ref lastReset)
		{
		}

		public async Task<CallToolResult> Execute(
			IConfiguration configuration,
			HttpClient httpClient,
			UserSecretHelper<ImapEmailConfiguration>? typedSecrets,
			ISecretHelper? genericSecrets = null,
			CancellationToken cancellationToken = default)
		{
			await _rateLimiter.WaitAsync(cancellationToken);

			try
			{
				var config = GetConfiguration(configuration, typedSecrets!, genericSecrets);
				string mailbox = config.DefaultMailbox ?? config.Username ?? "(unknown)";

				var service = CreateEmailService(config);
				var report = await service.GetHealthAsync(mailbox, cancellationToken);

				var healthData = new
				{
					status = "healthy",
					version = McpVersion(),
					timestamp = DateTime.UtcNow.ToString("O"),
					server = new
					{
						host = report.Host,
						port = config.Port,
						ssl = config.UseSsl,
						authMode = config.IsXOAuth2 ? "xoauth2" : "basic",
					},
					mailbox = new
					{
						email = report.Mailbox.Mailbox,
						displayName = report.Mailbox.DisplayName,
						accessible = true,
					},
					// INBOX counts specifically (not mailbox-wide) — labelled so callers don't over-read them,
					// matching the O365 health payload.
					inbox = new
					{
						unreadCount = report.Mailbox.UnreadCount,
						totalCount = report.Mailbox.TotalCount,
					},
					// The negotiated IMAP extensions — the agent reads these to know which substitutes are live.
					capabilities = report.Capabilities,
					substitutes = new
					{
						move = report.NativeMove ? "native (MOVE extension)" : "emulated (COPY + expunge)",
						specialUseFolders = report.SpecialUse ? "SPECIAL-USE attributes" : "name-guessing fallback",
						serverSort = report.ServerSort ? "SORT extension" : "client-side (fetch + sort)",
						delta = report.QResyncDelta ? "QRESYNC available (P1)" : "not available (P1 UID-watermark fallback)",
						categories = report.GmailExtensions ? "X-GM-LABELS available (P1)" : "no native equivalent (P1 keywords/sidecar)",
						conversationThreading = report.GmailExtensions ? "X-GM-THRID available (P2)" : "no native equivalent (P2 THREAD/header-walk)",
					},
					integration = "Ten99.Aria.Integration.Graph.Email (provider-neutral contract), MailKit backend",
				};

				return CreateSuccessResult(healthData);
			}
			catch (Exception ex)
			{
				return CreateErrorResult($"Health check failed (v{McpVersion()}): {ex.Message}", ex);
			}
			finally
			{
				_rateLimiter.Release();
			}
		}

		// The running MCP package version. InformationalVersion carries the NuGet/package version; fall
		// back to the assembly version.
		private static string McpVersion()
		{
			var asm = typeof(ImapEmailHealthOperation).Assembly;
			return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
				?? asm.GetName().Version?.ToString()
				?? "unknown";
		}
	}
}
