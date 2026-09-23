using Scmos.Api.Ai;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Documents;
using Scmos.Api.Ai.Engineering;
using Scmos.Api.Ai.Operations;
using Scmos.Api.Ai.Sre;
using Scmos.Api.Rules;

/// <summary>
/// The platform's four lists, checked against each other.
///
/// <para>
/// A read exists in four places at once: the tool registry (what the model
/// may be offered), the agent registry (whose tool it is), the permission
/// catalogue (whether it is allowed at all), and the audit vocabulary (what
/// a row may say). Every one of them was written by hand. This codebase's
/// oldest lesson is that a rule written twice drifts, and these are four
/// copies of the same fact — so a tool added to the registry and forgotten
/// in the audit, or an agent added without a flag of its own, is caught
/// here rather than by a run that dies at the audit write with a person
/// waiting for the answer.
/// </para>
///
/// <para>Pure: no fixtures, no adapters, no clock.</para>
/// </summary>
static class PlatformVocabularyCheck
{
    public static void Run(Action<bool, string> check)
    {
        var agents = new AgentRegistry();
        // No adapters: a tool's identity, ownership and vocabulary do not depend on being plugged in.
        var tools = new ToolRegistry();

        foreach (var tool in tools.All)
        {
            var owner = agents.Find(tool.AgentId);
            check(owner is not null && owner.AllowedTools.Contains(tool.Name, StringComparer.Ordinal),
                $"vocabulary: {tool.Name} is listed by the agent it says owns it ({tool.AgentId})");
            check(agents.All.Count(agent => agent.AllowedTools.Contains(tool.Name, StringComparer.Ordinal)) == 1,
                $"vocabulary: {tool.Name} belongs to exactly one agent");
            check(AiAuditRules.KnownTools.Contains(tool.Name, StringComparer.Ordinal),
                $"vocabulary: the audit knows {tool.Name} — a tool it does not know cannot be recorded, and a read that cannot be recorded is not released");
            check(AiPermissions.Find(tool.Name) is { Permission: AiPermission.Allow },
                $"vocabulary: the catalogue permits {tool.Name}");
            check(!AiPermissions.IsForbidden(tool.Name), $"vocabulary: {tool.Name} is not on the forbidden list");
            check(tool.Risk == AiRisk.Low && tool.Policy is { ActionLevel: AiActionLevel.Read },
                $"vocabulary: {tool.Name} is a low-risk read — nothing in the registry may be a write");
        }

        // Every name the audit will accept is a tool that exists. extract_document is the
        // Workspace's own extractor, audited through the platform since Phase 5 without being
        // offered to a model, so it is the one name that is allowed to have no registry entry.
        foreach (var name in AiAuditRules.KnownTools.Where(name => name != "extract_document"))
            check(tools.Find(name) is not null, $"vocabulary: the audit's tool {name} is a tool the registry has");
        check(AiPermissions.Find("extract_document") is { Permission: AiPermission.Allow } && tools.Find("extract_document") is null,
            "vocabulary: extract_document is permitted and audited but never offered to a model");

        // Every agent the audit may record is an agent the registry has, and every agent with a
        // connected executor is one the audit may record — the pair a run needs to be written at all.
        foreach (var id in AiAuditRules.KnownAgents)
            check(agents.Find(id) is not null, $"vocabulary: the audit's agent {id} is in the registry");

        // A flag of its own for every agent, found by name rather than by a second list:
        // a twelfth agent without one would be permanently off, or worse, on with somebody else's.
        foreach (var agent in agents.All)
        {
            var prefix = agent.Id.Replace("-agent", "");
            var property = typeof(AiOptions).GetProperty(char.ToUpperInvariant(prefix[0]) + prefix[1..] + "AgentEnabled");
            check(property is not null, $"vocabulary: {agent.Id} has a flag of its own (AI:{property?.Name ?? "…"})");
            if (property is null) continue;
            var own = new AiOptions();
            property.SetValue(own, true);
            check(AgentRegistry.Enabled(agent, own) && !AgentRegistry.Enabled(agent, new AiOptions()),
                $"vocabulary: {agent.Id} is on with its own flag and off without it");
            check(agents.All.Where(other => other.Id != agent.Id).All(other => !AgentRegistry.Enabled(other, own)),
                $"vocabulary: {agent.Id}'s flag turns on nothing but {agent.Id}");
        }

        // The resolver takes the first agent claiming a page, so two claims would make one of them unreachable.
        var pages = agents.All.SelectMany(agent => agent.Pages).ToList();
        check(pages.Distinct(StringComparer.Ordinal).Count() == pages.Count,
            "vocabulary: no two agents claim the same page — the page router would silently prefer one");
        foreach (var agent in agents.All)
            check(agent.Pages.All(page => agents.Resolve(new("x", Context: new(page)))?.Id == agent.Id),
                $"vocabulary: every page {agent.Id} claims routes to it");

        // The audit's view words are the reads' own words. A view the read returns and the audit
        // refuses is a run that dies at the write; one the audit allows and the read never returns
        // is a permission to record something that cannot happen.
        check(AiAuditRules.PlatformViews.SequenceEqual(PlatformReadService.Views), "vocabulary: the audit's platform views are the SRE read's own");
        check(AiAuditRules.DocumentViews.SequenceEqual(DocumentsReadService.Views), "vocabulary: the audit's document views are the paperwork read's own");
        check(AiAuditRules.MessageViews.SequenceEqual(MessagesReadService.Views), "vocabulary: the audit's message views are the communication read's own");
        check(AiAuditRules.SourceViews.SequenceEqual(SourceReadService.Modes), "vocabulary: the audit's source views are the source read's own modes");
        check(AiAuditRules.FollowUpViews.SequenceEqual(OperationsReadService.FollowUpViews), "vocabulary: the audit's follow-up views are the operations read's own");

        // What the schema offers the model is what the read accepts: a choice the read would
        // refuse is an invalid_tool the person sees as "AI ทำไม่ได้", and one the schema omits
        // is a view nobody can ask for.
        foreach (var (tool, views) in new (string Tool, IReadOnlyList<string> Views)[]
        {
            (PlatformReadService.Tool, PlatformReadService.Views),
            (DocumentsReadService.Tool, DocumentsReadService.Views),
            (MessagesReadService.Tool, MessagesReadService.Views),
            (EngineeringReadService.Tool, EngineeringReadService.Views),
        })
        {
            var schema = tools.Find(tool)!.InputSchema.Json;
            check(views.All(view => schema.Contains($"\"{view}\"", StringComparison.Ordinal)),
                $"vocabulary: {tool}'s schema offers every view it answers");
        }

        // The plans are not reads: they are never in the registry, and the audit never names one as a tool.
        foreach (var plan in Scmos.Api.Ai.Management.ManagementPlans.All)
        {
            check(tools.Find(plan.Name) is null && !AiAuditRules.KnownTools.Contains(plan.Name, StringComparer.Ordinal),
                $"vocabulary: the plan {plan.Name} is not a read and is never audited as one");
            check(AiPermissions.Find(plan.Name) is { Permission: AiPermission.Allow },
                $"vocabulary: the plan {plan.Name} is in the catalogue like everything else a person may set off");
            check(plan.Steps.All(step => tools.Find(step.Tool) is not null),
                $"vocabulary: every step of {plan.Name} is a registry read");
        }
    }
}
