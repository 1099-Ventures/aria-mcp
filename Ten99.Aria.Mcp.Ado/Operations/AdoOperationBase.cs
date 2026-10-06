using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Auth;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>
/// Shared REST plumbing for every ADO operation: resolves the active org + PAT, builds the
/// <c>https://dev.azure.com/{org}</c> base URL, attaches auth, and formats lean responses.
/// Raw <see cref="HttpClient"/> is used deliberately (matches the Codecks style and keeps full
/// control over payload shape) — the azure-devops .NET SDK is intentionally not referenced.
/// </summary>
internal abstract class AdoOperationBase
{
	protected const string ApiVersion = "7.1";
	protected const string OrgBase = "https://dev.azure.com";

	// Auth providers behind the async seam — all four modes wired via the shared EntraCredentialFactory
	// (interactive #423; service-principal + managed-identity #431). PAT is secret-based; the AAD modes
	// acquire a Bearer token for the ADO resource.
	private static readonly PatAuthProvider _patAuth = new();
	private static readonly InteractiveAuthProvider _interactiveAuth = new();
	private static readonly ServicePrincipalAuthProvider _servicePrincipalAuth = new();
	private static readonly ManagedIdentityAuthProvider _managedIdentityAuth = new();

	static Task<AuthenticationHeaderValue> AuthHeaderForAsync(AdoOrg org, AdoAuthContext ctx, CancellationToken ct) => org.AuthType switch
	{
		AdoAuthType.Pat => _patAuth.GetAuthHeaderAsync(org, ctx, ct),
		AdoAuthType.Interactive => _interactiveAuth.GetAuthHeaderAsync(org, ctx, ct),
		AdoAuthType.ServicePrincipal => _servicePrincipalAuth.GetAuthHeaderAsync(org, ctx, ct),
		AdoAuthType.ManagedIdentity => _managedIdentityAuth.GetAuthHeaderAsync(org, ctx, ct),
		_ => throw new InvalidOperationException(
			$"Auth type '{org.AuthType}' for org '{org.Key}' ({org.OrgName}) is not recognised."),
	};

	protected static readonly JsonSerializerOptions JsonRequestOptions = new()
	{
		WriteIndented = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
	};

	private static readonly JsonSerializerOptions JsonResponseCompact = new() { WriteIndented = false };
	private static readonly JsonSerializerOptions JsonResponseIndented = new() { WriteIndented = true };

	/// <summary>Compact (default) vs indented response serialization, per <c>Ado:IndentResponses</c>.</summary>
	protected static JsonSerializerOptions ResponseOptions(IConfiguration config)
	{
		AdoConfiguration cfg = LoadConfig(config);
		return cfg.IndentResponses ? JsonResponseIndented : JsonResponseCompact;
	}

	protected static AdoConfiguration LoadConfig(IConfiguration config)
		=> AdoConfigLoader.Load(config);

	/// <summary>Resolves the active org, throwing a clear error when none is selected.</summary>
	protected static AdoOrg ResolveOrg(IConfiguration config)
	{
		AdoConfiguration cfg = LoadConfig(config);
		AdoOrg? org = AdoOrgState.Resolve(cfg);
		if (org is null)
			throw new InvalidOperationException(
				cfg.Orgs.Count == 0
					? $"No organizations in the registry ({AdoOrgRegistry.DefaultPath}). Add one, or pass --config with an inline Ado:Orgs for testing."
					: "No active organization. Set a per-project default (--default-org / Ado__DefaultOrg) or call org_switch.");
		return org;
	}

	/// <summary>The authenticated user's id (via <c>_apis/connectionData</c>), or null. Needed for
	/// PR autocomplete (<c>autoCompleteSetBy</c>) and casting a vote as the current user.</summary>
	protected static async Task<string?> GetAuthenticatedUserIdAsync(IConfiguration config, HttpClient httpClient, AdoOrg org)
	{
		ApiResult r = await SendAsync(config, httpClient, HttpMethod.Get, "_apis/connectionData", org, apiVersion: "7.1-preview");
		if (!r.Ok)
			return null;
		using JsonDocument doc = JsonDocument.Parse(r.Body);
		return doc.RootElement.TryGetProperty("authenticatedUser", out JsonElement u)
			&& u.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String
			? id.GetString()
			: null;
	}

	protected sealed record ApiResult(bool Ok, HttpStatusCode Status, string Body);

	/// <summary>
	/// Sends a REST request to the active org. <paramref name="relativePath"/> is everything after
	/// <c>https://dev.azure.com/{org}/</c> and must NOT include api-version (added here).
	/// </summary>
	protected static async Task<ApiResult> SendAsync(
		IConfiguration config,
		HttpClient httpClient,
		HttpMethod method,
		string relativePath,
		AdoOrg org,
		string? jsonBody = null,
		string contentType = "application/json",
		IEnumerable<KeyValuePair<string, string>>? query = null,
		string? apiVersion = null)
	{
		string url = BuildUrl(org, relativePath, query, apiVersion);

		using HttpRequestMessage request = new(method, url);
		request.Headers.Add("User-Agent", "ARIA-ADO-MCP/1.0");
		AdoAuthContext authContext = new(httpClient, config.GetSection("Ado:Auth").Get<AdoAuthSettings>() ?? new AdoAuthSettings());
		request.Headers.Authorization = await AuthHeaderForAsync(org, authContext, CancellationToken.None);
		request.Headers.Accept.ParseAdd("application/json");

		if (jsonBody is not null)
			request.Content = new StringContent(jsonBody, Encoding.UTF8, contentType);

		using HttpResponseMessage response = await httpClient.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();
		return new ApiResult(response.IsSuccessStatusCode, response.StatusCode, body);
	}

	/// <summary>Send to an absolute URL (for cross-service endpoints like <c>almsearch.dev.azure.com</c>),
	/// reusing the org's auth. The caller supplies the full URL incl. api-version.</summary>
	protected static async Task<ApiResult> SendAbsoluteAsync(
		IConfiguration config, HttpClient httpClient, HttpMethod method, string absoluteUrl, AdoOrg org, string? jsonBody = null)
	{
		using HttpRequestMessage request = new(method, absoluteUrl);
		request.Headers.Add("User-Agent", "ARIA-ADO-MCP/1.0");
		AdoAuthContext authContext = new(httpClient, config.GetSection("Ado:Auth").Get<AdoAuthSettings>() ?? new AdoAuthSettings());
		request.Headers.Authorization = await AuthHeaderForAsync(org, authContext, CancellationToken.None);
		request.Headers.Accept.ParseAdd("application/json");
		if (jsonBody is not null)
			request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await httpClient.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();
		return new ApiResult(response.IsSuccessStatusCode, response.StatusCode, body);
	}

	/// <summary>Send with optional <c>If-Match</c> and return the response <c>ETag</c> too — needed for
	/// wiki page upsert (create has no ETag; update requires the current one).</summary>
	protected static async Task<(bool Ok, HttpStatusCode Status, string Body, string? ETag)> SendWithETagAsync(
		IConfiguration config, HttpClient httpClient, HttpMethod method, string relativePath, AdoOrg org,
		string? jsonBody = null, string? ifMatch = null, IEnumerable<KeyValuePair<string, string>>? query = null, string? apiVersion = null)
	{
		string url = BuildUrl(org, relativePath, query, apiVersion);
		using HttpRequestMessage request = new(method, url);
		request.Headers.Add("User-Agent", "ARIA-ADO-MCP/1.0");
		AdoAuthContext authContext = new(httpClient, config.GetSection("Ado:Auth").Get<AdoAuthSettings>() ?? new AdoAuthSettings());
		request.Headers.Authorization = await AuthHeaderForAsync(org, authContext, CancellationToken.None);
		request.Headers.Accept.ParseAdd("application/json");
		if (!string.IsNullOrEmpty(ifMatch))
			request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
		if (jsonBody is not null)
			request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await httpClient.SendAsync(request);
		string body = await response.Content.ReadAsStringAsync();
		return (response.IsSuccessStatusCode, response.StatusCode, body, response.Headers.ETag?.Tag);
	}

	protected static string BuildUrl(AdoOrg org, string relativePath, IEnumerable<KeyValuePair<string, string>>? query = null, string? apiVersion = null)
	{
		string path = relativePath.TrimStart('/');
		StringBuilder sb = new($"{OrgBase}/{Uri.EscapeDataString(org.OrgName)}/{path}");
		sb.Append(path.Contains('?') ? '&' : '?');
		// Most endpoints are GA on 7.1; some (e.g. _apis/connectionData) are preview-only and need a
		// preview api-version. Callers override per request; default is the GA ApiVersion.
		sb.Append("api-version=").Append(apiVersion ?? ApiVersion);
		if (query is not null)
			foreach (KeyValuePair<string, string> kv in query)
				sb.Append('&').Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
		return sb.ToString();
	}

	/// <summary>POST <c>_apis/wit/workitemsbatch</c> for up to 200 ids, requesting the given fields.</summary>
	protected static async Task<ApiResult> BatchGetAsync(
		IConfiguration config, HttpClient httpClient, AdoOrg org, IEnumerable<int> ids, IEnumerable<string> fields)
	{
		string body = JsonSerializer.Serialize(new { ids = ids.ToArray(), fields = fields.ToArray() }, JsonRequestOptions);
		return await SendAsync(config, httpClient, HttpMethod.Post, "_apis/wit/workitemsbatch", org, body);
	}

	/// <summary>Projects a <c>{value:[...]}</c> work-item collection into the compact list shape.</summary>
	protected static List<Dictionary<string, object?>> ProjectList(JsonElement root, IEnumerable<string>? extraFields, bool plainText)
	{
		List<Dictionary<string, object?>> items = [];
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.Array)
			foreach (JsonElement wi in value.EnumerateArray())
				items.Add(WorkItemProjection.Compact(wi, extraFields, plainText));
		return items;
	}

	// ---- Response helpers -------------------------------------------------

	protected static CallToolResult Json(object payload, IConfiguration config)
		=> new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(payload, ResponseOptions(config)) }] };

	protected static CallToolResult ApiError(string operation, ApiResult result, IConfiguration config)
		=> Json(new
		{
			success = false,
			operation,
			statusCode = (int)result.Status,
			error = Summarize(result.Body)
		}, config);

	protected static CallToolResult Failure(string operation, string message, IConfiguration config)
		=> Json(new { success = false, operation, error = message }, config);

	/// <summary>Pulls the ADO error "message" out of a failure body, falling back to the raw text.</summary>
	private static string Summarize(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.TryGetProperty("message", out JsonElement msg) && msg.ValueKind == JsonValueKind.String)
				return msg.GetString() ?? body;
		}
		catch (JsonException) { /* not JSON — return raw */ }
		return body.Length > 600 ? body[..600] : body;
	}
}
