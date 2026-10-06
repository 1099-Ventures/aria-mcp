using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.Codecks.Models;

internal class CodecksUpdateDeckRequest
{
	/// <summary>Required: the id of the deck to update. Sent as <c>id</c> in the dispatch payload.</summary>
	[JsonPropertyName("id")]
	public string DeckId { get; set; } = string.Empty;

	/// <summary>Optional: new deck title (rename).</summary>
	[JsonPropertyName("title")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Title { get; set; }

	/// <summary>Optional: new deck description.</summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; set; }
}
