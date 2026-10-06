using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ten99.Aria.Common.Attributes;
using Ten99.Aria.Common.Secrets;

namespace Ten99.Aria.Hosting.Mcp.Core;

/// <summary>
/// Host defaults for a standalone MCP server over stdio. The MCP-specific counterpart to the
/// private <c>AddAriaDefaults()</c>, deliberately free of the ARIA foundation (no Graph, no Key
/// Vault, no Common.Hosting): just what an MCP needs — stderr-safe logging, typed config binding,
/// user-secrets / NoOp secret resolution, and an optional HttpClient. Key Vault-backed secrets are
/// an internal concern, wired via <c>Ten99.Aria.Common.Secrets.Azure</c> outside the MCP host.
/// </summary>
public static class McpHostBuilderExtensions
{
	public static IHostApplicationBuilder AddMcpDefaults(this IHostApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// MCP stdio uses stdout as the wire, so logs must be able to go to stderr. The launcher sets
		// Logging:StdErrThreshold=Trace; honour it (and any explicit override) here.
		builder.Logging.AddConsole(options =>
		{
			var thresholdConfig = builder.Configuration["Logging:StdErrThreshold"];
			if (!string.IsNullOrEmpty(thresholdConfig) && Enum.TryParse<LogLevel>(thresholdConfig, out var threshold))
				options.LogToStandardErrorThreshold = threshold;
		});

		// Typed config: bind the [ConfigurationSection] type named by the 'options' key, if any.
		var optionsTypeName = builder.Configuration["options"];
		if (!string.IsNullOrEmpty(optionsTypeName))
			ConfigureOptions(builder.Services, optionsTypeName, builder.Configuration);

		// Secrets: user-secrets (DEBUG) when a config type is named, otherwise a NoOp fallback.
		// MCPs take their API token from config (--config / --token); the secret helper is only a
		// fallback. Key Vault is intentionally not handled in the MCP host.
		var userSecretsTypeName = builder.Configuration["userSecrets"];
#if DEBUG
		if (!string.IsNullOrEmpty(userSecretsTypeName))
			ConfigureUserSecrets(builder.Services, userSecretsTypeName, builder.Configuration);
		else
			builder.Services.AddSingleton<ISecretHelper, NoOpSecretHelper>();
#else
		builder.Services.AddSingleton<ISecretHelper, NoOpSecretHelper>();
#endif

		// Optional HttpClient for MCPs that call an HTTP API (--usesHttp true).
		if (bool.TryParse(builder.Configuration["usesHttp"], out var usesHttp) && usesHttp)
			builder.Services.AddHttpClient();

		return builder;
	}

	// Binds a [ConfigurationSection]-annotated options type named as "Namespace.TypeName, Assembly".
	private static void ConfigureOptions(IServiceCollection services, string typeName, IConfiguration configuration)
	{
		var configType = Type.GetType(typeName)
			?? throw new InvalidOperationException($"Configuration type '{typeName}' not found");
		var sectionName = ConfigurationSectionAttribute.GetSectionName(typeName);

		var configureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
			.GetMethod(nameof(OptionsConfigurationServiceCollectionExtensions.Configure),
				[typeof(IServiceCollection), typeof(IConfiguration)])
			?.MakeGenericMethod(configType);
		configureMethod?.Invoke(null, [services, configuration.GetSection(sectionName)]);
	}

	// As ConfigureOptions, plus registers UserSecretHelper<T> as the ISecretHelper (DEBUG secrets).
	private static void ConfigureUserSecrets(IServiceCollection services, string typeName, IConfiguration configuration)
	{
		var configType = Type.GetType(typeName)
			?? throw new InvalidOperationException($"Configuration type '{typeName}' not found");
		var sectionName = ConfigurationSectionAttribute.GetSectionName(configType);

		var configureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
			.GetMethod(nameof(OptionsConfigurationServiceCollectionExtensions.Configure),
				[typeof(IServiceCollection), typeof(IConfiguration)])
			?.MakeGenericMethod(configType);
		configureMethod?.Invoke(null, [services, configuration.GetSection(sectionName)]);

		var userSecretHelperType = typeof(UserSecretHelper<>).MakeGenericType(configType);
		services.AddSingleton(typeof(ISecretHelper), userSecretHelperType);
		services.AddSingleton(userSecretHelperType);
	}
}
