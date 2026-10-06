using System.ComponentModel;
using System.Text;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks.Operations;

/// <summary>
/// Creates a new card in a Codecks deck with basic metadata including content (Markdown capable),
/// effort, priority, assigneeId, and dueDate. Part of US#12 implementation.
/// Includes validation for priority (a/b/c) and effort (Fibonacci numbers).
/// </summary>
[Description("Create a new card in a Codecks deck with basic metadata (content, effort, priority, assignee, due date)")]
internal class CodecksCreateCardOperation(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBaseExtended<CodecksCreateCardOperation, CodecksCreateCardRequest>(rateLimiter, ref lastReset)
{
	private static readonly HashSet<int> FibonacciNumbers = GenerateFibonacciSet(233); // Generate up to reasonable max
	private static readonly string[] ValidPriorities = ["a", "b", "c"];

	protected override string Endpoint => "dispatch/cards/create";

	protected override object BuildRequest(CodecksCreateCardRequest request)
	{
		// Validate request and collect warnings
		var validationResult = ValidateRequest(request);

		if (validationResult.HasErrors)
		{
			throw new ArgumentException(validationResult.ErrorMessage);
		}

		// Build the dispatch payload for card creation
		var payload = new Dictionary<string, object?>
		{
			{ "deckId", request.DeckId },
			{ "content", request.Content }
		};

		// Add validation warnings to content if present
		if (validationResult.HasWarnings)
		{
			payload["_validationWarnings"] = validationResult.WarningMessage;
		}

		// Add optional fields only if they have values
		if (request.Effort.HasValue)
			payload["effort"] = request.Effort.Value;

		if (!string.IsNullOrWhiteSpace(request.Priority))
			payload["priority"] = request.Priority;

		if (!string.IsNullOrWhiteSpace(request.AssigneeId))
			payload["assigneeId"] = request.AssigneeId;

		if (!string.IsNullOrWhiteSpace(request.DueDate))
			payload["dueDate"] = request.DueDate;

		if (request.SubscribeCreator.HasValue)
			payload["subscribeCreator"] = request.SubscribeCreator.Value;

		if (request.PutInQueue.HasValue)
			payload["putInQueue"] = request.PutInQueue.Value;

		if (request.AddAsBookmark.HasValue)
			payload["addAsBookmark"] = request.AddAsBookmark.Value;

		if (request.IsDoc.HasValue)
			payload["isDoc"] = request.IsDoc.Value;

		if (request.MasterTags is { Length: > 0 })
			payload["masterTags"] = request.MasterTags;

		if (!string.IsNullOrWhiteSpace(request.MilestoneId))
			payload["milestoneId"] = request.MilestoneId;

		if (!string.IsNullOrWhiteSpace(request.SprintId))
			payload["sprintId"] = request.SprintId;

		return payload;
	}

	private static ValidationResult ValidateRequest(CodecksCreateCardRequest request)
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
