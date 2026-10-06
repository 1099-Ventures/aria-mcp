using System.Text.Json;

namespace Ten99.Aria.Mcp.Ado.Operations;

/// <summary>Tiny builder for the JSON-Patch documents ADO's work-item write endpoints expect.</summary>
internal sealed class JsonPatch
{
	private readonly List<object> _ops = [];

	public IReadOnlyList<object> Operations => _ops;
	public bool IsEmpty => _ops.Count == 0;

	/// <summary>Adds a <c>/fields/{refName}</c> op when the value is non-empty.</summary>
	public JsonPatch AddField(string refName, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
			_ops.Add(new { op = "add", path = $"/fields/{refName}", value });
		return this;
	}

	/// <summary>
	/// Adds a multiline/rich field written as <b>Markdown</b> (ARIA always authors Markdown — never
	/// left to chance): sets the value with <c>&lt;</c>/<c>&gt;</c> encoded per ADO's Markdown handling,
	/// and forces the field's format to Markdown so ADO never stores it as HTML. No-op when empty.
	/// </summary>
	public JsonPatch AddMarkdownField(string refName, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return this;

		string encoded = value.Replace("<", "&lt;").Replace(">", "&gt;");
		_ops.Add(new { op = "add", path = $"/fields/{refName}", value = encoded });
		_ops.Add(new { op = "add", path = $"/multilineFieldsFormat/{refName}", value = "Markdown" });
		return this;
	}

	/// <summary>Adds a parent link (Hierarchy-Reverse) to the given work item.</summary>
	public JsonPatch AddParent(string orgBaseUrl, int parentId)
	{
		_ops.Add(new
		{
			op = "add",
			path = "/relations/-",
			value = new
			{
				rel = "System.LinkTypes.Hierarchy-Reverse",
				url = $"{orgBaseUrl}/_apis/wit/workItems/{parentId}"
			}
		});
		return this;
	}

	/// <summary>Asserts the work item's current rev — an optimistic-concurrency guard so an index-based
	/// relation remove cannot act on a stale relations array (the index could otherwise have shifted).</summary>
	public JsonPatch TestRev(int rev)
	{
		_ops.Add(new { op = "test", path = "/rev", value = rev });
		return this;
	}

	/// <summary>Removes the relation at the given array index (resolved from a prior relations read).</summary>
	public JsonPatch RemoveRelationAt(int index)
	{
		_ops.Add(new { op = "remove", path = $"/relations/{index}" });
		return this;
	}

	/// <summary>Merges caller-supplied extra fields (a JSON object of refName -> value).</summary>
	public JsonPatch AddExtraFields(string? fieldsJson)
	{
		if (string.IsNullOrWhiteSpace(fieldsJson))
			return this;

		using JsonDocument doc = JsonDocument.Parse(fieldsJson);
		if (doc.RootElement.ValueKind != JsonValueKind.Object)
			throw new ArgumentException("fieldsJson must be a JSON object of { \"System.Xxx\": value }.");

		foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
		{
			object? value = prop.Value.ValueKind switch
			{
				JsonValueKind.String => prop.Value.GetString(),
				JsonValueKind.Number => prop.Value.TryGetInt64(out long l) ? l : prop.Value.GetDouble(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Null => null,
				_ => prop.Value.GetRawText()
			};
			_ops.Add(new { op = "add", path = $"/fields/{prop.Name}", value });
		}
		return this;
	}

	public string Serialize() => JsonSerializer.Serialize(_ops);
}
