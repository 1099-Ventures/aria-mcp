using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Ten99.Aria.Mcp.Codecks.Operations;

namespace Ten99.Aria.Mcp.Codecks.Tools;

/// <summary>
/// Journey domain (<c>journey_*</c>) — Codecks workflows. Phase 1: browse the published-template gallery
/// and clone a template into a deck. (Phase 2 will add authoring items, step zones, and applying a
/// journey to a card.)
/// </summary>
[McpServerToolType]
public static class CodecksJourneyTool
{
	[McpServerTool(Name = "journey_list_templates")]
	[Description("Browse Codecks' published workflow (Journey) templates — the community gallery (e.g. 'Steam Page Setup'). OPTIONAL: limit (default 25; ordered by most-cloned). Returns {count, templates:[{id, name, description, authorName, tags, isStaffPick, count:items, cloneCount}]}. Then use journey_get_template for a template's items, and journey_clone_template to stand it up in a deck.")]
	public static Task<CallToolResult> ListTemplates(IConfiguration config, HttpClient httpClient, int limit = 25)
		=> new CodecksListTemplatesOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, limit);

	[McpServerTool(Name = "journey_get_template")]
	[Description("Get a published workflow (Journey) template's detail by id: name, description, author, tags, step-zone labels (itemOrderLabels), its decks (deckLabelMap), cloneCount, and its items (orderLabel, title, effort, priority). REQUIRED: templateId (from journey_list_templates).")]
	public static Task<CallToolResult> GetTemplate(IConfiguration config, HttpClient httpClient, string templateId)
		=> new CodecksGetTemplateOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, templateId);

	[McpServerTool(Name = "journey_clone_template")]
	[Description("Clone a published workflow (Journey) template into a deck, creating the journey's template items there. REQUIRED: templateId (from journey_list_templates), targetDeckId (the deck to clone into). OPTIONAL: mode (default 'replace'). All of the template's decks are mapped onto the single targetDeckId. After cloning, use journey_apply to instantiate the journey onto a card. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> CloneTemplate(IConfiguration config, HttpClient httpClient, string templateId, string targetDeckId, string? mode = null)
		=> new CodecksCloneTemplateOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.ExecuteAsync(config, httpClient, templateId, targetDeckId, mode);

	[McpServerTool(Name = "journey_add_item")]
	[Description("Add a workflow item (a journey/template step) to a deck. A deck that holds workflow items is a Journey. REQUIRED: deckId (string), content (string, markdown). OPTIONAL: assigneeId (string), effort (int), priority ('a' high / 'b' medium / 'c' low), label (the step-zone this item sits in — see journey_set_step_zones), targetDeckId (deck where applied cards land). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> AddItem(IConfiguration config, HttpClient httpClient, string deckId, string content, string? assigneeId = null, int? effort = null, string? priority = null, string? label = null, string? targetDeckId = null)
		=> new CodecksCreateWorkflowItemOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new Models.CodecksCreateWorkflowItemRequest { DeckId = deckId, Content = content, AssigneeId = assigneeId, Effort = effort, Priority = priority, Label = label, TargetDeckId = targetDeckId });

	[McpServerTool(Name = "journey_set_step_zones")]
	[Description("Set the step zones (ordered group labels) of a Journey deck. REQUIRED: deckId (string), zones (string array, in display order). Replaces the deck's named zones; the default/ungrouped zone is kept implicitly. A workflow item is placed in a zone via its label (journey_add_item). Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> SetStepZones(IConfiguration config, HttpClient httpClient, string deckId, string[] zones)
		=> new CodecksSetStepZonesOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new Models.CodecksSetStepZonesRequest { DeckId = deckId, Zones = zones ?? [] });

	[McpServerTool(Name = "journey_list_decks")]
	[Description("List the decks that are journeys — decks holding one or more workflow items — across the token's projects. Use this to find a handcrafted (or previously cloned) journey to reuse. Returns {count, journeys:[{id, title, projectId, count:workflowItems}]}. Then journey_get_items to read its items and journey_apply to stamp it onto a card.")]
	public static Task<CallToolResult> ListDecks(IConfiguration config, HttpClient httpClient)
		=> new CodecksListJourneyDecksOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient);

	[McpServerTool(Name = "journey_get_items")]
	[Description("Read a journey deck's workflow items (handcrafted or cloned). REQUIRED: deckId. Returns {count, items:[{itemId, title, label, content, effort, priority, targetDeck}]}. Pass the itemIds to journey_apply to instantiate the journey onto a card.")]
	public static Task<CallToolResult> GetItems(IConfiguration config, HttpClient httpClient, string deckId)
		=> new CodecksGetJourneyItemsOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, deckId);

	[McpServerTool(Name = "journey_apply")]
	[Description("Apply a Journey to a card: the card becomes the Hero and the journey's items are generated as real cards under it. REQUIRED: cardId (the base/hero card), itemIds (string array of workflow item ids from the journey deck). Returns the created card ids. Requires a producer-tier (or higher) token.")]
	public static Task<CallToolResult> Apply(IConfiguration config, HttpClient httpClient, string cardId, string[] itemIds)
		=> new CodecksApplyJourneyOperation(CodecksRateLimit.Limiter, ref CodecksRateLimit.LastReset)
			.Execute(config, httpClient, new Models.CodecksApplyJourneyRequest { CardId = cardId, ItemIds = itemIds ?? [] });
}
