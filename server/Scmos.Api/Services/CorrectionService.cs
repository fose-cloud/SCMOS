using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>One proposal as the workspace reads it: what would change on which job, and why.</summary>
public sealed record CorrectionView(long Id, string JobKey, string JobCode, string Field, string Label, string From, string To, string Reason, DateTimeOffset ProposedAt);

/// <summary>What became of one decision — for the toast, and for the count a bulk approval reports.</summary>
public sealed record CorrectionOutcome(bool Ok, int Status, string Message, int Applied = 0, int Stale = 0, int Refused = 0);

/// <summary>
/// The owner's side of a proposed correction: see it on the job, approve it,
/// or say no — the rule LINE messages follow since 16 Sep 2026, applied to
/// the cells the rules propose (22 Sep 2026).
///
/// <para>
/// Approving writes the one cell, through the same patch a LINE approval
/// uses, with an audit row whose source says the AI proposed it and the
/// owner approved it. It refuses when the cell no longer says what the
/// proposal was made against — somebody edited it since — and marks the
/// proposal stale rather than writing over their edit. Rejecting writes
/// nothing and keeps the row as the record that the owner looked and said
/// no, so the next run does not ask again about the same spelling.
/// </para>
/// </summary>
public sealed class CorrectionService(ScmosDbContext db, JobsRepository jobs, DelegationService delegations, AuditService audit)
{
    public const string Source = "AI";
    /// <summary>The most proposals the workspace is told about at once — a mark per row is all it needs.</summary>
    public const int PendingLimit = 5000;

    /// <summary>Every open proposal, newest first, for the workspace to mark rows and the drawer to list.</summary>
    public async Task<IReadOnlyList<CorrectionView>> PendingAsync(CancellationToken token)
    {
        var rows = await db.JobCorrections.AsNoTracking()
            .Where(row => row.State == CorrectionState.Pending)
            .OrderByDescending(row => row.ProposedAt).ThenBy(row => row.Id)
            .Take(PendingLimit)
            .ToListAsync(token);
        return rows.Select(View).ToList();
    }

    /// <summary>How many wait on jobs this person owns or covers — the number beside the button that approves them all.</summary>
    public async Task<int> MineAsync(AppUser user, CancellationToken token)
    {
        var owners = await OwnersAsync(user, token);
        if (owners.Count == 0) return 0;
        var keys = await db.OperationJobs.AsNoTracking().Where(job => owners.Contains(job.OwnerId)).Select(job => job.Key).ToListAsync(token);
        return await db.JobCorrections.AsNoTracking().CountAsync(row => row.State == CorrectionState.Pending && keys.Contains(row.JobKey), token);
    }

    public async Task<CorrectionOutcome> ApplyAsync(long id, AppUser user, CancellationToken token)
    {
        var row = await db.JobCorrections.FirstOrDefaultAsync(one => one.Id == id, token);
        if (row is null) return new(false, StatusCodes.Status404NotFound, "ไม่พบข้อเสนอนี้");
        if (row.State != CorrectionState.Pending) return new(false, StatusCodes.Status409Conflict, $"ข้อเสนอนี้ถูกจัดการไปแล้ว ({row.State})");
        if (!await MayActOnAsync(user, row.JobKey, token))
            return new(false, StatusCodes.Status403Forbidden, "อนุมัติได้เฉพาะงานของตัวเอง หรืองานที่ดูแลแทนอยู่");
        var outcome = await WriteAsync(row, user, token);
        await db.SaveChangesAsync(token);
        return outcome;
    }

    public async Task<CorrectionOutcome> RejectAsync(long id, AppUser user, string? note, CancellationToken token)
    {
        var row = await db.JobCorrections.FirstOrDefaultAsync(one => one.Id == id, token);
        if (row is null) return new(false, StatusCodes.Status404NotFound, "ไม่พบข้อเสนอนี้");
        if (row.State != CorrectionState.Pending) return new(false, StatusCodes.Status409Conflict, $"ข้อเสนอนี้ถูกจัดการไปแล้ว ({row.State})");
        if (!await MayActOnAsync(user, row.JobKey, token))
            return new(false, StatusCodes.Status403Forbidden, "ปฏิเสธได้เฉพาะงานของตัวเอง หรืองานที่ดูแลแทนอยู่");
        row.State = CorrectionState.Rejected;
        row.DecidedBy = user.Signature;
        row.DecidedAt = DateTimeOffset.UtcNow;
        row.Note = Formats.Clean(note).Length > 300 ? Formats.Clean(note)[..300] : Formats.Clean(note);
        await db.SaveChangesAsync(token);
        return new(true, StatusCodes.Status200OK, $"ไม่แก้ {CorrectionRules.Labels.GetValueOrDefault(row.Field, row.Field)} ของงาน {Name(row)} — คงค่าเดิม \"{row.FromValue}\"");
    }

    /// <summary>
    /// Every open proposal on one job, approved together — the drawer's "ทั้งหมดของงานนี้".
    /// Each cell is checked and audited on its own; one stale cell does not stop the rest.
    /// </summary>
    public async Task<CorrectionOutcome> ApplyJobAsync(string jobKey, AppUser user, CancellationToken token)
    {
        var key = (jobKey ?? "").Trim();
        if (key.Length == 0) return new(false, StatusCodes.Status400BadRequest, "ต้องระบุงาน");
        if (!await MayActOnAsync(user, key, token))
            return new(false, StatusCodes.Status403Forbidden, "อนุมัติได้เฉพาะงานของตัวเอง หรืองานที่ดูแลแทนอยู่");
        var rows = await db.JobCorrections.Where(row => row.State == CorrectionState.Pending && row.JobKey == key).OrderBy(row => row.Id).ToListAsync(token);
        return await WriteAllAsync(rows, user, token);
    }

    /// <summary>
    /// Every open proposal on the jobs this person owns (or covers), approved
    /// together — the toolbar's "อนุมัติทั้งหมดของฉัน". Only their own: a
    /// supervisor who may edit any job still approves other people's jobs
    /// one job at a time, because the department's rule is that the job's
    /// owner is the one who says yes.
    /// </summary>
    public async Task<CorrectionOutcome> ApplyMineAsync(AppUser user, CancellationToken token)
    {
        var owners = await OwnersAsync(user, token);
        if (owners.Count == 0) return new(false, StatusCodes.Status403Forbidden, "บัญชีนี้ไม่มีงานของตัวเอง");
        var keys = await db.OperationJobs.AsNoTracking().Where(job => owners.Contains(job.OwnerId)).Select(job => job.Key).ToListAsync(token);
        var rows = await db.JobCorrections.Where(row => row.State == CorrectionState.Pending && keys.Contains(row.JobKey)).OrderBy(row => row.JobKey).ThenBy(row => row.Id).ToListAsync(token);
        return await WriteAllAsync(rows, user, token);
    }

    private async Task<CorrectionOutcome> WriteAllAsync(List<JobCorrection> rows, AppUser user, CancellationToken token)
    {
        if (rows.Count == 0) return new(true, StatusCodes.Status200OK, "ไม่มีข้อเสนอที่รออยู่");
        int applied = 0, stale = 0, refused = 0;
        foreach (var row in rows)
        {
            var one = await WriteAsync(row, user, token);
            if (one.Ok) applied++; else if (row.State == CorrectionState.Stale) stale++; else refused++;
        }
        await db.SaveChangesAsync(token);
        var message = $"อนุมัติแล้ว {applied} รายการ"
            + (stale > 0 ? $" · {stale} รายการค่าเปลี่ยนไปแล้ว ไม่ได้เขียนทับ" : "")
            + (refused > 0 ? $" · {refused} รายการบันทึกไม่สำเร็จ" : "");
        return new(applied > 0 || (stale == 0 && refused == 0), StatusCodes.Status200OK, message, applied, stale, refused);
    }

    /// <summary>
    /// Writes one approved cell, or marks the proposal stale when the cell —
    /// or the job — is no longer what it was proposed against. The row is
    /// changed in memory; the caller saves.
    /// </summary>
    private async Task<CorrectionOutcome> WriteAsync(JobCorrection row, AppUser user, CancellationToken token)
    {
        var job = await db.OperationJobs.AsNoTracking().Where(one => one.Key == row.JobKey).Select(one => new { one.Key, one.JobCode, one.Data }).FirstOrDefaultAsync(token);
        var current = job is null ? null : CellOf(job.Data, row.Field);
        if (job is null || current != row.FromValue)
        {
            row.State = CorrectionState.Stale;
            row.DecidedBy = user.Signature;
            row.DecidedAt = DateTimeOffset.UtcNow;
            row.Note = job is null ? "ไม่พบงานนี้แล้ว" : $"ค่าปัจจุบันคือ \"{current}\" ไม่ใช่ \"{row.FromValue}\" ที่เสนอไว้";
            return new(false, StatusCodes.Status409Conflict, job is null ? "ไม่พบงานนี้แล้ว" : $"ช่องนี้ถูกแก้ไปแล้วเป็น \"{current}\" — ไม่เขียนทับ");
        }
        var wrote = await jobs.PatchAsync(row.JobKey, new Dictionary<string, string> { [row.Field] = row.ToValue }, user.Signature, token);
        if (!wrote) return new(false, StatusCodes.Status409Conflict, "บันทึกไม่สำเร็จ");
        // Six months from now "who changed this carrier's spelling" answers with the
        // owner who approved it and the fact that a proposal is why.
        await audit.RecordAsync(user, ActionOf(row.Field), "job", row.JobKey, job.JobCode, row.Field, row.FromValue, row.ToValue,
            $"อนุมัติข้อเสนอแก้ไข #{row.Id}: {row.Reason}", token, Source);
        row.State = CorrectionState.Applied;
        row.DecidedBy = user.Signature;
        row.DecidedAt = DateTimeOffset.UtcNow;
        return new(true, StatusCodes.Status200OK, $"แก้ {CorrectionRules.Labels.GetValueOrDefault(row.Field, row.Field)} ของงาน {Name(row)}: \"{row.FromValue}\" → \"{row.ToValue}\"", 1);
    }

    /// <summary>The LINE rule: the job's owner, somebody covering for them, or an account that may edit any job.</summary>
    private async Task<bool> MayActOnAsync(AppUser user, string jobKey, CancellationToken token)
    {
        if (user.Can(Capability.EditAnyJob)) return true;
        if (!user.Can(Capability.EditOwnJobs) || jobKey.Length == 0 || string.IsNullOrWhiteSpace(user.OperatorId)) return false;
        var acting = await delegations.ActingForAsync(user.OperatorId, token);
        var others = await jobs.OthersJobsAsync([jobKey], user.OperatorId, token, acting);
        return others.Count == 0;
    }

    /// <summary>The owner ids whose jobs count as this person's own: theirs, and those they are covering.</summary>
    private async Task<List<string>> OwnersAsync(AppUser user, CancellationToken token)
    {
        if (!user.Can(Capability.EditOwnJobs) || string.IsNullOrWhiteSpace(user.OperatorId)) return [];
        var owners = new List<string> { user.OperatorId };
        owners.AddRange(await delegations.ActingForAsync(user.OperatorId, token));
        return owners.Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static string ActionOf(string field) => field switch
    {
        "status" => AuditActions.StatusChange,
        "trucker" => AuditActions.CarrierChange,
        _ => AuditActions.Update,
    };

    private static string? CellOf(string data, string field)
    {
        try
        {
            using var json = JsonDocument.Parse(data);
            return json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }
        catch (JsonException) { return null; }
    }

    private static string Name(JobCorrection row) => row.JobCode.Length > 0 ? row.JobCode : row.JobKey;

    private static CorrectionView View(JobCorrection row) =>
        new(row.Id, row.JobKey, row.JobCode, row.Field, CorrectionRules.Labels.GetValueOrDefault(row.Field, row.Field), row.FromValue, row.ToValue, row.Reason, row.ProposedAt);
}
