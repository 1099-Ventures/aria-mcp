namespace Ten99.Aria.Mcp.Codecks.Models
{
	internal class CodecksDetailRequest(string id, int offset = 0, string? accountId = null)
	{
		public string Id { get; set; } = id;
		public string AccountId { get; set; } = accountId!;
		public int Offset { get; set; } = offset;

		/// <summary>Optional page size; falls back to the operation's default when null.</summary>
		public int? Limit { get; set; }

		/// <summary>When true, list_projects inlines trimmed deck info; otherwise only deck counts are returned.</summary>
		public bool IncludeDecks { get; set; }

		public string ToEntityQuery(string entityName) => $"{entityName}({Id})";
	}
}
