using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Ado.Operations;

namespace Ten99.Aria.Mcp.Ado.Tools;

/// <summary>
/// Pipelines domain (<c>pipelines_*</c>) — build/run monitoring (ADO #438, Phase 1: read).
/// Consolidated action-parameterized tools per the ADR; all are project-scoped.
/// </summary>
[McpServerToolType]
public static class AdoPipelinesTool
{
	[McpServerTool(Name = "pipelines_build")]
	[Description("Builds/runs of a project. action: list (filter definitions=csv ids, statusFilter, resultFilter, branchName, top, queryOrder) | get_status (buildId → status/result) | get_changes (buildId → commits + work items). Lean build projection.")]
	public static Task<CallToolResult> Build(
		IConfiguration config, HttpClient httpClient,
		[Description("list | get_status | get_changes")] string action,
		[Description("Project id or name.")] string project,
		[Description("Build id. Required for get_status/get_changes.")] int? buildId = null,
		[Description("Comma-separated build definition ids to filter (list).")] string? definitions = null,
		[Description("Status filter (list): e.g. completed, inProgress, notStarted, cancelling.")] string? statusFilter = null,
		[Description("Result filter (list): e.g. succeeded, failed, canceled, partiallySucceeded.")] string? resultFilter = null,
		[Description("Branch filter (list), e.g. refs/heads/main.")] string? branchName = null,
		[Description("Max results (default 20).")] int? top = null,
		[Description("Order (list), e.g. finishTimeDescending.")] string? queryOrder = null)
		=> new AdoBuildOperation().Execute(config, httpClient, action, project, buildId, definitions, statusFilter, resultFilter, branchName, top, queryOrder);

	[McpServerTool(Name = "pipelines_build_log")]
	[Description("A build's logs. action: list (available logs for a build) | get_content (text of a log by id, optional startLine/endLine).")]
	public static Task<CallToolResult> BuildLog(
		IConfiguration config, HttpClient httpClient,
		[Description("list | get_content")] string action,
		[Description("Project id or name.")] string project,
		[Description("Build id.")] int buildId,
		[Description("Log id. Required for get_content.")] int? logId = null,
		[Description("Start line (get_content).")] int? startLine = null,
		[Description("End line (get_content).")] int? endLine = null)
		=> new AdoBuildLogOperation().Execute(config, httpClient, action, project, buildId, logId, startLine, endLine);

	[McpServerTool(Name = "pipelines_definition")]
	[Description("Pipeline (build) definitions. action: list (filter name, top, queryOrder) | list_revisions (definitionId → revision history).")]
	public static Task<CallToolResult> Definition(
		IConfiguration config, HttpClient httpClient,
		[Description("list | list_revisions")] string action,
		[Description("Project id or name.")] string project,
		[Description("Definition id. Required for list_revisions.")] int? definitionId = null,
		[Description("Name filter (list).")] string? name = null,
		[Description("Max results (default 50).")] int? top = null,
		[Description("Order (list), e.g. lastModifiedDescending.")] string? queryOrder = null)
		=> new AdoDefinitionOperation().Execute(config, httpClient, action, project, definitionId, name, top, queryOrder);

	[McpServerTool(Name = "pipelines_run")]
	[Description("Runs of a pipeline (Pipelines API). action: list (runs for a pipeline) | get (a single run by id).")]
	public static Task<CallToolResult> Run(
		IConfiguration config, HttpClient httpClient,
		[Description("list | get")] string action,
		[Description("Project id or name.")] string project,
		[Description("Pipeline id (definition id).")] int pipelineId,
		[Description("Run id. Required for get.")] int? runId = null)
		=> new AdoPipelineRunOperation().Execute(config, httpClient, action, project, pipelineId, runId);

	[McpServerTool(Name = "pipelines_artifact")]
	[Description("Published artifacts of a build. action: list (name + download url per artifact).")]
	public static Task<CallToolResult> Artifact(
		IConfiguration config, HttpClient httpClient,
		[Description("list")] string action,
		[Description("Project id or name.")] string project,
		[Description("Build id.")] int buildId)
		=> new AdoBuildArtifactOperation().Execute(config, httpClient, action, project, buildId);
}
