using Microsoft.Extensions.Configuration;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado;

/// <summary>
/// Builds the effective <see cref="AdoConfiguration"/> for a call: per-instance settings
/// (DefaultOrg, RegistryPath, IndentResponses, Pats) from the normal config chain, plus the org
/// <b>list</b> from the global registry file. PATs are stitched onto <c>authType=pat</c> orgs so
/// the registry itself stays secret-free. When the registry is empty/missing, any inline
/// <c>Ado:Orgs</c> from a <c>--config</c> file is used instead (legacy/testing PAT path).
/// </summary>
internal static class AdoConfigLoader
{
	public static AdoConfiguration Load(IConfiguration config)
	{
		AdoConfiguration cfg = config.GetSection("Ado").Get<AdoConfiguration>() ?? new AdoConfiguration();

		List<AdoOrg> inline = cfg.Orgs;                          // inline Ado:Orgs from --config (testing)
		List<AdoOrg> registry = AdoOrgRegistry.Load(cfg.RegistryPath);
		cfg.Orgs = registry.Count > 0 ? registry : inline;

		foreach (AdoOrg org in cfg.Orgs)
		{
			// Registry is secret-free: pull the PAT for pat-auth orgs from Ado:Pats:{key}.
			if (org.AuthType == AdoAuthType.Pat && string.IsNullOrEmpty(org.Pat)
				&& cfg.Pats.TryGetValue(org.Key, out string? pat))
				org.Pat = pat;

			// Legacy/testing convenience: an inline --config org that carries a PAT is pat-auth.
			if (!string.IsNullOrEmpty(org.Pat))
				org.AuthType = AdoAuthType.Pat;
		}

		return cfg;
	}
}
