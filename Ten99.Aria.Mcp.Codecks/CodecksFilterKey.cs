using System.Text.Json.Serialization;
using Ten99.Aria.Mcp.Codecks.Models;

namespace Ten99.Aria.Mcp.Codecks;

[JsonConverter(typeof(CodecksFilterKeyConverter))]
public class CodecksFilterKey(string entityType, object filter)
{
	public string EntityType { get; set; } = entityType;
	public object Filter { get; set; } = filter;
}

public static class Filters
{
	// === EXISTING FILTERS ===
	static public readonly CodecksFilterKey FilterDecks = new("decks", new CodecksFilter
	{
		Order = ["sortValue"],
		IsDeleted = false,
	});

	// === PROJECT FILTERS ===
	/// <summary>
	/// Filter for listing all projects - chronological order with reasonable limit
	/// </summary>
	static public readonly CodecksFilterKey FilterProjects = new("projects", new CodecksFilter
	{
		Limit = 20,
		Order = ["name"],
		IsDeleted = false,
	});

	static public CodecksFilterKey FilterProjectsPaginated(int offset, int limit = 20) => new("projects", new CodecksFilter
	{
		Limit = limit,
		Order = ["name"],
		Offset = offset,
	});

	/// <summary>
	/// Factory method for paginated deck filtering
	/// </summary>
	static public CodecksFilterKey FilterDecksPaginated(int offset) => new("decks", new CodecksFilter
	{
		Limit = 25,
		Order = ["sortValue", "createdAt"],
		IsDeleted = false,
		Offset = offset,
	});

	/// <summary>
	/// Factory method for paginated card filtering
	/// </summary>
	static public CodecksFilterKey FilterCardsPaginated(int offset) => new("cards", new CodecksFilter
	{
		Limit = 30,
		Order = ["createdAt"],
		Offset = offset,
	});

	/// <summary>
	/// Filter for active projects only - excludes deleted and inactive projects
	/// </summary>
	static public readonly CodecksFilterKey FilterActiveProjects = new("projects", new CodecksFilter
	{
		Limit = 20,
		Order = ["name"],
		IsDeleted = false,
		IsActive = true,
	});

	/// <summary>
	/// Filter for public projects - for public registry and discovery
	/// </summary>
	static public readonly CodecksFilterKey FilterPublicProjects = new("projects", new CodecksFilter
	{
		Limit = 20,
		Order = ["name"],
		IsDeleted = false,
		IsPublic = true,
	});

	// === USER & TEAM FILTERS ===
	/// <summary>
	/// Filter for project team members - sorted by tenure (longest first)
	/// </summary>
	static public readonly CodecksFilterKey FilterProjectUsers = new("explicitProjectUsers", new CodecksFilter
	{
		Order = ["createdAt"],
	});

	/// <summary>
	/// Filter for deck guardians - users with special deck permissions
	/// </summary>
	static public readonly CodecksFilterKey FilterDeckGuardians = new("guardians", new CodecksFilter
	{
		Order = ["createdAt"]
	});

	/// <summary>
	/// Filter for workspace users - all users in account
	/// </summary>
	static public readonly CodecksFilterKey FilterWorkspaceUsers = new("users", new CodecksFilter
	{
		Limit = 20,
		Order = ["name"],
	});

	// === MILESTONE & TIMELINE FILTERS ===
	/// <summary>
	/// Filter for project milestones - upcoming milestones first
	/// </summary>
	static public readonly CodecksFilterKey FilterMilestones = new("milestoneProjects", new CodecksFilter
	{
		Order = ["date", "createdAt"],
	});

	/// <summary>
	/// Filter for active milestones only - excludes completed and deleted milestones
	/// </summary>
	static public readonly CodecksFilterKey FilterActiveMilestones = new("milestoneProjects", new CodecksFilter
	{
		Order = ["date"],
		IsActive = true,
	});

	// === ACTIVITY & HISTORY FILTERS ===
	/// <summary>
	/// Filter for recent project activity - small preview of latest changes
	/// </summary>
	static public readonly CodecksFilterKey FilterRecentActivities = new("activities", new CodecksFilter
	{
		Limit = 10,
		Order = ["-createdAt"],
	});

	/// <summary>
	/// Filter for comprehensive activity history - larger historical view
	/// </summary>
	static public readonly CodecksFilterKey FilterActivityHistory = new("activities", new CodecksFilter
	{
		Limit = 50,
		Order = ["-createdAt"],
	});

	/// <summary>
	/// Filter for user-specific activities - activity by specific user
	/// </summary>
	static public readonly CodecksFilterKey FilterUserActivities = new("activities", new CodecksFilter
	{
		Limit = 25,
		Order = ["-createdAt"],
	});

	// === TAG & ORGANIZATION FILTERS ===
	/// <summary>
	/// Filter for project tags - alphabetical order for consistency
	/// </summary>
	static public readonly CodecksFilterKey FilterProjectTags = new("tags", new CodecksFilter
	{
		Order = ["name"],
	});

	/// <summary>
	/// Filter for master tags - global tag definitions
	/// </summary>
	static public readonly CodecksFilterKey FilterMasterTags = new("masterTags", new CodecksFilter
	{
		Order = ["name"]
	});

	// === WORKFLOW & STATUS FILTERS ===
	/// <summary>
	/// Filter for workflow items - deck workflow states
	/// </summary>
	static public readonly CodecksFilterKey FilterWorkflowItems = new("workflowItems", new CodecksFilter
	{
		Order = ["sortValue", "name"],
	});

	/// <summary>
	/// Filter for active workflow items only
	/// </summary>
	static public readonly CodecksFilterKey FilterActiveWorkflowItems = new("workflowItems", new CodecksFilter
	{
		Order = ["sortValue"],
		IsActive = true,
	});

	// === SPECIALIZED CARD FILTERS ===
	/// <summary>
	/// Filter for cards with blocking dependencies
	/// </summary>
	static public readonly CodecksFilterKey FilterBlockedCards = new("cards", new CodecksFilter
	{
		Limit = 25,
		Order = ["-lastUpdatedAt"],
		IsDeleted = false,
		HasBlockingDeps = true,
	});

	/// <summary>
	/// Filter for unassigned cards - cards without assignees
	/// </summary>
	static public readonly CodecksFilterKey FilterUnassignedCards = new("cards", new CodecksFilter
	{
		Limit = 30,
		Order = ["createdAt"],
		IsDeleted = false,
	});

	/// <summary>
	/// Filter for high-effort cards - cards requiring significant work
	/// </summary>
	static public readonly CodecksFilterKey FilterHighEffortCards = new("cards", new CodecksFilter
	{
		Limit = 15,
		Order = ["effort", "-lastUpdatedAt"],
		IsDeleted = false,
		MinEffort = 8, // High effort threshold
	});

	// === DECK ORGANIZATION FILTERS ===
	/// <summary>
	/// Filter for active decks - excludes deleted decks, sorted by sort value
	/// </summary>
	static public readonly CodecksFilterKey FilterActiveDecks = new("decks", new CodecksFilter
	{
		Order = ["sortValue"],
		IsDeleted = false,
	});

	/// <summary>
	/// Filter for decks with guardians - decks that have special access control
	/// </summary>
	static public readonly CodecksFilterKey FilterGuardedDecks = new("decks", new CodecksFilter
	{
		Order = ["title"],
		IsDeleted = false,
	});

	// === TIME-BASED FILTERS ===
	/// <summary>
	/// Filter for recently created items - last 30 days
	/// </summary>
	static public readonly CodecksFilterKey FilterRecentItems = new("cards", new CodecksFilter
	{
		Limit = 25,
		Order = ["-createdAt"],
		IsDeleted = false,
		CreatedAfter = DateTime.Now.AddDays(-30),
	});

	/// <summary>
	/// Filter for recently updated items - last 7 days
	/// </summary>
	static public readonly CodecksFilterKey FilterRecentlyUpdated = new("cards", new CodecksFilter
	{
		Limit = 25,
		Order = ["-lastUpdatedAt"],
		IsDeleted = false,
		UpdatedAfter = DateTime.Now.AddDays(-7),
	});
}

