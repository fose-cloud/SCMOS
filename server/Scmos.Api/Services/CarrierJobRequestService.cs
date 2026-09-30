using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Ai.Booking;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record CarrierJobRequestView(long Id, int SupplierId, string SupplierName, string Category,
    IReadOnlyDictionary<string, string> Fields, string Note, string Status, string CreatedBy, DateTimeOffset CreatedAt,
    string DecidedBy, DateTimeOffset? DecidedAt, string DecisionNote, string JobKey, int Revision);

/// <summary>The add-job form's layouts a carrier keys into: each category's fields, and the ones it cannot go without.</summary>
public record CarrierJobRequestForm(IReadOnlyDictionary<string, string[]> Fields, IReadOnlyDictionary<string, string[]> Essential);

public record CarrierJobRequestResult(bool Ok, string Code, string Message, long? Id = null);

/// <summary>
/// Jobs a carrier keys in itself, for Leschaco to confirm (29 Sep 2026 — "ทั้งงานที่เสนอมาและผู้ขนส่งสร้างเองได้").
///
/// The carrier's side is its own company only: the supplier comes from the
/// account, never the request. The department's side lists what waits, opens a
/// request as the ordinary add-job form, and marks it approved with the job the
/// form saved — or refuses it with a reason. The fields are the add-job form's
/// own (<see cref="BookingVerification.Fields"/>), so a request opens into the
/// form without translation, and the form's save is the only way into the register.
/// </summary>
public class CarrierJobRequestService(ScmosDbContext db, CarrierService carriers, AuditService audit, TimeProvider clock)
{
    /// <summary>How many requests one carrier may have waiting at once.</summary>
    public const int MaxPending = 50;
    public const int MaxValue = 120;
    public const int MaxNote = 500;

    public static readonly CarrierJobRequestForm Form = new(BookingVerification.Fields, BookingVerification.Essential);

    private static readonly HashSet<string> DateFields = ["date", "closingDate"];
    private static readonly HashSet<string> TimeFields = ["planTime", "closingTime"];

    /// <summary>Who in the department may see and settle requests: anyone who may add a job.</summary>
    public static bool CanReview(AppUser? user) => user is not null && !CarrierTenantContext.IsCarrier(user)
        && user.Can(Capability.EditOwnJobs);

    /// <summary>What is wrong with a request as keyed, or null — the category, its essential fields, dates, times, lengths.</summary>
    public static string? Problem(string? category, IReadOnlyDictionary<string, string>? fields, string? note)
    {
        var wanted = (category ?? "").Trim().ToUpperInvariant();
        if (!Form.Fields.TryGetValue(wanted, out var allowed)) return "เลือกประเภทงาน IMPORT, EXPORT หรือ DELIVERY";
        fields ??= new Dictionary<string, string>();
        foreach (var (name, raw) in fields)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal)) return $"ช่อง {name} ไม่ใช่ช่องของงาน {wanted}";
            var value = (raw ?? "").Trim();
            if (value.Length > MaxValue || value.Any(char.IsControl)) return $"ช่อง {name} ยาวเกินไปหรือมีอักขระที่ใช้ไม่ได้";
            if (value.Length == 0) continue;
            if (DateFields.Contains(name) && Formats.ParseDay(value) is null) return $"ช่อง {name} ต้องเป็นวันที่แบบ dd/mm/yyyy";
            if (TimeFields.Contains(name) && Formats.TimeMinutes(value) is null) return $"ช่อง {name} ต้องเป็นเวลาแบบ HH:mm";
        }
        var missing = Form.Essential[wanted].Where(name => !fields.TryGetValue(name, out var v) || string.IsNullOrWhiteSpace(v)).ToList();
        if (missing.Count > 0) return "ต้องกรอก: " + string.Join(", ", missing);
        var why = (note ?? "").Trim();
        if (why.Length > MaxNote || why.Any(ch => char.IsControl(ch) && ch is not '\n' and not '\r')) return "หมายเหตุยาวเกินไป";
        return null;
    }

    public async Task<CarrierJobRequestResult> CreateAsync(AppUser user, string? category, IReadOnlyDictionary<string, string>? fields,
        string? note, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return new(false, "no_company", "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา");
        if (Problem(category, fields, note) is { } problem) return new(false, "invalid", problem);
        var waiting = await db.CarrierJobRequests.CountAsync(row => row.SupplierId == company.Id && row.Status == CarrierJobRequest.Pending, token);
        if (waiting >= MaxPending) return new(false, "too_many", $"มีงานที่แจ้งไว้รอยืนยันครบ {MaxPending} รายการแล้ว");

        var kept = fields!.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.Ordinal);
        var row = new CarrierJobRequest
        {
            SupplierId = company.Id, SupplierName = company.Name, Category = category!.Trim().ToUpperInvariant(),
            Fields = JsonSerializer.Serialize(kept), Note = (note ?? "").Trim(), Status = CarrierJobRequest.Pending,
            CreatedBy = user.Signature, CreatedAt = clock.GetUtcNow(),
        };
        db.CarrierJobRequests.Add(row);
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Update, "carrier-job-request", row.Id.ToString(), company.Name,
            "status", "", CarrierJobRequest.Pending, "ผู้ขนส่งแจ้งงานใหม่", token);
        return new(true, "ok", "ส่งงานให้ Leschaco ยืนยันแล้ว", row.Id);
    }

    /// <summary>The carrier's own requests, newest first.</summary>
    public async Task<IReadOnlyList<CarrierJobRequestView>?> MineAsync(AppUser user, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return null;
        var rows = await db.CarrierJobRequests.AsNoTracking().Where(row => row.SupplierId == company.Id)
            .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).Take(200).ToListAsync(token);
        return rows.Select(View).ToList();
    }

    public async Task<CarrierJobRequestResult> WithdrawAsync(AppUser user, long id, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return new(false, "no_company", "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา");
        var row = await db.CarrierJobRequests.FirstOrDefaultAsync(one => one.Id == id && one.SupplierId == company.Id, token);
        if (row is null) return new(false, "not_found", "ไม่พบงานที่แจ้งไว้");
        if (row.Status != CarrierJobRequest.Pending) return new(false, "closed", "งานนี้ Leschaco ตอบไปแล้ว");
        return await SettleAsync(user, row, CarrierJobRequest.Withdrawn, "", "ผู้ขนส่งถอนงานที่แจ้ง", row.Revision, token);
    }

    /// <summary>What waits for the department, oldest first — or every status, newest first, when asked.</summary>
    public async Task<IReadOnlyList<CarrierJobRequestView>> ListAsync(string? status, CancellationToken token)
    {
        var wanted = (status ?? CarrierJobRequest.Pending).Trim().ToUpperInvariant();
        var query = db.CarrierJobRequests.AsNoTracking();
        if (wanted != "ALL") query = query.Where(row => row.Status == wanted);
        var rows = wanted == CarrierJobRequest.Pending
            ? await query.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Take(200).ToListAsync(token)
            : await query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).Take(200).ToListAsync(token);
        return rows.Select(View).ToList();
    }

    public Task<int> PendingCountAsync(CancellationToken token) =>
        db.CarrierJobRequests.CountAsync(row => row.Status == CarrierJobRequest.Pending, token);

    /// <summary>
    /// A request settled as the job the add-job form saved. The job has to be in the register already —
    /// a request never names a job whose save failed — and the request has to be the one that was read.
    /// </summary>
    public async Task<CarrierJobRequestResult> ApproveAsync(AppUser user, long id, string? jobKey, int revision, CancellationToken token)
    {
        if (!CanReview(user)) return new(false, "forbidden", "ต้องเป็นผู้ที่เพิ่มงานได้");
        var key = (jobKey ?? "").Trim();
        if (key.Length is 0 or > 100) return new(false, "invalid", "ระบุงานที่สร้างจากคำขอนี้");
        if (!await db.OperationJobs.AnyAsync(job => job.Key == key, token)) return new(false, "no_job", "ยังไม่พบงานนี้ในทะเบียน — บันทึกงานก่อน");
        var row = await db.CarrierJobRequests.FirstOrDefaultAsync(one => one.Id == id, token);
        if (row is null) return new(false, "not_found", "ไม่พบคำขอนี้");
        if (row.Status != CarrierJobRequest.Pending) return new(false, "closed", "คำขอนี้ถูกตอบไปแล้ว");
        row.JobKey = key;
        return await SettleAsync(user, row, CarrierJobRequest.Approved, "", "ยืนยันงานที่ผู้ขนส่งแจ้ง → " + key, revision, token);
    }

    public async Task<CarrierJobRequestResult> RejectAsync(AppUser user, long id, string? reason, int revision, CancellationToken token)
    {
        if (!CanReview(user)) return new(false, "forbidden", "ต้องเป็นผู้ที่เพิ่มงานได้");
        var why = (reason ?? "").Trim();
        if (why.Length is 0 or > MaxNote || why.Any(char.IsControl)) return new(false, "reason_required", "ต้องระบุเหตุผลที่ไม่รับ");
        var row = await db.CarrierJobRequests.FirstOrDefaultAsync(one => one.Id == id, token);
        if (row is null) return new(false, "not_found", "ไม่พบคำขอนี้");
        if (row.Status != CarrierJobRequest.Pending) return new(false, "closed", "คำขอนี้ถูกตอบไปแล้ว");
        return await SettleAsync(user, row, CarrierJobRequest.Rejected, why, why, revision, token);
    }

    private async Task<CarrierJobRequestResult> SettleAsync(AppUser user, CarrierJobRequest row, string status, string note,
        string reason, int revision, CancellationToken token)
    {
        if (row.Revision != revision) return new(false, "conflict", "มีผู้ตอบคำขอนี้ไปแล้ว — รีเฟรชแล้วลองใหม่");
        var before = row.Status;
        row.Status = status;
        row.DecisionNote = note;
        row.DecidedBy = user.Signature;
        row.DecidedAt = clock.GetUtcNow();
        row.Revision = checked(row.Revision + 1);
        audit.Stage(user, status == CarrierJobRequest.Rejected ? AuditActions.Reject : status == CarrierJobRequest.Approved ? AuditActions.Approve : AuditActions.Update,
            "carrier-job-request", row.Id.ToString(), row.SupplierName, "status", before, status, reason);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { return new(false, "conflict", "มีผู้ตอบคำขอนี้ไปแล้ว — รีเฟรชแล้วลองใหม่"); }
        return new(true, "ok", status switch
        {
            CarrierJobRequest.Approved => "ยืนยันงานแล้ว",
            CarrierJobRequest.Rejected => "แจ้งผู้ขนส่งว่าไม่รับแล้ว",
            _ => "ถอนงานที่แจ้งแล้ว",
        }, row.Id);
    }

    public static CarrierJobRequestView View(CarrierJobRequest row)
    {
        Dictionary<string, string> fields;
        try { fields = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Fields) ?? []; }
        catch (JsonException) { fields = []; }
        return new(row.Id, row.SupplierId, row.SupplierName, row.Category, fields, row.Note, row.Status, row.CreatedBy,
            row.CreatedAt, row.DecidedBy, row.DecidedAt, row.DecisionNote, row.JobKey, row.Revision);
    }
}
