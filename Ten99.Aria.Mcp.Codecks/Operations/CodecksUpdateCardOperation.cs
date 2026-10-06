using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Text.Json;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Updates an existing card in Codecks with new field values.
/// Supports updating content (Markdown), effort, priority, assignee, due date, deck, milestone, sprint, visibility, and status.
/// Includes validation for priority (a/b/c) and effort (Fibonacci numbers).
/// Part of US#68 implementation.
/// </summary>
[Description("Update an existing card in Codecks with new field values")]
internal class CodecksUpdateCardOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksUpdateCardOperation, CodecksUpdateCardRequest>(rateLimiter, ref lastReset)
{
	private static readonly HashSet<int> FibonacciNumbers = GenerateFibonacciSet(233);
	private static readonly string[] ValidPriorities = ["a", "b", "c"];

	protected override string Endpoint => "dispatch/cards/update";

	protected override object BuildRequest(CodecksUpdateCardRequest request)
	{
		// Validate request and collect warnings
		var validationResult = ValidateRequest(request);

		if (validationResult.HasErrors)
		{
			throw new ArgumentException(validationResult.ErrorMessage);
		}

		// Build the dispatch payload for card update
		var payload = new Dictionary<string, object?>
		{
			{ "id", request.CardId }
		};

		// Add validation warnings if present
		if (validationResult.HasWarnings)
		{
			payload["_validationWarnings"] = validationResult.WarningMessage;
		}

		// Add optional fields only if they have values
		if (!string.IsNullOrWhiteSpace(request.Title))
			payload["title"] = request.Title;

		if (!string.IsNullOrWhiteSpace(request.Content))
			payload["content"] = request.Content;

		if (request.Effort.HasValue)
			payload["effort"] = request.Effort.Value;

		if (!string.IsNullOrWhiteSpace(request.Priority))
			payload["priority"] = request.Priority;

		if (!string.IsNullOrWhiteSpace(request.AssigneeId))
			payload["assigneeId"] = request.AssigneeId;

		if (!string.IsNullOrWhiteSpace(request.DueDate))
			payload["dueDate"] = request.DueDate;

		if (!string.IsNullOrWhiteSpace(request.DeckId))
			payload["deckId"] = request.DeckId;

		if (!string.IsNullOrWhiteSpace(request.MilestoneId))
			payload["milestoneId"] = request.MilestoneId;

		if (!string.IsNullOrWhiteSpace(request.SprintId))
			payload["sprintId"] = request.SprintId;

		if (!string.IsNullOrWhiteSpace(request.Visibility))
			payload["visibility"] = request.Visibility;

		if (!string.IsNullOrWhiteSpace(request.Status))
			payload["status"] = request.Status;

		if (request.MasterTags is not null)
			payload["masterTags"] = request.MasterTags;

		return payload;
	}

	// The MCP owns the doc-card rule: docs are status-less. Codecks rejects a status change on a doc
	// with a raw '400 Can't change status of Doc card' — translate it into a clear, structured error
	// so the caller never has to decode the Codecks quirk.
	protected override CallToolResult FormatResponse(string response)
	{
		if (response.Contains("Can't change status of Doc card", StringComparison.OrdinalIgnoreCase))
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new
				{
					success = false,
					error = "This card is a Doc — docs are reference material and cannot take a workflow status. " +
							"Drop the status change, or convert the card from a doc first."
				}) }]
			};
		return base.FormatResponse(response);
	}

	private static ValidationResult ValidateRequest(CodecksUpdateCardRequest request)
	{
		var errors = new List<string>();
		var warnings = new List<string>();

		// Validate priority
		if (!string.IsNullOrWhiteSpace(request.Priority))
		{
			if (!ValidPriorities.Contains(request.Priority.ToLowerInvariant()))
			{
				errors.Add($"Priority must be 'a' (high), 'b' (medium), or 'c' (low). Received: '{request.Priority}'");
			}
		}

		// Validate effort
		if (request.Effort.HasValue)
		{
			if (!IsFibonacci(request.Effort.Value))
			{
				errors.Add($"Effort must be a Fibonacci number (0, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233...). Received: {request.Effort.Value}");
			}
			else if (request.Effort.Value > 8)
			{
				warnings.Add($"⚠️ Effort value {request.Effort.Value} is higher than recommended maximum of 8. Consider breaking down into smaller tasks.");
			}
		}

		return new ValidationResult(errors, warnings);
	}

	private static bool IsFibonacci(int number)
	{
		return FibonacciNumbers.Contains(number);
	}

	private static HashSet<int> GenerateFibonacciSet(int max)
	{
		var set = new HashSet<int> { 0, 1 };
		int a = 0, b = 1;

		while (b <= max)
		{
			int next = a + b;
			if (next > max) break;
			set.Add(next);
			a = b;
			b = next;
		}

		return set;
	}

	private class ValidationResult
	{
		private readonly List<string> _errors;
		private readonly List<string> _warnings;

		public ValidationResult(List<string> errors, List<string> warnings)
		{
			_errors = errors;
			_warnings = warnings;
		}

		public bool HasErrors => _errors.Count > 0;
		public bool HasWarnings => _warnings.Count > 0;

		public string ErrorMessage => string.Join("; ", _errors);
		public string WarningMessage => string.Join("; ", _warnings);
	}
}
