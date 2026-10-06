using ModelContextProtocol.Protocol;

namespace Ten99.Aria.Mcp.LocalFiles.Operations;

internal interface IFileOperation<T>
{
	Task<CallToolResult> ExecuteAsync(T arg);
	Task<CallToolResult> ExecuteAsync(T arg, CancellationToken cancellationToken);
}

internal interface IFileOperation<T1, T2>
{
	Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2);
	Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, CancellationToken cancellationToken);
}

internal interface IFileOperation<T1, T2, T3>
{
	Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3);
	Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, CancellationToken cancellationToken);
}

internal interface IFileOperation<T1, T2, T3, T4, T5>
{
	Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
	Task<CallToolResult> ExecuteAsync(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, CancellationToken cancellationToken);
}