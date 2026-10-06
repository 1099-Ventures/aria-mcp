using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Common.Secrets;
using Ten99.Aria.Mcp.Email.Imap.Configuration;

namespace Ten99.Aria.Mcp.Email.Imap.Operations
{
	/// <summary>
	/// Shared scaffolding for IMAP MCP operations — config assembly (typed secrets → env/CLI → generic
	/// secrets) and result envelopes. A structural mirror of <c>O365EmailOperationBase</c>, minus the
	/// Graph client wiring (IMAP builds its own short-lived clients per operation).
	/// </summary>
	public abstract class ImapEmailOperationBase<T> where T : ImapEmailOperationBase<T>
	{
		protected readonly SemaphoreSlim _rateLimiter;
		protected readonly DateTime _lastReset;

		protected ImapEmailOperationBase(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		{
			_rateLimiter = rateLimiter;
			_lastReset = lastReset;
		}

		protected ImapEmailConfiguration GetConfiguration(
			IConfiguration configuration,
			UserSecretHelper<ImapEmailConfiguration>? secretHelper,
			ISecretHelper? secrets)
		{
			if (IsValid(secretHelper?.Settings))
				return secretHelper!.Settings;

			var config = new ImapEmailConfiguration
			{
				Host = configuration["Imap:Auth:Host"] ?? secrets?.GetSecret("Imap:Auth:Host"),
				Port = configuration.GetValue("Imap:Auth:Port", 993),
				UseSsl = configuration.GetValue("Imap:Auth:UseSsl", true),
				Username = configuration["Imap:Auth:Username"] ?? secrets?.GetSecret("Imap:Auth:Username"),
				Password = configuration["Imap:Auth:Password"] ?? secrets?.GetSecret("Imap:Auth:Password"),
				AuthMode = configuration["Imap:Auth:AuthMode"] ?? secrets?.GetSecret("Imap:Auth:AuthMode") ?? "basic",
				DefaultMailbox = configuration["Imap:Auth:DefaultMailbox"] ?? secrets?.GetSecret("Imap:Auth:DefaultMailbox"),
				SmtpHost = configuration["Imap:Smtp:Host"] ?? secretHelper?.Settings?.SmtpHost,
				SmtpPort = configuration.GetValue("Imap:Smtp:Port", 587),
				SmtpUseStartTls = configuration.GetValue("Imap:Smtp:UseStartTls", true),
			};

			IsValid(config, true);
			return config;
		}

		protected bool IsValid(ImapEmailConfiguration? config, bool throwOnError = false)
		{
			try
			{
				if (config is null)
					throw new InvalidOperationException("Imap:Auth configuration is missing.");
				if (string.IsNullOrWhiteSpace(config.Host))
					throw new InvalidOperationException("Imap:Auth:Host is required.");
				if (string.IsNullOrWhiteSpace(config.Username))
					throw new InvalidOperationException("Imap:Auth:Username is required.");
				return true;
			}
			catch (InvalidOperationException) when (!throwOnError)
			{
				return false;
			}
		}

		protected ImapEmailService CreateEmailService(ImapEmailConfiguration config) => new(config);

		protected CallToolResult CreateSuccessResult(object data)
		{
			var responseText = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = responseText }]
			};
		}

		protected CallToolResult CreateErrorResult(string error, Exception? exception = null)
		{
			var errorData = new
			{
				error,
				timestamp = DateTime.UtcNow.ToString("O"),
				details = exception?.Message,
				type = exception?.GetType().Name
			};

			return new CallToolResult
			{
				IsError = true,
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(errorData, new JsonSerializerOptions { WriteIndented = true }) }]
			};
		}
	}
}
