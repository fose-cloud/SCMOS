using System.Text.Json;
using Scmos.Api.Ai;
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
    string DecidedBy, DateTimeOffset? DecidedAt, string DecisionNote, string Result,
    /// <summary>The fingerprint an approver reads and "applied" must quote (1E).</summary>
    string PayloadHash,
    DateTimeOffset? ExpiresAt,
    string AppliedBy, DateTimeOffset? AppliedAt,
    /// <summary>Whether the reader made this proposal.</summary>
    bool Mine,
    /// <summary>What the reader may do to it now: approve · reject · cancel · apply.</summary>
    IReadOnlyList<string> Actions);

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
///
/// <para>
/// Since Phase 1E (20 Sep 2026) the queue's rules are <see cref="ApprovalPolicy"/>:
/// a proposal is made by an internal account that holds the capability the
/// tool stands in for; the requester and approvers see it; an approver who
/// is not its requester decides it, once, with the change of state made
/// under a condition so two people cannot both decide; it expires untouched;
/// "applied" is recorded only against the fingerprint the approver read.
/// The routes ask for the second factor before a decision.
/// </para>
/// </summary>
public class AiGateway(ScmosDbContext db)
{
    // Additive chat entry; legacy tools/approval/extraction behavior is unchanged.
    public AiStatus Status(AppUser user, AgentOrchestrator orchestrator) => orchestrator.Status(user);
    public Task<AiChatOutcome> ChatAsync(AiChatRequest? request, AppUser? user, AgentOrchestrator orchestrator, CancellationToken token)
        => orchestrator.RunAsync(request, user, token);

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
        Func<Task<object?>>? execute, CancellationToken token, string correlationId = "")
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
                // The proposal is bound to the account that could make the
                // change by hand, and the fields are held to plain text of a
                // bounded size — the arguments of a tool, never who is asking.
                var fields = payload as IReadOnlyDictionary<string, string>;
                if (ApprovalPolicy.RequestProblem(user, definition, summary.Trim(), fields) is { } problem)
                    return new ToolOutcome(false, RequestRefusal(problem), "refused");

                var now = DateTimeOffset.UtcNow;
                var json = JsonSerializer.Serialize(payload);
                var approval = new Approval
                {
                    Tool = name,
                    Agent = definition.Agent,
                    Summary = summary.Trim().Length > 0 ? summary.Trim() : definition.Description,
                    Payload = json,
                    PayloadHash = ApprovalPolicy.Hash(json),
                    State = ApprovalPolicy.Pending,
                    RequestedBy = user.Signature,
                    RequesterId = user.UserId,
                    RequestedAt = now,
                    ExpiresAt = ApprovalPolicy.ExpiryOf(ApprovalPolicy.Pending, now),
                    CorrelationId = (correlationId ?? "").Length > 64 ? correlationId![..64] : correlationId ?? "",
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

    /// <summary>
    /// The queue as this person may see it: all of it for an approver, their
    /// own proposals for anybody else, nothing for a carrier. Rows past their
    /// expiry are marked expired first, so the list never shows as pending
    /// what nobody may decide any more.
    /// </summary>
    public async Task<IReadOnlyList<ApprovalView>> ApprovalsAsync(AppUser user, string? state, CancellationToken token)
    {
        if (!AiPermissionPolicy.InternalUser(user)) return [];
        var now = DateTimeOffset.UtcNow;
        await ExpireAsync(now, token);

        var query = db.Approvals.AsNoTracking().Where(a => a.Agent != Scmos.Api.Ai.Operations.OperationsChangePolicy.Agent);
        if (!ApprovalPolicy.IsApprover(user))
        {
            var id = user.UserId;
            var signature = user.Signature;
            query = query.Where(a => a.RequesterId == id || (a.RequesterId == "" && a.RequestedBy == signature));
        }
        if (!string.IsNullOrWhiteSpace(state) && state != "All") query = query.Where(a => a.State == state);

        var rows = await query.OrderByDescending(a => a.Id).Take(200).ToListAsync(token);
        return rows.Select(a => View(a, user, now)).ToList();
    }

    private static ApprovalView View(Approval a, AppUser user, DateTimeOffset now) =>
        new(a.Id, a.Tool, a.Agent, a.Summary, a.Payload, a.State,
            a.RequestedBy, a.RequestedAt, a.DecidedBy, a.DecidedAt, a.DecisionNote, a.Result,
            ApprovalPolicy.ExpectedHash(a), a.ExpiresAt, a.AppliedBy, a.AppliedAt,
            ApprovalPolicy.IsRequester(user, a), ApprovalPolicy.ActionsFor(user, a, now));

    /// <summary>Marks every pending or approved row past its expiry as expired — one statement, no row read.</summary>
    public Task<int> ExpireAsync(DateTimeOffset now, CancellationToken token) =>
        db.Approvals
            .Where(a => (a.State == ApprovalPolicy.Pending || a.State == ApprovalPolicy.Approved) && a.ExpiresAt != null && a.ExpiresAt <= now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.State, ApprovalPolicy.Expired)
                .SetProperty(a => a.ExpiresAt, (DateTimeOffset?)null), token);

    /// <summary>One row as this person may see it, or null when it does not exist or is not theirs to see — the same answer for both.</summary>
    public async Task<Approval?> FindAsync(long id, AppUser user, CancellationToken token)
    {
        var approval = await db.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, token);
        return approval is not null && ApprovalPolicy.CanSee(user, approval) ? approval : null;
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
        if (!ApprovalPolicy.IsApprover(user))
            return new ToolOutcome(false, "อนุมัติได้เฉพาะระดับหัวหน้างานขึ้นไป", "refused");
        if ((note ?? "").Length > ApprovalPolicy.MaxSummaryLength)
            return new ToolOutcome(false, "หมายเหตุยาวเกิน 500 ตัวอักษร", "refused");

        var approval = await db.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, token);
        if (approval is null) return new ToolOutcome(false, "ไม่พบรายการนี้", "refused");
        if (approval.Agent == Scmos.Api.Ai.Operations.OperationsChangePolicy.Agent)
            return new(false, "ต้องยืนยันผ่าน Operations changes", "refused");

        var now = DateTimeOffset.UtcNow;
        if (ApprovalPolicy.DecideProblem(user, approval, now) is { } problem)
            return new ToolOutcome(false, problem switch
            {
                "self" => "อนุมัติข้อเสนอของตัวเองไม่ได้ — ต้องเป็นหัวหน้างานคนอื่น",
                "expired" => "รายการนี้หมดอายุแล้ว — ให้เสนอใหม่",
                _ => $"รายการนี้ตัดสินไปแล้ว ({approval.State})",
            }, "refused");

        // The change of state is conditional on the state it was read in, so
        // two approvers deciding at once have one effect and the other is told.
        var state = approved ? ApprovalPolicy.Approved : ApprovalPolicy.Rejected;
        var expires = ApprovalPolicy.ExpiryOf(state, now);
        var by = user.Signature;
        var trimmed = (note ?? "").Trim();
        var changed = await db.Approvals
            .Where(a => a.Id == id && a.State == ApprovalPolicy.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.State, state)
                .SetProperty(a => a.DecidedBy, by)
                .SetProperty(a => a.DecidedAt, now)
                .SetProperty(a => a.DecisionNote, trimmed)
                .SetProperty(a => a.ExpiresAt, expires), token);
        if (changed == 0)
            return new ToolOutcome(false, "มีคนตัดสินรายการนี้ไปก่อนแล้ว", "refused");

        return new ToolOutcome(true,
            approved ? "อนุมัติแล้ว — กด “นำไปใช้” เมื่อทำการเปลี่ยนแปลงแล้ว" : "ปฏิเสธแล้ว",
            "ran", null, approval.Id);
    }

    /// <summary>The requester (or an approver) withdraws a pending proposal.</summary>
    public async Task<ToolOutcome> CancelAsync(long id, string note, AppUser user, CancellationToken token)
    {
        var approval = await db.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, token);
        if (approval is null || !ApprovalPolicy.CanSee(user, approval)) return new ToolOutcome(false, "ไม่พบรายการนี้", "refused");
        if (approval.Agent == Scmos.Api.Ai.Operations.OperationsChangePolicy.Agent)
            return new(false, "ต้องยืนยันผ่าน Operations changes", "refused");
        var now = DateTimeOffset.UtcNow;
        if (ApprovalPolicy.CancelProblem(user, approval, now) is { } problem)
            return new ToolOutcome(false, problem switch
            {
                "who" => "ถอนได้เฉพาะผู้เสนอหรือหัวหน้างาน",
                "expired" => "รายการนี้หมดอายุแล้ว",
                _ => $"รายการนี้ตัดสินไปแล้ว ({approval.State})",
            }, "refused");

        var by = user.Signature;
        var trimmed = (note ?? "").Trim();
        var changed = await db.Approvals
            .Where(a => a.Id == id && a.State == ApprovalPolicy.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.State, ApprovalPolicy.Cancelled)
                .SetProperty(a => a.DecidedBy, by)
                .SetProperty(a => a.DecidedAt, now)
                .SetProperty(a => a.DecisionNote, trimmed)
                .SetProperty(a => a.ExpiresAt, (DateTimeOffset?)null), token);
        return changed == 0
            ? new ToolOutcome(false, "มีคนตัดสินรายการนี้ไปก่อนแล้ว", "refused")
            : new ToolOutcome(true, "ถอนข้อเสนอแล้ว", "ran", null, id);
    }

    /// <summary>
    /// Records that an approved change was made — by a person, by hand —
    /// against the fingerprint the approver read. Nothing is executed here;
    /// the result text is what the person says happened.
    /// </summary>
    public async Task<ToolOutcome> MarkAppliedAsync(long id, string result, string reviewedHash, AppUser user, CancellationToken token)
    {
        if ((result ?? "").Length > 1000) return new ToolOutcome(false, "ผลลัพธ์ยาวเกิน 1000 ตัวอักษร", "refused");
        var approval = await db.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, token);
        if (approval is null) return new ToolOutcome(false, "ไม่พบรายการนี้", "refused");
        if (approval.Agent == Scmos.Api.Ai.Operations.OperationsChangePolicy.Agent)
            return new(false, "ต้องยืนยันผ่าน Operations changes", "refused");

        var now = DateTimeOffset.UtcNow;
        if (ApprovalPolicy.ApplyProblem(user, approval, reviewedHash, now) is { } problem)
            return new ToolOutcome(false, problem switch
            {
                "approver" => "ทำได้เฉพาะระดับหัวหน้างานขึ้นไป",
                "expired" => "การอนุมัตินี้หมดอายุแล้ว — ให้เสนอและอนุมัติใหม่",
                "hash" => "ข้อมูลที่อ้างถึงไม่ตรงกับที่อนุมัติไว้ — โหลดคิวใหม่แล้วลองอีกครั้ง",
                _ => "ต้องอนุมัติก่อนจึงจะนำไปใช้ได้",
            }, "refused");

        var by = user.Signature;
        var trimmed = (result ?? "").Trim();
        var changed = await db.Approvals
            .Where(a => a.Id == id && a.State == ApprovalPolicy.Approved)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.State, ApprovalPolicy.Applied)
                .SetProperty(a => a.Result, trimmed)
                .SetProperty(a => a.AppliedBy, by)
                .SetProperty(a => a.AppliedAt, now)
                .SetProperty(a => a.ExpiresAt, (DateTimeOffset?)null), token);
        return changed == 0
            ? new ToolOutcome(false, "มีคนบันทึกรายการนี้ไปก่อนแล้ว", "refused")
            : new ToolOutcome(true, "บันทึกว่านำไปใช้แล้ว", "ran", null, id);
    }

    private static string RequestRefusal(string problem) => problem switch
    {
        "not-internal" => "เสนอได้เฉพาะบัญชีภายในที่รู้จัก",
        "no-capability" => "เสนอได้เฉพาะผู้ที่มีสิทธิ์ทำการเปลี่ยนแปลงนี้เอง",
        "summary" => "เรื่องยาวเกิน 500 ตัวอักษรหรือมีอักขระควบคุม",
        "too-many-fields" => $"ข้อมูลเกิน {ApprovalPolicy.MaxFields} รายการ",
        "too-large" => "ข้อมูลใหญ่เกิน 8 KB",
        _ when problem.StartsWith("reserved:", StringComparison.Ordinal) => $"ข้อมูลระบุ {problem[9..]} ไม่ได้ — ตัวตนและสิทธิ์มาจากระบบเท่านั้น",
        _ when problem.StartsWith("key:", StringComparison.Ordinal) => $"ชื่อข้อมูลไม่ถูกต้อง: {problem[4..]}",
        _ when problem.StartsWith("value:", StringComparison.Ordinal) => $"ค่าของ {problem[6..]} ยาวเกิน 500 ตัวอักษรหรือมีอักขระควบคุม",
        _ => "ข้อเสนอไม่ถูกต้อง",
    };
}
