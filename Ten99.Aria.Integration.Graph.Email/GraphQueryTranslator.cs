namespace Ten99.Aria.Integration.Graph.Email;

/// <summary>
/// Translates a provider-neutral <see cref="EmailQuery"/> into a Graph OData $filter.
/// Predicates Graph can't express server-side (To/Body, and unsupported ops) are returned
/// separately to be applied client-side by the caller. Keeping OData confined here is
/// deliberate — the <see cref="EmailQuery"/> contract stays provider-neutral so a MAPI
/// provider can translate the same query its own way.
/// </summary>
internal static class GraphQueryTranslator
{
    public static (string Filter, IReadOnlyList<EmailPredicate> ClientSide) Translate(EmailQuery q)
    {
        var clauses = new List<string>();
        var clientSide = new List<EmailPredicate>();

        if (q.IsRead is bool read)
            clauses.Add($"isRead eq {Bool(read)}");

        if (q.IsFlagged is bool flagged)
            clauses.Add(flagged ? "flag/flagStatus eq 'flagged'" : "flag/flagStatus ne 'flagged'");

        if (q.Received?.After is { } after)
            clauses.Add($"receivedDateTime ge {after:yyyy-MM-ddTHH:mm:ssZ}");
        if (q.Received?.Before is { } before)
            clauses.Add($"receivedDateTime le {before:yyyy-MM-ddTHH:mm:ssZ}");

        if (q.HasAttachments is bool hasAtt)
            clauses.Add($"hasAttachments eq {Bool(hasAtt)}");

        if (q.IncludeCategories.Count > 0)
        {
            var ors = q.IncludeCategories.Select(c => $"categories/any(x:x eq '{Escape(c)}')");
            clauses.Add($"({string.Join(" or ", ors)})");
        }

        foreach (var cat in q.ExcludeCategories)
            clauses.Add($"not(categories/any(c:c eq '{Escape(cat)}'))");

        if (q.ConversationIds.Count > 0)
        {
            var ors = q.ConversationIds.Select(id => $"conversationId eq '{Escape(id)}'");
            clauses.Add($"({string.Join(" or ", ors)})");
        }

        foreach (var p in q.Predicates)
        {
            var clause = TryServerSide(p);
            if (clause is not null) clauses.Add(clause);
            else clientSide.Add(p);
        }

        return (string.Join(" and ", clauses), clientSide);
    }

    // Subject & From translate to OData; To/Body (and unsupported ops) fall back to client-side.
    private static string? TryServerSide(EmailPredicate p)
    {
        string? path = p.Field switch
        {
            EmailField.Subject => "subject",
            EmailField.From => "from/emailAddress/address",
            _ => null,
        };
        if (path is null) return null;

        var v = Escape(p.Value);
        return p.Op switch
        {
            EmailOperator.Equals => $"{path} eq '{v}'",
            EmailOperator.Contains => $"contains({path},'{v}')",
            EmailOperator.StartsWith => $"startsWith({path},'{v}')",
            EmailOperator.Domain when p.Field == EmailField.From => $"contains({path},'@{v}')",
            _ => null,
        };
    }

    /// <summary>Apply a predicate to an already-fetched message (for To/Body and any client-side fallback).</summary>
    public static bool MatchesClientSide(EmailMessage m, EmailPredicate p)
    {
        string[] haystacks = p.Field switch
        {
            EmailField.Subject => [m.Subject],
            EmailField.From => [m.FromAddress, m.FromName],
            EmailField.To => [.. m.ToRecipients],
            EmailField.Recipient => [.. m.ToRecipients, .. m.CcRecipients],
            EmailField.Body => [m.BodyContent ?? m.BodyPreview],
            _ => [],
        };

        bool Match(string h) => p.Op switch
        {
            EmailOperator.Equals => string.Equals(h, p.Value, StringComparison.OrdinalIgnoreCase),
            EmailOperator.Contains => h.Contains(p.Value, StringComparison.OrdinalIgnoreCase),
            EmailOperator.StartsWith => h.StartsWith(p.Value, StringComparison.OrdinalIgnoreCase),
            EmailOperator.Domain => h.Contains("@" + p.Value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

        return haystacks.Any(h => !string.IsNullOrEmpty(h) && Match(h));
    }

    private static string Bool(bool b) => b ? "true" : "false";
    private static string Escape(string value) => value.Replace("'", "''");
}
