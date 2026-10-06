using Microsoft.Graph;

namespace Ten99.Aria.Integration.Graph;

/// <summary>
/// Result of a Graph authentication attempt. Used for delegated flows
/// where token acquisition may fail and require user re-authorization.
/// </summary>
public record GraphAuthResult
{
	public GraphServiceClient? Client { get; init; }
	public bool RequiresReauth { get; init; }
	public string? Error { get; init; }

	public bool IsSuccess => Client is not null && !RequiresReauth;

	public static GraphAuthResult Success(GraphServiceClient client) => new() { Client = client };
	public static GraphAuthResult Reauth(string error) => new() { RequiresReauth = true, Error = error };
	public static GraphAuthResult Failure(string error) => new() { Error = error };
}
