using System.Collections.Concurrent;

namespace Ten99.Aria.Mcp.Ado.Auth;

/// <summary>
/// Discovers the AAD tenant backing an Azure DevOps org, so a cross-tenant credential can target the
/// right authority. Trick: an unauthenticated request to <c>https://vssps.dev.azure.com/{org}</c>
/// returns the tenant id in the <c>x-vss-resourcetenant</c> response header. Cached per org for the
/// process lifetime (org→tenant bindings don't change). Returns null when discovery fails; callers
/// then fall back to the multi-tenant authority.
/// </summary>
internal static class AadTenantResolver
{
	const string Header = "x-vss-resourcetenant";
	static readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

	public static async Task<string?> ResolveAsync(HttpClient http, string orgName, CancellationToken ct = default)
	{
		if (_cache.TryGetValue(orgName, out string? cached))
			return cached;

		string? tenant = await ProbeAsync(http, orgName, ct);
		_cache[orgName] = tenant;
		return tenant;
	}

	static async Task<string?> ProbeAsync(HttpClient http, string orgName, CancellationToken ct)
	{
		try
		{
			using HttpRequestMessage req = new(HttpMethod.Head, $"https://vssps.dev.azure.com/{Uri.EscapeDataString(orgName)}");
			using HttpResponseMessage res = await http.SendAsync(req, ct);
			if (!res.Headers.TryGetValues(Header, out IEnumerable<string>? values))
				return null;

			// The header may carry a comma/space-separated list; the home tenant is the first guid.
			foreach (string raw in values)
				foreach (string part in raw.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
					if (Guid.TryParse(part, out _))
						return part;

			return null;
		}
		catch (HttpRequestException)
		{
			return null; // network hiccup — fall back to the multi-tenant authority.
		}
	}
}
