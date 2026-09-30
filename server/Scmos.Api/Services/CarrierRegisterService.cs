using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Jobs">The carrier's own rows, cut to <see cref="CarrierRegisterService.Fields"/>.</param>
/// <param name="UpdatedAt">The register's stamp, to ask for changes from — the same stamp /api/jobs answers.</param>
public record CarrierRegister(IReadOnlyList<Dictionary<string, string>> Jobs, string UpdatedAt);

/// <param name="Count">How many rows the carrier holds now — the screen reloads when its own count differs.</param>
public record CarrierRegisterDelta(IReadOnlyList<Dictionary<string, string>> Jobs, int Count, bool Full, string UpdatedAt);

public record CarrierSaveResult(bool Ok, int Saved, string Message, int Status = StatusCodes.Status200OK);

/// <summary>
/// The register as the carrier's My job reads and writes it (30 Sep 2026): the
/// department's Operation Workspace, the same screen, over the carrier's own
/// jobs only (asked for: "เหมือนกันทุกประการ แต่ดึงงานเฉพาะของบริษัทตัวเอง").
///
/// <para>
/// Its reads answer in /api/jobs's own shapes, so the workspace loads, pages,
/// counts and syncs the same way: a row is the carrier's when its portal counts
/// it (the confirmed assignment, or the register's carrier where the job has no
/// assignment history), and every row is cut to <see cref="Fields"/> — the
/// operators' remarks, the CS person, the edit history and incident references
/// stay the department's.
/// </para>
///
/// <para>
/// Its write takes the workspace's own save (whole jobs) and keeps only what a
/// carrier may change (the department's decision, 30 Sep 2026): the truck,
/// driver and phone, the container and seal, the arrival date and time, and the
/// status along the carrier's own ladder (<see cref="CarrierService.AdvanceForAsync"/>).
/// Everything else on a job it sends is left as the register holds it. A job it
/// does not hold refuses the whole save; a closed job takes nothing; each value
/// is checked the way the Carrier API checks it, and every change is audited
/// with what it was before.
/// </para>
/// </summary>
public class CarrierRegisterService(ScmosDbContext db, CarrierService carriers, JobsRepository jobs, AuditService audit)
{
    /// <summary>What a carrier's grid shows: the job as the department's grid has it, less what is the department's own.</summary>
    public static readonly string[] Fields =
    [
        "key", "id", "cat", "op", "opId", "date", "customer", "trucker", "jobCode", "abs", "booking", "product", "fclLcl",
        "agent", "destination", "plant", "planTime", "type", "cyYard", "returnLoc", "emptyReturn", "weight", "container",
        "seal", "tare", "licence", "driver", "contact", "arrDate", "arrTime", "closingDate", "closingTime", "reason", "ot",
        "pickupPlan", "pickupTime", "freightType", "status", "origDate", "moveReason", "cancelReason", "wh", "jobNo", "sid",
        "dCode", "customerPo", "tmsId", "sapOrder", "deliverNo", "vtl", "province", "zip", "pallet", "kgs",
        "v4", "v6", "v10", "vtr", "cost", "diesel", "returnLoad", "returnFinished",
    ];

    /// <summary>The cells a carrier may change on its own jobs; the status is the other, along its ladder.</summary>
    public static readonly string[] Editable = ["licence", "driver", "contact", "container", "seal", "arrDate", "arrTime"];

    public async Task<CarrierRegister?> ReadAsync(AppUser user, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return null;
        var keys = await OwnKeysAsync(company, token);
        var (json, _) = await jobs.LoadAsync(token);
        using var document = JsonDocument.Parse(json);
        var stamp = document.RootElement.TryGetProperty("updatedAt", out var at) && at.ValueKind == JsonValueKind.String ? at.GetString() ?? "" : "";
        var rows = document.RootElement.TryGetProperty("jobs", out var all) && all.ValueKind == JsonValueKind.Array
            ? Cut(all, keys) : [];
        return new CarrierRegister(rows, stamp);
    }

    public async Task<CarrierRegisterDelta?> ChangedAsync(AppUser user, DateTimeOffset after, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return null;
        var keys = await OwnKeysAsync(company, token);
        var (json, _, updatedAt, full) = await jobs.ChangedSinceAsync(after, token);
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.ValueKind == JsonValueKind.Array ? Cut(document.RootElement, keys) : [];
        return new CarrierRegisterDelta(rows, keys.Count, full, updatedAt.ToUniversalTime().ToString("O"));
    }

    /// <summary>A carrier's grid save — see the class notes for what it keeps.</summary>
    public async Task<CarrierSaveResult> SaveAsync(AppUser user, IReadOnlyList<JsonElement> incoming, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return new(false, 0, "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา", StatusCodes.Status403Forbidden);
        var keys = await OwnKeysAsync(company, token);
        var sent = incoming.Where(job => job.ValueKind == JsonValueKind.Object)
            .GroupBy(job => Field(job, "key")).Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        if (sent.Keys.FirstOrDefault(key => !keys.Contains(key)) is { } foreign)
            return new(false, 0, $"งาน {foreign} ไม่ได้อยู่กับบริษัทนี้", StatusCodes.Status403Forbidden);

        var before = await CurrentAsync(sent.Keys.ToList(), token);
        var plans = new List<(string Key, Dictionary<string, string> Current, Dictionary<string, string> Writes, string? Advance)>();
        var problems = new List<string>();
        foreach (var (key, job) in sent)
        {
            if (!before.TryGetValue(key, out var current)) { problems.Add($"{key}: ไม่พบงานในทะเบียน"); continue; }
            var writes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in Editable)
            {
                if (!job.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) continue;
                var next = Formats.Clean(value.GetString());
                if (next == Formats.Clean(current.GetValueOrDefault(field, ""))) continue;
                if (Problem(field, next) is { } problem) { problems.Add($"{Label(current, key)}: {problem}"); continue; }
                writes[field] = next;
            }

            string? advance = null;
            var status = job.TryGetProperty("status", out var said) && said.ValueKind == JsonValueKind.String ? Formats.Clean(said.GetString()) : "";
            var was = Formats.Clean(current.GetValueOrDefault("status", ""));
            if (status.Length > 0 && !string.Equals(status, was, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Canonical(status), Canonical(was), StringComparison.OrdinalIgnoreCase))
            {
                advance = StepFor(Canonical(status));
                if (advance is null) problems.Add($"{Label(current, key)}: ผู้ขนส่งเปลี่ยนสถานะได้เฉพาะขั้นขนส่ง (รถออก รับตู้ ขนถ่าย ระหว่างทาง ส่งถึง คืนตู้ ส่งงานเสร็จ)");
            }

            if ((writes.Count > 0 || advance is not null) && JobStatus.IsClosedOut(Canonical(was)))
            {
                problems.Add($"{Label(current, key)}: งานปิดแล้ว ({was}) แก้ไม่ได้");
                continue;
            }
            plans.Add((key, current, writes, advance));
        }
        if (problems.Count > 0) return new(false, 0, string.Join(" · ", problems.Take(5)), StatusCodes.Status400BadRequest);

        foreach (var (key, current, writes, advance) in plans)
        {
            if (writes.Count > 0)
            {
                if (!await jobs.PatchAsync(key, writes, user.Signature, token))
                    return new(false, 0, $"บันทึก {Label(current, key)} ไม่สำเร็จ", StatusCodes.Status503ServiceUnavailable);
                foreach (var (field, value) in writes)
                    await audit.RecordAsync(user, AuditActions.For(field)?.Action ?? AuditActions.Update, "job", key, Label(current, key),
                        AuditActions.For(field)?.Label ?? field, current.GetValueOrDefault(field, ""), value, "ผู้ขนส่งแก้ในตาราง My job", token);
            }
            if (advance is not null)
            {
                var result = await carriers.AdvanceForAsync(user, company, key, advance, null, "", token);
                if (!result.Ok && !result.Replayed)
                    return new(false, 0, $"{Label(current, key)}: {result.Message}", StatusCodes.Status409Conflict);
            }
        }
        return new(true, sent.Count, "บันทึกแล้ว");
    }

    /// <summary>What is wrong with a value for a cell a carrier may change, or null. Clearing a cell is allowed.</summary>
    public static string? Problem(string field, string value)
    {
        if (value.Length == 0) return null;
        if (value.Length > 80 || value.Any(char.IsControl)) return "ข้อความยาวเกินไปหรือมีอักขระที่ใช้ไม่ได้";
        return field switch
        {
            "licence" when !Formats.IsPlate(value) => $"ทะเบียนรถ {value} ไม่ถูกรูปแบบ",
            "contact" when !Formats.IsPhone(value) => $"เบอร์โทร {value} ไม่ถูกรูปแบบ",
            "arrDate" when !Formats.IsDate(value) => "วันที่ถึงต้องเป็น dd/mm/yyyy",
            "arrTime" when Formats.TimeMinutes(value) is null => "เวลาถึงต้องเป็น HH:mm",
            _ => null,
        };
    }

    /// <summary>A status as the ladder names it: the code itself, or what a legacy label means.</summary>
    private static string Canonical(string status) => JobStatus.IsControlled(status) ? status.ToUpperInvariant() : JobStatus.FromLegacy(status);

    /// <summary>The carrier's own status step for a ladder status, or null for one a carrier does not set.</summary>
    private static string? StepFor(string status) => status.ToUpperInvariant() switch
    {
        JobStatus.Dispatched => CarrierOperations.Dispatched,
        JobStatus.PickedUp => CarrierOperations.PickedUp,
        JobStatus.Loading => CarrierOperations.Loading,
        JobStatus.InTransit => CarrierOperations.InTransit,
        JobStatus.Delivered => CarrierOperations.Delivered,
        JobStatus.ContainerReturned => CarrierOperations.ContainerReturned,
        JobStatus.Completed => CarrierOperations.DeliveryComplete,
        _ => null,
    };

    /// <summary>
    /// The jobs as the register holds them now, every text field — the audit snapshot carries only the audited
    /// ones, and without arrTime every save would read the arrival as changed and write it again.
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, string>>> CurrentAsync(IReadOnlyList<string> keys, CancellationToken token)
    {
        var rows = await db.OperationJobs.AsNoTracking().Where(job => keys.Contains(job.Key))
            .Select(job => new { job.Key, job.Data }).ToListAsync(token);
        var current = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var document = JsonDocument.Parse(row.Data);
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Value.ValueKind == JsonValueKind.String) fields[property.Name] = property.Value.GetString() ?? "";
            }
            catch (JsonException) { /* an unreadable row reads as empty cells; the save still checks each value */ }
            current[row.Key] = fields;
        }
        return current;
    }

    private async Task<HashSet<string>> OwnKeysAsync(Supplier company, CancellationToken token) =>
        (await carriers.ReadForAsync(company, token)).Accepted.Select(job => job.Key).ToHashSet(StringComparer.Ordinal);

    private static List<Dictionary<string, string>> Cut(JsonElement rows, IReadOnlySet<string> keys)
    {
        var cut = new List<Dictionary<string, string>>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !keys.Contains(Field(row, "key"))) continue;
            var job = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in Fields)
                if (row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) job[name] = value.GetString() ?? "";
            // The screen keys a row by its id; a row written with a key alone would be a row nobody can edit.
            if (job.GetValueOrDefault("id", "").Length == 0) job["id"] = job.GetValueOrDefault("key", "");
            cut.Add(job);
        }
        return cut;
    }

    private static string Field(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Label(IReadOnlyDictionary<string, string> job, string key) =>
        job.GetValueOrDefault("jobCode", "") is { Length: > 0 } code ? code : key;
}
