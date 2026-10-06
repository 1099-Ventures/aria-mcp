namespace Ten99.Aria.Integration.Graph.Email;

public record EmailFilter
{
	public DateTimeOffset? ReceivedAfter { get; init; }
	public DateTimeOffset? ReceivedBefore { get; init; }
	public string? FromContains { get; init; }
	public string? SubjectContains { get; init; }
	public IReadOnlyList<string>? ExcludeCategories { get; init; }

	/// <summary>Exclude the Deleted Items and Junk Email folders (unread trash/spam is triage noise).</summary>
	public bool ExcludeDeletedAndJunk { get; init; }

	/// <summary>Include CC recipients in list results (off by default to keep lists lean).</summary>
	public bool IncludeCc { get; init; }

	/// <summary>Include the full message body in list results (off by default to keep lists lean).</summary>
	public bool IncludeBody { get; init; }

	/// <summary>Body render format when <see cref="IncludeBody"/> is set (Text strips HTML, Html keeps markup).</summary>
	public EmailBodyFormat BodyFormat { get; init; } = EmailBodyFormat.Text;

	/// <summary>
	/// Builds an OData $filter string for the Graph Messages API.
	/// </summary>
	internal string ToODataFilter()
	{
		var clauses = new List<string>();

		if (ReceivedAfter.HasValue)
			clauses.Add($"receivedDateTime ge {ReceivedAfter.Value:yyyy-MM-ddTHH:mm:ssZ}");

		if (ReceivedBefore.HasValue)
			clauses.Add($"receivedDateTime le {ReceivedBefore.Value:yyyy-MM-ddTHH:mm:ssZ}");

		if (!string.IsNullOrWhiteSpace(FromContains))
			clauses.Add($"contains(from/emailAddress/address,'{EscapeOData(FromContains)}')");

		if (!string.IsNullOrWhiteSpace(SubjectContains))
			clauses.Add($"contains(subject,'{EscapeOData(SubjectContains)}')");

		if (ExcludeCategories is { Count: > 0 })
		{
			foreach (var cat in ExcludeCategories)
				clauses.Add($"not(categories/any(c:c eq '{EscapeOData(cat)}'))");
		}

		return clauses.Count > 0 ? string.Join(" and ", clauses) : string.Empty;
	}

	private static string EscapeOData(string value) => value.Replace("'", "''");
}
