using System.Reflection;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using Ten99.Aria.Common.Secrets;
using Ten99.Aria.Mcp.Email.O365.Configuration;

namespace Ten99.Aria.Mcp.Email.O365.Operations
{
	public class O365EmailHealthOperation : O365EmailOperationBase<O365EmailHealthOperation>
	{
		public O365EmailHealthOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
			: base(rateLimiter, ref lastReset)
		{
		}

		public async Task<CallToolResult> Execute(
			IConfiguration configuration,
			HttpClient httpClient,
			UserSecretHelper<O365EmailConfiguration>? typedSecrets,
			ISecretHelper? genericSecrets = null,
			CancellationToken cancellationToken = default)
		{
			await _rateLimiter.WaitAsync(cancellationToken);

			try
			{
				var config = GetConfiguration(configuration, typedSecrets!, genericSecrets);

				// Use shared IEmailService for the health check (mode-aware: app-only or interactive delegated)
				var emailService = await CreateEmailServiceAsync(config, cancellationToken);
				var mailboxInfo = await emailService.GetMailboxInfoAsync(config.DefaultMailbox!, cancellationToken);

				var healthData = new
				{
					status = "healthy",
					version = McpVersion(),
					timestamp = DateTime.UtcNow.ToString("O"),
					tenant = new
					{
						id = config.TenantId,
						authenticated = true
					},
					application = new
					{
						id = config.ClientId,
						authType = config.IsInteractive
							? "interactive (delegated, via Integration.Graph)"
							: "client_credentials (via Integration.Graph)",
					},
					mailbox = new
					{
						email = mailboxInfo.Mailbox,
						displayName = mailboxInfo.DisplayName,
						accessible = true
					},
					// These are the INBOX folder's counts, not the whole mailbox — labelled so callers
					// don't read them as mailbox-wide (#521). Other folders hold their own unread.
					inbox = new
					{
						unreadCount = mailboxInfo.UnreadCount,
						totalCount = mailboxInfo.TotalCount,
					},
					graphApi = new
					{
						status = "connected",
						integration = "Ten99.Aria.Integration.Graph.Email"
					}
				};

				return CreateSuccessResult(healthData);
			}
			catch (Exception ex)
			{
				// Surface the version even on failure — knowing which build failed is the point.
				return CreateErrorResult($"Health check failed (v{McpVersion()}): {ex.Message}", ex);
			}
			finally
			{
				_rateLimiter.Release();
			}
		}

		// The running MCP package version. InformationalVersion carries the NuGet/package version (the
		// number that tells you whether the client picked up the latest published build); fall back to
		// the assembly version.
		private static string McpVersion()
		{
			var asm = typeof(O365EmailHealthOperation).Assembly;
			return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
				?? asm.GetName().Version?.ToString()
				?? "unknown";
		}
	}
}
