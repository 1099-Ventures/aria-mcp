using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Space management for Codecks. Spaces are not a standalone dispatch entity — they live as an array on
/// the project, so create / rename / delete is a read-modify-write of the whole array via
/// <c>dispatch/projects/update</c> (payload: <c>{ id: projectId, spaces: [...] }</c>). Last-write-wins:
/// two concurrent space edits on the same project can clobber each other.
/// </summary>
internal sealed class CodecksSpacesOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<CodecksSpacesOperation>(rateLimiter, ref lastReset)
{
	internal enum Mode { Create, Update, Delete }

	// The complete set of Codecks space deck types (one of each confirmed in the live account). Icons are
	// a larger, open catalog, so those stay pass-through and the API validates them.
	static readonly HashSet<string> ValidDeckTypes = new(StringComparer.OrdinalIgnoreCase) { "mixed", "task", "doc", "hero" };

	protected override object BuildRequest() => throw new NotSupportedException();

	public async Task<CallToolResult> ExecuteAsync(
		IConfiguration config, HttpClient httpClient, Mode mode,
		string projectId, int? spaceId, string? name, string? icon, string? defaultDeckType, bool force = false)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(projectId))
				return Error("projectId is required.");

			if (!string.IsNullOrWhiteSpace(defaultDeckType) && !ValidDeckTypes.Contains(defaultDeckType))
				return Error($"defaultDeckType must be one of: mixed, task, doc, hero. Received '{defaultDeckType}'.");

			// 1. Read the project's current spaces.
			string readResponse = await MakeApiRequest(config, httpClient, SpacesReadQuery(), "");
			if (!TryGetSpaces(readResponse, projectId, out List<CodecksSpace> spaces, out string? readError))
				return Error(readError!);

			// 2. Apply the mutation to the array.
			CodecksSpace? affected;
			switch (mode)
			{
				case Mode.Create:
					if (string.IsNullOrWhiteSpace(name))
						return Error("name is required to create a space.");
					int nextId = spaces.Count == 0 ? 1 : spaces.Max(s => s.Id) + 1;
					affected = new CodecksSpace { Id = nextId, Name = name, Icon = icon, DefaultDeckType = defaultDeckType ?? "mixed" };
					spaces.Add(affected);
					break;

				case Mode.Update:
					if (spaceId is null) return Error("spaceId is required to update a space.");
					if (name is null && icon is null && string.IsNullOrWhiteSpace(defaultDeckType))
						return Error("Provide at least one of name, icon, or defaultDeckType to update.");
					affected = spaces.FirstOrDefault(s => s.Id == spaceId);
					if (affected is null) return Error($"No space with id {spaceId} in project {projectId}.");
					if (name is not null) affected.Name = name;
					if (icon is not null) affected.Icon = icon;
					if (!string.IsNullOrWhiteSpace(defaultDeckType)) affected.DefaultDeckType = defaultDeckType;
					break;

				case Mode.Delete:
					if (spaceId is null) return Error("spaceId is required to delete a space.");
					affected = spaces.FirstOrDefault(s => s.Id == spaceId);
					if (affected is null) return Error($"No space with id {spaceId} in project {projectId}.");
					// Guard: deleting a populated space silently reassigns its decks to another space.
					// Refuse unless the caller explicitly forces it.
					if (!force)
					{
						int deckCount = await CountDecksInSpaceAsync(config, httpClient, projectId, spaceId.Value);
						if (deckCount > 0)
							return Error($"Space {spaceId} contains {deckCount} deck(s). Deleting it does not delete the decks — "
								+ "Codecks reassigns them to another space, which is easy to miss. Move or delete the decks first, "
								+ "or pass force=true to delete anyway and accept the reassignment.");
					}
					spaces.Remove(affected);
					break;

				default:
					return Error($"Unsupported mode {mode}.");
			}

			// 3. Write the full array back.
			var writePayload = new Dictionary<string, object?> { ["id"] = projectId, ["spaces"] = spaces };
			string writeResponse = await MakeApiRequest(config, httpClient, writePayload, "dispatch/projects/update");

			if (!IsDispatchSuccess(writeResponse))
				return Raw(writeResponse); // surface the structured error verbatim

			var result = new
			{
				success = true,
				mode = mode.ToString().ToLowerInvariant(),
				projectId,
				space = mode == Mode.Delete ? null : affected,
				spaces,
			};
			return Raw(JsonSerializer.Serialize(result, ResponseOptions(config)));
		}
		catch (Exception ex)
		{
			return Error(ex.Message);
		}
	}

	/// <summary>
	/// Reorder a project's spaces. The array order is the display order. The ids in
	/// <paramref name="orderedSpaceIds"/> come first in that order; any existing spaces not listed keep
	/// their current relative order after them, so no space is ever dropped. Unknown ids are rejected.
	/// </summary>
	public async Task<CallToolResult> ReorderAsync(IConfiguration config, HttpClient httpClient, string projectId, int[] orderedSpaceIds)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(projectId)) return Error("projectId is required.");
			if (orderedSpaceIds is null || orderedSpaceIds.Length == 0) return Error("orderedSpaceIds is required.");

			string readResponse = await MakeApiRequest(config, httpClient, SpacesReadQuery(), "");
			if (!TryGetSpaces(readResponse, projectId, out List<CodecksSpace> spaces, out string? readError))
				return Error(readError!);

			Dictionary<int, CodecksSpace> byId = spaces.ToDictionary(s => s.Id);
			List<int> unknown = orderedSpaceIds.Where(id => !byId.ContainsKey(id)).Distinct().ToList();
			if (unknown.Count > 0)
				return Error($"Unknown space id(s): {string.Join(", ", unknown)}. Valid ids: {string.Join(", ", byId.Keys)}.");

			HashSet<int> listed = [.. orderedSpaceIds];
			List<CodecksSpace> ordered = [];
			foreach (int id in orderedSpaceIds)
				if (listed.Remove(id)) ordered.Add(byId[id]); // de-dupe repeated ids, keep first occurrence
			foreach (CodecksSpace s in spaces)
				if (!orderedSpaceIds.Contains(s.Id)) ordered.Add(s);

			var writePayload = new Dictionary<string, object?> { ["id"] = projectId, ["spaces"] = ordered };
			string writeResponse = await MakeApiRequest(config, httpClient, writePayload, "dispatch/projects/update");
			if (!IsDispatchSuccess(writeResponse)) return Raw(writeResponse);

			return Raw(JsonSerializer.Serialize(new { success = true, mode = "reorder", projectId, spaces = ordered }, ResponseOptions(config)));
		}
		catch (Exception ex)
		{
			return Error(ex.Message);
		}
	}

	/// <summary>Query for a project's id + inline spaces array. Spaces is a scalar field (request bare).</summary>
	static object SpacesReadQuery() => new
	{
		query = new
		{
			_root = new object[]
			{
				new { account = new object[] { new { projects = new object[] { "id", "spaces" } } } },
			},
		},
	};

	/// <summary>Counts the non-deleted decks in a given space of a project (for the delete guard).</summary>
	async Task<int> CountDecksInSpaceAsync(IConfiguration config, HttpClient httpClient, string projectId, int spaceId)
	{
		var query = new
		{
			query = new
			{
				_root = new object[]
				{
					new { account = new object[]
					{
						new { projects = new object[]
						{
							"id",
							new { decks = new[] { "id", "spaceId", "isDeleted", "projectId" } },
						}},
					}},
				},
			},
		};

		string response = await MakeApiRequest(config, httpClient, query, "");
		using JsonDocument doc = JsonDocument.Parse(response);
		if (!doc.RootElement.TryGetProperty("deck", out JsonElement deckMap) || deckMap.ValueKind != JsonValueKind.Object)
			return 0;

		int count = 0;
		foreach (JsonProperty entry in deckMap.EnumerateObject())
		{
			JsonElement d = entry.Value;
			bool deleted = d.TryGetProperty("isDeleted", out var del) && del.ValueKind == JsonValueKind.True;
			int sid = d.TryGetProperty("spaceId", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetInt32() : -1;
			string? pid = d.TryGetProperty("projectId", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
			if (!deleted && sid == spaceId && pid == projectId)
				count++;
		}
		return count;
	}

	/// <summary>Parses the flattened query response and extracts the target project's spaces.</summary>
	static bool TryGetSpaces(string response, string projectId, out List<CodecksSpace> spaces, out string? error)
	{
		spaces = [];
		error = null;
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			JsonElement root = doc.RootElement;

			if (root.TryGetProperty("success", out JsonElement ok) && ok.ValueKind == JsonValueKind.False)
			{
				error = response; // structured error from the read
				return false;
			}
			if (!root.TryGetProperty("project", out JsonElement projects) || projects.ValueKind != JsonValueKind.Object)
			{
				error = $"No project data in response (token scope?). {response}";
				return false;
			}
			if (!projects.TryGetProperty(projectId, out JsonElement project))
			{
				error = $"Project {projectId} not found — check the id and that the token's scope includes it.";
				return false;
			}

			if (project.TryGetProperty("spaces", out JsonElement spacesEl) && spacesEl.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement s in spacesEl.EnumerateArray())
					spaces.Add(new CodecksSpace
					{
						Id = s.GetProperty("id").GetInt32(),
						Name = s.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
						Icon = s.TryGetProperty("icon", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null,
						DefaultDeckType = s.TryGetProperty("defaultDeckType", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "mixed",
					});
			}
			return true;
		}
		catch (Exception ex)
		{
			error = $"Failed to parse spaces: {ex.Message}";
			return false;
		}
	}

	/// <summary>A successful dispatch returns an actionId; an error returns the success:false envelope.</summary>
	static bool IsDispatchSuccess(string response)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(response);
			return doc.RootElement.ValueKind == JsonValueKind.Object
				&& doc.RootElement.TryGetProperty("actionId", out _);
		}
		catch { return false; }
	}

	static CallToolResult Raw(string text) => new() { Content = [new TextContentBlock { Text = text }] };
	static CallToolResult Error(string message) => Raw(JsonSerializer.Serialize(new { success = false, error = message }));
}
