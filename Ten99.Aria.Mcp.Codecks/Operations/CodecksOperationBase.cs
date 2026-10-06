using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.IO;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Ten99.Aria.Common.Secrets;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

internal abstract class CodecksOperationBase<TOperation>(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	where TOperation : CodecksOperationBase<TOperation>
{
	protected readonly SemaphoreSlim _rateLimiter = rateLimiter;
	protected DateTime _lastReset = lastReset;

	protected static readonly JsonSerializerOptions JsonRequestOptions = new()
	{
		WriteIndented = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	protected static readonly JsonSerializerOptions JsonResponseOptions = new()
	{
		WriteIndented = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	protected static readonly JsonSerializerOptions JsonResponseOptionsIndented = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	/// <summary>
	/// Selects compact (production) or indented (debugging) response serialization
	/// based on the <c>Codecks:IndentResponses</c> config flag. Defaults to compact.
	/// </summary>
	protected static JsonSerializerOptions ResponseOptions(IConfiguration config)
	{
		string? flag = config["Codecks:IndentResponses"] ?? config["indentResponses"];
		bool indent = bool.TryParse(flag, out bool parsed) && parsed;
		return indent ? JsonResponseOptionsIndented : JsonResponseOptions;
	}

	protected abstract object BuildRequest();

	protected virtual string OperationName
	{
		get
		{
			Type type = typeof(TOperation);
			DescriptionAttribute? descriptionAttr = type.GetCustomAttribute<DescriptionAttribute>();
			return descriptionAttr?.Description ??
				   type.Name.Replace("Request", string.Empty).ToLowerInvariant();
		}
	}

	protected virtual string Endpoint => "";

	public async Task<CallToolResult> Execute(IConfiguration config, HttpClient httpClient)
	{
		try
		{
			return FormatResponse(await MakeApiRequest(config, httpClient, BuildRequest(), Endpoint));
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error {OperationName}: {ex.Message}" }]
			};
		}
	}

	/// <summary>
	/// Override this method to format the response as needed.
	/// </summary>	
	protected virtual CallToolResult FormatResponse(string response)
	{
		return new CallToolResult
		{
			Content = [new TextContentBlock { Text = response }]
		};
	}

	/// <summary>
	/// #479: projects a raw Codecks normalized list payload into <c>{count, &lt;itemsName&gt;[], …relations}</c>
	/// so every list tool returns a top-level <c>count</c> (0 on empty) — letting a caller tell
	/// "success, nothing matched" from a silent failure. The primary entity map is flattened to an
	/// array; sibling relation maps (e.g. <c>user</c>, <c>project</c>) are preserved for id resolution;
	/// <c>_root</c> is dropped. Codecks error payloads (<c>{success:false,…}</c>) pass through untouched.
	/// </summary>
	protected static CallToolResult FormatListResponse(string response, string primaryKey, string itemsName)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			JsonElement root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| (root.TryGetProperty("success", out JsonElement ok) && ok.ValueKind == JsonValueKind.False))
				return new CallToolResult { Content = [new TextContentBlock { Text = response }] };

			List<JsonElement> items = [];
			if (root.TryGetProperty(primaryKey, out JsonElement primary) && primary.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty entry in primary.EnumerateObject())
					items.Add(entry.Value);

			// count + items first, then every other top-level map (relations) except _root and the
			// now-flattened primary. Dictionary keys are written verbatim (no camelCase remap).
			Dictionary<string, object> result = new() { ["count"] = items.Count, [itemsName] = items };
			foreach (JsonProperty prop in root.EnumerateObject())
				if (prop.Name != "_root" && prop.Name != primaryKey)
					result[prop.Name] = prop.Value;

			string text = JsonSerializer.Serialize(result, JsonResponseOptions);
			return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
		}
		catch (JsonException)
		{
			return new CallToolResult { Content = [new TextContentBlock { Text = response }] };
		}
	}

	protected async Task WaitForRateLimit()
	{
		// Implement rate limiting: 40 requests per 5 seconds
		DateTime now = DateTime.Now;
		if ((now - _lastReset).TotalSeconds >= 5 && _rateLimiter.CurrentCount < CodecksRateLimit.MaxRequests)
		{
			_rateLimiter.Release(CodecksRateLimit.MaxRequests - _rateLimiter.CurrentCount);
			_lastReset = now;
		}

		await _rateLimiter.WaitAsync();
	}

	protected async Task<string> MakeApiRequest(IConfiguration config, HttpClient httpClient, object data, string endpoint)
	{
		string json = JsonSerializer.Serialize(data, JsonRequestOptions);
#if DEBUG
		Console.Error.WriteLine($"Making API request to {endpoint}:{Environment.NewLine}{json}");
#endif
		JsonSerializerOptions responseOptions = ResponseOptions(config);

		// Up to two attempts: the proactive limiter (WaitForRateLimit, 40 req / 5s) should keep us under
		// the documented cap, and this is the reactive backstop that honours a 429 Retry-After. A fresh
		// HttpRequestMessage is built per attempt since a message cannot be re-sent.
		const int maxAttempts = 2;
		for (int attempt = 1; ; attempt++)
		{
			await WaitForRateLimit();

			HttpResponseMessage response;
			string responseContent;
			try
			{
				StringContent content = new(json, Encoding.UTF8, "application/json");
				HttpRequestMessage request = new(HttpMethod.Post, $"https://api.codecks.io/{endpoint}")
				{
					Content = content
				};
				ConfigureHttpHeaders(config, request);

				response = await httpClient.SendAsync(request);
				responseContent = await response.Content.ReadAsStringAsync();
			}
			finally
			{
				_rateLimiter.Release();
			}

			// 429: wait out the Retry-After (Delta or absolute Date; default 5s) and retry once.
			if ((int)response.StatusCode == 429 && attempt < maxAttempts)
			{
				TimeSpan delay = response.Headers.RetryAfter?.Delta
					?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.Now : TimeSpan.FromSeconds(5));
				if (delay < TimeSpan.Zero)
					delay = TimeSpan.FromSeconds(5);
				await Task.Delay(delay);
				continue;
			}

			if (!response.IsSuccessStatusCode)
				return FormatErrorResponse(responseContent, (int)response.StatusCode, responseOptions);

			// Try to parse and format the response; return the raw body if it isn't JSON.
			try
			{
				JsonDocument jsonDoc = JsonDocument.Parse(responseContent);
				return JsonSerializer.Serialize(jsonDoc, responseOptions);
			}
			catch
			{
				return responseContent;
			}
		}
	}

	/// <summary>
	/// New-API error bodies are structured: <c>{ error, message, path, hint }</c> for a 400, and
	/// <c>{ error: "missing_scope" | "token_expired" | "token_account_mismatch", … }</c> for 401/403.
	/// Surface those fields verbatim under a <c>success:false</c> envelope (which <see cref="FormatListResponse"/>
	/// passes through), so the caller sees the real error code, message, and query path instead of an opaque
	/// HTTP dump. Falls back to the raw body + status when the response is not the structured shape.
	/// </summary>
	protected static string FormatErrorResponse(string body, int statusCode, JsonSerializerOptions options)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.ValueKind == JsonValueKind.Object
				&& doc.RootElement.TryGetProperty("error", out JsonElement err))
			{
				Dictionary<string, object?> error = new()
				{
					["success"] = false,
					["statusCode"] = statusCode,
					["error"] = err.ValueKind == JsonValueKind.String ? err.GetString() : err.Clone(),
				};
				foreach (string field in new[] { "message", "path", "hint" })
					if (doc.RootElement.TryGetProperty(field, out JsonElement v))
						error[field] = v.ValueKind == JsonValueKind.String ? v.GetString() : v.Clone();
				return JsonSerializer.Serialize(error, options);
			}
		}
		catch (JsonException) { /* not structured JSON — fall through to the raw envelope */ }

		return JsonSerializer.Serialize(new
		{
			success = false,
			error = $"HTTP {statusCode}: {body}",
			statusCode
		}, options);
	}

	protected string GetRequiredApiToken(IConfiguration config, ISecretHelper? secrets)
	{
		var token = config["token"] ?? secrets?.GetSecret("ApiToken");
		if (string.IsNullOrEmpty(token))
			throw new InvalidOperationException("No API token provided. Use --token or configure user secrets.");
		return token;
	}

	protected static void ConfigureHttpHeaders(IConfiguration config, HttpRequestMessage request)
	{
		request.Headers.Add("User-Agent", "ARIA-Codecks-MCP/1.0");

		// Prefer the active org from the --config file; fall back to the legacy --token/--account args
		// (mutually exclusive: orgs win when configured, otherwise the single-org CLI path is used).
		CodecksConfiguration codecks = config.GetSection("Codecks").Get<CodecksConfiguration>() ?? new CodecksConfiguration();

		string? authToken = null;
		string? accountName = null;
		if (CodecksOrgState.Resolve(codecks) is { } org)
		{
			authToken = org.Token;
			accountName = org.Account;
		}

		authToken ??= config["token"] ?? config["Codecks:Token"];
		accountName ??= config["account"] ?? config["Codecks:Account"];

		// Fail fast if no token is available from either source.
		if (string.IsNullOrEmpty(authToken))
			throw new InvalidOperationException("No Codecks API token. Supply --config with a populated Codecks:Orgs, or --token.");

		// New API (manual.codecks.io/api): bearer auth replaces the deprecated X-Auth-Token header
		// (which stops working 2026-12-31). Tokens are cdxat_ (organisation) or cdxut_ (personal).
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

		// X-Account is optional now — the token already identifies its organisation, and when sent it must
		// match it (else 400 token_account_mismatch). Send it when we have a slug (keeps multi-org explicit)
		// and omit it rather than failing when only a token is configured.
		if (!string.IsNullOrEmpty(accountName))
			request.Headers.Add("X-Account", accountName);
	}

	/// <summary>
	/// Shared Codecks file-upload flow (US 715): request a signed S3 POST from <c>s3/sign</c>, upload the
	/// file to S3, and return the uploaded-file reference used by cards/addFile (fileData) and
	/// decks/update (coverFileData). The S3 POST is unauthenticated (presigned); only the sign request
	/// carries the Codecks bearer token.
	/// </summary>
	protected async Task<Dictionary<string, object?>> UploadFileAsync(
		IConfiguration config, HttpClient httpClient, string filePath, bool affectsQuota)
	{
		if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is required.");
		if (!File.Exists(filePath)) throw new FileNotFoundException($"File not found: {filePath}");

		byte[] bytes = await File.ReadAllBytesAsync(filePath);
		string fileName = Path.GetFileName(filePath);
		string contentType = GuessContentType(fileName);

		// 1. Signed S3 POST (GET, Codecks bearer auth).
		var signRequest = new HttpRequestMessage(HttpMethod.Get,
			$"https://api.codecks.io/s3/sign?objectName={Uri.EscapeDataString(fileName)}&affectsQuota={(affectsQuota ? "true" : "false")}");
		ConfigureHttpHeaders(config, signRequest);
		HttpResponseMessage signResponse = await httpClient.SendAsync(signRequest);
		string signBody = await signResponse.Content.ReadAsStringAsync();
		if (!signResponse.IsSuccessStatusCode)
			throw new InvalidOperationException($"s3/sign failed: HTTP {(int)signResponse.StatusCode}: {signBody}");

		using JsonDocument doc = JsonDocument.Parse(signBody);
		JsonElement root = doc.RootElement;
		string signedUrl = root.GetProperty("signedUrl").GetString()!;
		string publicUrl = root.GetProperty("publicUrl").GetString()!;
		long sizeLimit = root.TryGetProperty("sizeLimit", out JsonElement sl) && sl.ValueKind == JsonValueKind.Number ? sl.GetInt64() : 10_485_760;
		if (bytes.LongLength > sizeLimit)
			throw new InvalidOperationException($"File '{fileName}' is {bytes.LongLength} bytes, over the {sizeLimit}-byte limit.");

		// 2. Multipart POST to S3 (presigned fields first, the file last).
		using var form = new MultipartFormDataContent();
		foreach (JsonProperty field in root.GetProperty("fields").EnumerateObject())
			form.Add(new StringContent(field.Value.GetString() ?? string.Empty), field.Name);
		form.Add(new StringContent(contentType), "Content-Type");
		var fileContent = new ByteArrayContent(bytes);
		fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
		form.Add(fileContent, "file", fileName);

		HttpResponseMessage uploadResponse = await httpClient.PostAsync(signedUrl, form);
		if (!uploadResponse.IsSuccessStatusCode)
			throw new InvalidOperationException($"S3 upload failed: HTTP {(int)uploadResponse.StatusCode}: {await uploadResponse.Content.ReadAsStringAsync()}");

		// 3. The uploaded-file reference (shape shared by fileData / coverFileData).
		return new Dictionary<string, object?>
		{
			["fileName"] = fileName,
			["url"] = publicUrl,
			["size"] = bytes.LongLength,
			["type"] = contentType,
			["noAccount"] = false,
		};
	}

	static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
	{
		".png" => "image/png",
		".jpg" or ".jpeg" => "image/jpeg",
		".gif" => "image/gif",
		".webp" => "image/webp",
		".svg" => "image/svg+xml",
		".pdf" => "application/pdf",
		".txt" or ".md" or ".log" => "text/plain",
		".csv" => "text/csv",
		".json" => "application/json",
		".zip" => "application/zip",
		".mp4" => "video/mp4",
		_ => "application/octet-stream",
	};
}
