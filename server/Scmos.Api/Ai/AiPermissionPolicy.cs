using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai;

public static class AiPermissionPolicy
{
    public static bool Authenticated(AppUser? user) => user is { Recognised: true }
        && !string.IsNullOrWhiteSpace(user.UserId);

    // A carrier requires a separately reviewed carrier-scoped adapter, not an operator owner filter.
    public static bool InternalUser(AppUser user) => Authenticated(user) && Roles.Find(user.Role) is { } role
        && role.Name != Roles.Subcontractor;

    public static AiReadScope? Scope(AppUser user)
    {
        if (!InternalUser(user)) return null;
        if (user.Can(Capability.ViewTeam)) return new(true, null);
        return string.IsNullOrWhiteSpace(user.OperatorId) ? null : new(false, user.OperatorId);
    }

    public static bool CanUse(AppUser user, AgentDefinition agent) => InternalUser(user)
        && user.Can(agent.RequiredCapability) && Scope(user) is not null;

    public static string AuthorizeTool(AppUser user, AgentDefinition agent, string name,
        ToolRegistry registry, bool durableAuditReady)
    {
        if (!CanUse(user, agent)) return "forbidden";
        var existing = AiPermissions.Find(name);
        if (AiPermissions.IsForbidden(name) || existing is null || existing.Permission == AiPermission.Deny)
            return "restricted";
        // This reports the policy; it does NOT create or execute an approval.
        if (existing.Permission == AiPermission.Approval) return "approval_required";
        var tool = registry.Find(name);
        if (tool is null) return "not_connected";
        if (tool.AgentId != agent.Id || !agent.AllowedTools.Contains(name)
            || !user.Can(tool.RequiredCapability)) return "forbidden";
        if (tool.Risk == AiRisk.Restricted) return "restricted";
        if (tool.Risk == AiRisk.High) return "approval_required";
        if (tool.Risk != AiRisk.Low) return "write_disabled";
        if (tool.Handler is null) return "not_connected";
        if (!durableAuditReady) return "audit_not_ready";
        return "allowed";
    }
}
