namespace Ten99.Aria.Mcp.Codecks;

/// <summary>
/// Process-wide Codecks API rate limit, shared across the per-domain tool classes so splitting the
/// old single <c>CodecksTool</c> into <c>org_/project_/deck_/card_/user_</c> groups doesn't multiply
/// the request budget. (ADO #437.)
/// </summary>
internal static class CodecksRateLimit
{
	public const int MaxRequests = 40;
	public static readonly SemaphoreSlim Limiter = new(MaxRequests, MaxRequests);
	public static DateTime LastReset = DateTime.Now;
}
