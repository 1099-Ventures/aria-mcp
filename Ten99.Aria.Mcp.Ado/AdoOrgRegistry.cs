using System.Text.Json;
using Ten99.Aria.Mcp.Ado.Models;

namespace Ten99.Aria.Mcp.Ado;

/// <summary>
/// Loads the global org registry — <c>{home}/.claude/.ado-mcp-orgs.json</c> — into a list of
/// <see cref="AdoOrg"/>. The registry lists orgs only (no secrets); its root
/// <c>defaultOrganization</c> is deliberately <b>ignored</b> — the default is per-project,
/// resolved from <c>Ado:DefaultOrg</c>. Leaving that field in the file keeps the still-running
/// Node fork working until it's retired (ADO #427). A missing or malformed file yields an empty
/// list (the caller then surfaces a clear "no active organization" message).
/// </summary>
internal static class AdoOrgRegistry
{
	public const string FileName = ".ado-mcp-orgs.json";

	/// <summary>OS-agnostic default: <c>{UserProfile}/.claude/.ado-mcp-orgs.json</c>.</summary>
	public static string DefaultPath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", FileName);

	/// <summary>Reads the registry from <paramref name="overridePath"/> or the default path.</summary>
	public static List<AdoOrg> Load(string? overridePath = null)
	{
		string path = string.IsNullOrWhiteSpace(overridePath) ? DefaultPath : overridePath;
		if (!File.Exists(path))
			return [];

		try
		{
			using FileStream stream = File.OpenRead(path);
			using JsonDocument doc = JsonDocument.Parse(stream);

			List<AdoOrg> orgs = [];
			if (doc.RootElement.ValueKind == JsonValueKind.Object &&
				doc.RootElement.TryGetProperty("organizations", out JsonElement orgsEl) &&
				orgsEl.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty entry in orgsEl.EnumerateObject())
				{
					JsonElement v = entry.Value;
					bool useMsClient = GetBool(v, "useMicrosoftClientId");
					string? overrideClientId = GetString(v, "overrideClientId");

					// overrideClientId and useMicrosoftClientId are mutually exclusive; override wins.
					if (useMsClient && !string.IsNullOrWhiteSpace(overrideClientId))
						Console.Error.WriteLine(
							$"[ado-mcp] org '{entry.Name}': overrideClientId and useMicrosoftClientId are mutually " +
							"exclusive — overrideClientId takes precedence.");

					orgs.Add(new AdoOrg
					{
						Key = entry.Name,
						OrgName = GetString(v, "orgName") ?? entry.Name,
						AuthType = AdoAuthTypeParser.Parse(GetString(v, "authType")),
						Domains = GetStringList(v, "domains"),
						UseMicrosoftClientId = useMsClient,
						OverrideClientId = overrideClientId,
					});
				}
			}
			return orgs;
		}
		catch (JsonException ex)
		{
			// Don't take the server down over a bad registry — warn on stderr and act as if empty.
			Console.Error.WriteLine($"[ado-mcp] Failed to parse org registry '{path}': {ex.Message}");
			return [];
		}
	}

	static string? GetString(JsonElement obj, string name)
		=> obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String
			? e.GetString()
			: null;

	static bool GetBool(JsonElement obj, string name)
		=> obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e)
			&& (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False)
			&& e.GetBoolean();

	static List<string> GetStringList(JsonElement obj, string name)
	{
		List<string> list = [];
		if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Array)
			foreach (JsonElement item in e.EnumerateArray())
				if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
					list.Add(s);
		return list;
	}
}
