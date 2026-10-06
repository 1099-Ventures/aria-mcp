using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.Codecks.Operations;

internal abstract class CodecksOperationBaseExtended<TOperation, TRequest>(SemaphoreSlim rateLimiter, ref DateTime lastReset)
	: CodecksOperationBase<TOperation>(rateLimiter, ref lastReset)
	where TOperation : CodecksOperationBaseExtended<TOperation, TRequest>
{
	protected override object BuildRequest()
	{
		throw new NotImplementedException();
	}

	protected abstract object BuildRequest(TRequest request);

	public async Task<CallToolResult> Execute(IConfiguration config, HttpClient httpClient, TRequest request)
	{
		try
		{
			return FormatResponse(await MakeApiRequest(config, httpClient, BuildRequest(request), Endpoint));
		}
		catch (Exception ex)
		{
			return new CallToolResult
			{
				Content = [new TextContentBlock { Text = $"Error {OperationName}: {ex.Message}" }]
			};
		}
	}
}