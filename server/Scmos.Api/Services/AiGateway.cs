using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record ToolView(string Name, string Agent, string Permission, string Description, bool Enabled);

public record ToolOutcome(
    bool Ok,
    string Message,
    /// <summary>ran · needs-approval · refused</summary>
    string Kind,
    object? Data = null,
    long? ApprovalId = null);

public record ApprovalView(
    long Id, string Tool, string Agent, string Summary, string Payload, string State,
    string RequestedBy, DateTimeOffset RequestedAt,
    string DecidedBy, DateTimeOffset? DecidedAt, string DecisionNote, string Result);

/// <summary>
/// Everything the assistant does passes through here.
///
/// The gateway is the enforcement point for the permission matrix, and it is
/// deliberately the only way in. A tool marked Deny — or any name that looks
/// like a deletion — is refused before anything is dispatched, so "the AI must
/// never delete a record" holds even if a future agent is told otherwise, even
/// if a prompt is injected, and even if somebody edits the system message.
///
/// A tool marked Approval never writes. It produces the exact payload it would
/// have applied and parks it for a person; approving applies what was reviewed
/// rather than asking a model again, whose second answer may differ from the
/// one that was read.
/// </summary>
public class AiGateway(ScmosDbContext db)
{
    public async Task<IReadOnlyList<ToolView>> ToolsAsync(CancellationToken token)
    {
        var stored = await db.AiTools.AsNoTracking().OrderBy(tool => tool.Agent).ThenBy(tool => tool.Name)
            .ToListAsync(token);

        // The database is the roster; the code is the authority on what a
        // permission means. A row edited to say "allow" for a tool the catalogue
        // calls approval is ignored, so a stray UPDATE cannot widen the matrix.
        return stored.Select(tool =>
        {
            var definition = AiPermissions.Find(tool.Name);
            var permission = definition?.Permission.ToString().ToLowerInvariant() ?? "deny";
            return new ToolView(tool.Name, tool.Agent, permission, tool.Description, tool.Enabled);
        }).ToList();
    }

    /// <summary>
    /// Runs a tool, parks it for approval, or refuses it.
    ///
    /// `execute` is only ever invoked for a tool the catalogue marks Allow. The
    /// caller supplies it, so the gateway does not need to know what any
    /// particular tool does — only whether it is permitted.
    /// </summary>
    public async Task<ToolOutcome> InvokeAsync(
        string toolName, string summary, object payload, AppUser user,
        Func<Task<object?>>? execute, CancellationToken token)
    {
        var name = (toolName ?? "").Trim();

        if (AiPermissions.IsForbidden(name))
        {
            return new ToolOutcome(false,
                $"ปฏิเสธ: {name} เป็นการกระทำที่ AI ทำไม่ได้ไม่ว่ากรณีใด (การลบข้อมูล)",
                "refused");
        }

        var definition = AiPermissions.Find(name);
        if (definition is null)
            return new ToolOutcome(false, $"ไม่รู้จักเครื่องมือ {name}", "refused");

        var row = await db.AiTools.AsNoTracking().FirstOrDefaultAsync(tool => tool.Name == name, token);
        if (row is { Enabled: false })
            return new ToolOutcome(false, $"{name} ถูกปิดใช้งานอยู่", "refused");

        switch (definition.Permission)
        {
            case AiPermission.Deny:
                return new ToolOutcome(false, $"ปฏิเสธ: {name} ไม่อยู่ในสิทธิ์ของ AI", "refused");

            case AiPermission.Approval:
            {
                var approval = new Approval
                {
                    Tool = name,
                    Agent = definition.Agent,
                    Summary = summary.Trim().Length > 0 ? summary.Trim() : definition.Description,
                    Payload = JsonSerializer.Serialize(payload),
                    State = "pending",
                    RequestedBy = user.Signature,
                    RequestedAt = DateTimeOffset.UtcNow,
                };
                db.Approvals.Add(approval);
                await db.SaveChangesAsync(token);

                return new ToolOutcome(true,
                    $"ร่างไว้แล้ว — ต้องมีคนอนุมัติก่อนจึงจะมีผล ({definition.Description})",
                    "needs-approval", payload, approval.Id);
            }

            default:
            {
                if (execute is null)
                    return new ToolOutcome(false, $"{name} ยังไม่ได้ต่อกับข้อมูลจริง", "refused");
                var data = await execute();
                return new ToolOutcome(true, "สำเร็จ", "ran", data);
            }
        }
    }

    public async Task<IReadOnlyList<ApprovalView>> ApprovalsAsync(string? state, CancellationToken token)
    {
        var query = db.Approvals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state) && state != "All") query = query.Where(a => a.State == state);

        return await query.OrderByDescending(a => a.Id).Take(200)
            .Select(a => new ApprovalView(a.Id, a.Tool, a.Agent, a.Summary, a.Payload, a.State,
                a.RequestedBy, a.RequestedAt, a.DecidedBy, a.DecidedAt, a.DecisionNote, a.Result))
            .ToListAsync(token);
    }

    /// <summary>
    /// A person's decision.
    ///
    /// Only a supervisor may give it: an Operation User approving the
    /// assistant's edit to their own job would just be the assistant editing it.
    /// Approving records the decision — applying the payload is a separate,
    /// explicit step, so nothing is written by the act of reading the queue.
    /// </summary>
    public async Task<ToolOutcome> DecideAsync(long id, bool approved, string note,
        AppUser user, CancellationToken token)
    {
        if (!AiPermissions.CanApprove(user.Role))
            return new ToolOutcome(false, "อนุมัติได้เฉพาะระดับหัวหน้างานขึ้นไป", "refused");

        var approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == id, token);
        if (approval is null) return new ToolOutcome(false, "ไม่พบรายการนี้", "refused");
        if (approval.State != "pending")
            return new ToolOutcome(false, $"รายการนี้ตัดสินไปแล้ว ({approval.State})", "refused");

        approval.State = approved ? "approved" : "rejected";
        approval.DecidedBy = user.Signature;
        approval.DecidedAt = DateTimeOffset.UtcNow;
        approval.DecisionNote = note.Trim();
        await db.SaveChangesAsync(token);

        return new ToolOutcome(true,
            approved ? "อนุมัติแล้ว — กด “นำไปใช้” เพื่อให้มีผล" : "ปฏิเสธแล้ว",
            "ran", null, approval.Id);
    }

    /// <summary>Marks an approved change as applied, with what happened.</summary>
    public async Task<ToolOutcome> MarkAppliedAsync(long id, string result, CancellationToken token)
    {
        var approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == id, token);
        if (approval is null) return new ToolOutcome(false, "ไม่พบรายการนี้", "refused");
        if (approval.State != "approved")
            return new ToolOutcome(false, "ต้องอนุมัติก่อนจึงจะนำไปใช้ได้", "refused");

        approval.State = "applied";
        approval.Result = result.Trim();
        await db.SaveChangesAsync(token);
        return new ToolOutcome(true, "นำไปใช้แล้ว", "ran", null, id);
    }
}
