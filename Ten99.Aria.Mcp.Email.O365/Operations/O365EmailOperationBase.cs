using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Common.Secrets;
using Ten99.Aria.Integration.Graph.Email;
using AriaGraphClientFactory = Ten99.Aria.Integration.Graph.GraphClientFactory;
using Ten99.Aria.Mcp.Email.O365.Configuration;

namespace Ten99.Aria.Mcp.Email.O365.Operations
{
	public abstract class O365EmailOperationBase<T> where T : O365EmailOperationBase<T>
	{
		protected readonly SemaphoreSlim _rateLimiter;
		protected readonly DateTime _lastReset;

		protected O365EmailOperationBase(SemaphoreSlim rateLimiter, ref DateTime lastReset)
		{
			_rateLimiter = rateLimiter;
			_lastReset = lastReset;
		}

		protected O365EmailConfiguration GetConfiguration(
			IConfiguration configuration,
			UserSecretHelper<O365EmailConfiguration> secretHelper,
			ISecretHelper? secrets)
		{
			if (IsValid(secretHelper?.Settings))
				return secretHelper!.Settings;

			var config = new O365EmailConfiguration
			{
				TenantId = configuration["O365:Auth:TenantId"] ?? secrets?.GetSecret("O365:Auth:TenantId"),
				ClientId = configuration["O365:Auth:ClientId"] ?? secrets?.GetSecret("O365:Auth:ClientId"),
				ClientSecret = configuration["O365:Auth:ClientSecret"] ?? secrets?.GetSecret("O365:Auth:ClientSecret"),
				ClientCertThumbprint = configuration["O365:Auth:ClientCertThumbprint"] ?? secrets?.GetSecret("O365:Auth:ClientCertThumbprint"),
				ClientCertPath = configuration["O365:Auth:ClientCertPath"] ?? secrets?.GetSecret("O365:Auth:ClientCertPath"),
				ClientCertPassword = configuration["O365:Auth:ClientCertPassword"] ?? secrets?.GetSecret("O365:Auth:ClientCertPassword"),
				DefaultMailbox = configuration["O365:Auth:DefaultMailbox"] ?? secrets?.GetSecret("O365:Auth:DefaultMailbox"),
				AuthMode = configuration["O365:Auth:AuthMode"] ?? secrets?.GetSecret("O365:Auth:AuthMode"),
				Scopes = configuration.GetSection("O365:Auth:Scopes").Get<string[]>() ?? WrapSecret(secrets?.GetSecret("O365:Auth:Scopes")),
				AdditionalScopes = configuration.GetSection("O365:Auth:AdditionalScopes").Get<string[]>() ?? WrapSecret(secrets?.GetSecret("O365:Auth:AdditionalScopes")),
			};

			IsValid(config, true);
			return config;
		}

		// A flat secret carries scopes as one (possibly delimited) string; wrap it so the config binding path
		// and the secret path both feed string[] into the shared normaliser.
		private static string[]? WrapSecret(string? value)
			=> string.IsNullOrWhiteSpace(value) ? null : [value];

		protected bool IsValid(O365EmailConfiguration? config, bool throwOnError = false)
		{
			try
			{
				if (config is null)
					throw new InvalidOperationException("O365:Auth configuration is missing.");
				// Mode-aware credential requirements — the single source of truth (managed identity needs none).
				O365GraphClientBuilder.ValidateConfig(config);
				if (string.IsNullOrEmpty(config.DefaultMailbox))
					throw new InvalidOperationException("DefaultMailbox is required.");
				return true;
			}
			catch (InvalidOperationException) when (!throwOnError)
			{
				return false;
			}
		}

		/// <summary>
		/// Creates a GraphServiceClient using the shared GraphClientFactory.
		/// Replaces the duplicate auth code that was previously inline.
		/// </summary>
		protected GraphServiceClient CreateGraphClient(O365EmailConfiguration config)
		{
			return AriaGraphClientFactory.CreateWithClientCredentials(
				config.TenantId!, config.ClientId!, config.ClientSecret!);
		}

		/// <summary>
		/// Creates an IEmailService instance backed by the shared integration layer.
		/// </summary>
		protected IEmailService CreateEmailService(O365EmailConfiguration config)
		{
			var graphClient = CreateGraphClient(config);
			return new GraphEmailService(graphClient);
		}

		/// <summary>
		/// Mode-aware service factory: interactive, service principal, or managed identity. Async because
		/// the interactive first-run may prompt. Shares the wiring with the tool path via O365GraphClientBuilder.
		/// </summary>
		protected async Task<IEmailService> CreateEmailServiceAsync(O365EmailConfiguration config, CancellationToken ct = default)
		{
			var graphClient = await O365GraphClientBuilder.BuildAsync(config, config.DefaultMailbox ?? string.Empty, ct);
			return new GraphEmailService(graphClient);
		}

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
