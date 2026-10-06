using System.Text.Json.Serialization;

namespace Ten99.Aria.Mcp.Codecks.Models
{
	internal class CodecksFilter
	{
		// === PAGINATION & ORDERING ===
		[JsonPropertyName("$limit")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Limit { get; set; }

		[JsonPropertyName("$offset")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? Offset { get; set; }

		[JsonPropertyName("$order")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string[]? Order { get; set; }

		// === STATUS & LIFECYCLE ===
		[JsonPropertyName("$isActive")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public bool? IsActive { get; set; }

		[JsonPropertyName("isDeleted")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public bool? IsDeleted { get; set; }

		// === ENTITY-SPECIFIC FILTERS ===
		// Project filters
		[JsonPropertyName("projectId")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? ProjectId { get; set; }

		[JsonPropertyName("accountId")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? AccountId { get; set; }

		[JsonPropertyName("isPublic")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public bool? IsPublic { get; set; }

		// Card filters
		[JsonPropertyName("assigneeId")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? AssigneeId { get; set; }

		[JsonPropertyName("deckId")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? DeckId { get; set; }

		[JsonPropertyName("status")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Status { get; set; }

		[JsonPropertyName("milestoneId")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? MilestoneId { get; set; }

		[JsonPropertyName("creatorId")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? CreatorId { get; set; }

		// Date range filters
		[JsonPropertyName("createdAfter")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public DateTime? CreatedAfter { get; set; }

		[JsonPropertyName("createdBefore")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public DateTime? CreatedBefore { get; set; }

		[JsonPropertyName("updatedAfter")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public DateTime? UpdatedAfter { get; set; }

		[JsonPropertyName("updatedBefore")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public DateTime? UpdatedBefore { get; set; }

		// Priority and effort filters
		[JsonPropertyName("minEffort")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? MinEffort { get; set; }

		[JsonPropertyName("maxEffort")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int? MaxEffort { get; set; }

		[JsonPropertyName("priority")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Priority { get; set; }

		// Content search
		[JsonPropertyName("titleContains")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? TitleContains { get; set; }

		[JsonPropertyName("contentContains")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? ContentContains { get; set; }

		// Tag filters
		[JsonPropertyName("tagIds")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string[]? TagIds { get; set; }

		[JsonPropertyName("hasBlockingDeps")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public bool? HasBlockingDeps { get; set; }

		// User access filters
		[JsonPropertyName("userId")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? UserId { get; set; }

		[JsonPropertyName("projectRole")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? ProjectRole { get; set; }
	}
}
