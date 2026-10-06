using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using System.Reflection;

namespace Ten99.Aria.Hosting.Mcp.Core;

/// <summary>
/// Reusable entry point for hosting an ARIA MCP over stdio. Two thin front-ends wrap it:
/// <list type="bullet">
///   <item>Per-tool self-contained tool packages call <see cref="RunAsync(Assembly, string[])"/>
///   with their own tool assembly (resolved at compile time) — the <c>dnx</c>/PackAsTool shape.</item>
///   <item>The generic host (<c>Ten99.Aria.Hosting.Mcp</c>) calls <see cref="RunAsync(string[])"/>,
///   which resolves the tool assembly from the <c>--mcp &lt;dll&gt;</c> argument.</item>
/// </list>
/// One launcher, two entry shapes — so packaging and the loose host stay in lock-step.
/// See Wiki/Implementation/MCP-Servers/ADO/CSharp-Rewrite-Discussion.md and ADO #426.
/// </summary>
public static class AriaMcpLauncher
{
    /// <summary>
    /// Host the MCP whose tools live in <paramref name="toolAssembly"/>. Used by per-tool packages
    /// whose <c>Main</c> is a one-liner: <c>AriaMcpLauncher.RunAsync(typeof(SomeTool).Assembly, args)</c>.
    /// </summary>
    public static Task RunAsync(Assembly toolAssembly, string[] args)
    {
        ArgumentNullException.ThrowIfNull(toolAssembly);
        return RunCoreAsync(args, _ => toolAssembly);
    }

    /// <summary>
    /// Host the MCP whose assembly path is supplied via the <c>--mcp</c> argument. Used by the
    /// generic host that can load any MCP assembly at runtime.
    /// </summary>
    public static Task RunAsync(string[] args) =>
        RunCoreAsync(args, config =>
        {
            var assemblyPath = config["mcp"];
            if (string.IsNullOrEmpty(assemblyPath))
                throw new InvalidOperationException(
                    "No MCP server specified. Use --mcp to specify the MCP server assembly path.");

            return Assembly.LoadFrom(assemblyPath);
        });

    static async Task RunCoreAsync(string[] args, Func<IConfiguration, Assembly> resolveAssembly)
    {
        // Pin the content root to this tool's own package directory, not the process working
        // directory. The default host builder registers appsettings.json with reloadOnChange: true,
        // whose Linux file watcher recurses the entire content root. Launched via `dnx` from a large
        // tree (e.g. a game repo), that watched the caller's whole subtree and exhausted the user's
        // inotify watches. AppContext.BaseDirectory is the small package folder. (Bug 644)
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

#if DEBUG
        // Development authentication secrets, read from the entry (exe) assembly's UserSecretsId.
        // Optional so a per-tool package without secrets configured still starts.
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
            builder.Configuration.AddUserSecrets(entryAssembly, optional: true);
#endif

        // Optional typed config file for the loaded MCP (generic — any MCP can ship its own config).
        // When --config is supplied, its values take effect and the MCP binds its own
        // [ConfigurationSection] type; per-setting CLI args act as the fallback when absent.
        var configPath = builder.Configuration["config"];
        if (!string.IsNullOrEmpty(configPath))
            builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);

        var toolAssembly = resolveAssembly(builder.Configuration);
        ArgumentNullException.ThrowIfNull(toolAssembly);

        // MCP servers using the stdio transport must send all logs to stderr — stdout is the wire.
        builder.Configuration["Logging:StdErrThreshold"] = "Trace";

        // MCP host defaults: stderr-safe logging, typed config, user-secrets/NoOp secrets, optional
        // HttpClient. Self-contained in Hosting.Mcp.Core — no dependency on the private ARIA foundation.
        builder.AddMcpDefaults();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(toolAssembly)
            .WithListResourcesHandler((_, _) => ValueTask.FromResult(new ListResourcesResult { Resources = [] }))
            .WithListPromptsHandler((_, _) => ValueTask.FromResult(new ListPromptsResult { Prompts = [] }));

        await builder.Build().RunAsync();
    }
}
