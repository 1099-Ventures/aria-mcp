using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Ado.Operations;

namespace Ten99.Aria.Mcp.Ado.Tools;

/// <summary>
/// Repositories domain (<c>repo_*</c>) — repositories and pull requests. Consolidated,
/// action-parameterized tools mirroring the MS azure-devops-mcp shape (ADO #433). Phase 1: repo +
/// PR read/write; threads/branches/file/commit-search land in Phase 2 (#436).
/// </summary>
[McpServerToolType]
public static class AdoRepositoryTool
{
	[McpServerTool(Name = "repo_repository")]
	[Description("Read Git repositories in the active org. action: get (by id or name — name needs project) | list (optionally scoped to project). Returns lean {id, name, project, defaultBranch, webUrl}.")]
	public static Task<CallToolResult> Repository(
		IConfiguration config, HttpClient httpClient,
		[Description("get | list")] string action,
		[Description("Project id or name. Required when repositoryId is a name (not a GUID), or to scope list.")] string? project = null,
		[Description("Repository id (GUID) or name. Required for get.")] string? repositoryId = null)
		=> new AdoRepositoryOperation().Execute(config, httpClient, action, project, repositoryId);

	[McpServerTool(Name = "repo_pull_request")]
	[Description("Read pull requests. action: get (by pullRequestId) | list (by repo; filter status active|abandoned|completed|all, sourceRefName, targetRefName, top) | list_by_commits (PRs containing given commit shas). Lean PR projection.")]
	public static Task<CallToolResult> PullRequest(
		IConfiguration config, HttpClient httpClient,
		[Description("get | list | list_by_commits")] string action,
		[Description("Repository id (GUID) or name. Required. Name needs project.")] string? repositoryId = null,
		[Description("Pull request id. Required for get.")] int? pullRequestId = null,
		[Description("Project id or name (required when repositoryId is a name).")] string? project = null,
		[Description("Status filter for list: active (default) | abandoned | completed | all.")] string? status = null,
		[Description("Filter list by source branch, e.g. refs/heads/my-branch.")] string? sourceRefName = null,
		[Description("Filter list by target branch, e.g. refs/heads/main.")] string? targetRefName = null,
		[Description("Max results for list (default 25).")] int? top = null,
		[Description("Space/comma-separated commit shas for list_by_commits.")] string? commitIds = null)
		=> new AdoPullRequestReadOperation().Execute(config, httpClient, action, repositoryId, pullRequestId, project, status, sourceRefName, targetRefName, top, commitIds);

	[McpServerTool(Name = "repo_pull_request_write")]
	[Description("Write pull requests. action: create (source/target/title[/description/workItems]; set autoComplete for one-call autocomplete with deleteSourceBranch/transitionWorkItems/mergeStrategy) | update (title/description/status active|abandoned/targetRefName/isDraft; toggle autoComplete) | update_reviewers (reviewerIds + reviewerAction add|remove) | vote (Approved|ApprovedWithSuggestions|NoVote|WaitingForAuthor|Rejected). Returns a lean ack {id, status, autoComplete, url}.")]
	public static Task<CallToolResult> PullRequestWrite(
		IConfiguration config, HttpClient httpClient,
		[Description("create | update | update_reviewers | vote")] string action,
		[Description("Repository id (GUID) or name. Required. Name needs project.")] string? repositoryId = null,
		[Description("Pull request id. Required for update, update_reviewers, vote.")] int? pullRequestId = null,
		[Description("Project id or name (required when repositoryId is a name).")] string? project = null,
		[Description("Source branch, e.g. refs/heads/my-branch. Required for create.")] string? sourceRefName = null,
		[Description("Target branch, e.g. refs/heads/main. Required for create.")] string? targetRefName = null,
		[Description("PR title. Required for create.")] string? title = null,
		[Description("PR description (Markdown, max 4000).")] string? description = null,
		[Description("Whether the PR is a draft.")] bool isDraft = false,
		[Description("Work item ids to link on create, space/comma-separated (e.g. '425 431').")] string? workItems = null,
		[Description("Enable/disable autocomplete (create or update). true = complete when policies pass.")] bool? autoComplete = null,
		[Description("Merge strategy for autocomplete: noFastForward | squash | rebase | rebaseMerge.")] string? mergeStrategy = null,
		[Description("Delete source branch on autocomplete (default false).")] bool deleteSourceBranch = false,
		[Description("Transition linked work items on autocomplete (default true).")] bool transitionWorkItems = true,
		[Description("New status for update: active | abandoned.")] string? status = null,
		[Description("Reviewer ids for update_reviewers.")] string[]? reviewerIds = null,
		[Description("add | remove — for update_reviewers.")] string? reviewerAction = null,
		[Description("Vote for vote action: Approved|ApprovedWithSuggestions|NoVote|WaitingForAuthor|Rejected.")] string? vote = null)
		=> new AdoPullRequestWriteOperation().Execute(config, httpClient, action, repositoryId, pullRequestId, project,
			sourceRefName, targetRefName, title, description, isDraft, workItems, autoComplete, mergeStrategy,
			deleteSourceBranch, transitionWorkItems, status, reviewerIds, reviewerAction, vote);

	[McpServerTool(Name = "repo_pull_request_thread")]
	[Description("Read PR review threads. action: list (threads on a PR) | list_comments (comments in a thread; needs threadId). Lean {id, status, filePath, comments}.")]
	public static Task<CallToolResult> PullRequestThread(
		IConfiguration config, HttpClient httpClient,
		[Description("list | list_comments")] string action,
		[Description("Repository id or name (name needs project).")] string? repositoryId = null,
		[Description("Pull request id.")] int? pullRequestId = null,
		[Description("Project id or name (required when repositoryId is a name).")] string? project = null,
		[Description("Thread id. Required for list_comments.")] int? threadId = null)
		=> new AdoPullRequestThreadOperation().Execute(config, httpClient, action, repositoryId, pullRequestId, project, threadId);

	[McpServerTool(Name = "repo_pull_request_thread_write")]
	[Description("Write PR review threads. action: create (content [+ filePath/rightFileStartLine/rightFileEndLine to anchor], status) | reply (threadId + content) | update_status (threadId + status: active|fixed|wontFix|closed|byDesign|pending).")]
	public static Task<CallToolResult> PullRequestThreadWrite(
		IConfiguration config, HttpClient httpClient,
		[Description("create | reply | update_status")] string action,
		[Description("Repository id or name (name needs project).")] string? repositoryId = null,
		[Description("Pull request id.")] int? pullRequestId = null,
		[Description("Project id or name (required when repositoryId is a name).")] string? project = null,
		[Description("Thread id. Required for reply and update_status.")] int? threadId = null,
		[Description("Comment content. Required for create and reply.")] string? content = null,
		[Description("Thread status (create/update_status): active|fixed|wontFix|closed|byDesign|pending.")] string? status = null,
		[Description("File path to anchor the thread (create).")] string? filePath = null,
		[Description("Start line in the right file (create, with filePath).")] int? rightFileStartLine = null,
		[Description("End line in the right file (create, with filePath).")] int? rightFileEndLine = null)
		=> new AdoPullRequestThreadWriteOperation().Execute(config, httpClient, action, repositoryId, pullRequestId, project, threadId, content, status, filePath, rightFileStartLine, rightFileEndLine);

	[McpServerTool(Name = "repo_branch")]
	[Description("Read branches (Git refs). action: get (by branchName) | list | list_mine (branches you've pushed to). Optional filterContains, top. Lean {name, objectId}.")]
	public static Task<CallToolResult> Branch(
		IConfiguration config, HttpClient httpClient,
		[Description("get | list | list_mine")] string action,
		[Description("Repository id or name (name needs project).")] string? repositoryId = null,
		[Description("Project id or name (required when repositoryId is a name).")] string? project = null,
		[Description("Branch name (without refs/heads/). Required for get.")] string? branchName = null,
		[Description("Max results for list/list_mine (default 100).")] int? top = null,
		[Description("Only branches whose name contains this string.")] string? filterContains = null)
		=> new AdoBranchOperation().Execute(config, httpClient, action, repositoryId, project, branchName, top, filterContains);

	[McpServerTool(Name = "repo_create_branch")]
	[Description("Create a branch from a source branch (default main) or an explicit sourceCommitId. Returns {success, branch, from, commitId}.")]
	public static Task<CallToolResult> CreateBranch(
		IConfiguration config, HttpClient httpClient,
		[Description("Repository id or name (name needs project).")] string? repositoryId = null,
		[Description("New branch name (without refs/heads/).")] string? branchName = null,
		[Description("Source branch to base from (default main).")] string? sourceBranchName = null,
		[Description("Explicit source commit sha (overrides sourceBranchName).")] string? sourceCommitId = null,
		[Description("Project id or name (required when repositoryId is a name).")] string? project = null)
		=> new AdoCreateBranchOperation().Execute(config, httpClient, repositoryId, branchName, sourceBranchName, sourceCommitId, project);

	[McpServerTool(Name = "repo_file")]
	[Description("Read repository items. action: get_content (text of a file; path required; optional version + versionType branch|commit|tag) | list_directory (files/folders at path; recursive optional).")]
	public static Task<CallToolResult> File(
		IConfiguration config, HttpClient httpClient,
		[Description("get_content | list_directory")] string action,
		[Description("Repository id or name (name needs project).")] string? repositoryId = null,
		[Description("File path (get_content) or directory scope (list_directory, default /).")] string? path = null,
		[Description("Project id or name (required when repositoryId is a name).")] string? project = null,
		[Description("Version: branch name, tag, or commit sha.")] string? version = null,
		[Description("How to interpret version: branch | commit | tag (default commit).")] string? versionType = null,
		[Description("List recursively (list_directory).")] bool recursive = false)
		=> new AdoFileOperation().Execute(config, httpClient, action, repositoryId, path, project, version, versionType, recursive);

	[McpServerTool(Name = "repo_search_commits")]
	[Description("Keyword commit search across the org (Search service), filterable by project/repository/branch/author (comma-separated) and date range. orderBy ASC|DESC. Lean {commitId, comment, author, date, repository}.")]
	public static Task<CallToolResult> SearchCommits(
		IConfiguration config, HttpClient httpClient,
		[Description("Keywords to match in commit messages.")] string searchText,
		[Description("Project name(s), comma-separated. Omit to search all.")] string? project = null,
		[Description("Repository name(s), comma-separated.")] string? repository = null,
		[Description("Branch name(s), comma-separated.")] string? branch = null,
		[Description("Author display name(s), comma-separated.")] string? author = null,
		[Description("From date (YYYY-MM-DD or ISO).")] string? commitStartDate = null,
		[Description("To date (YYYY-MM-DD or ISO).")] string? commitEndDate = null,
		[Description("Sort by date: ASC | DESC.")] string? orderBy = null,
		[Description("Max results (default 10).")] int? top = null,
		[Description("Results to skip (default 0).")] int? skip = null)
		=> new AdoCommitSearchOperation().Execute(config, httpClient, searchText, project, repository, branch, author, commitStartDate, commitEndDate, orderBy, top, skip);
}
