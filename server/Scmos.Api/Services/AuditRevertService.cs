using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Reverts audit rows — see <see cref="AuditRevert"/> for the rules. Each
/// row is one cell on one job put back to the row's old value through the
/// register's own write path, with a new audit row that names the one it
/// reversed. Rows the rules refuse, or whose cell has moved on since, are
/// reported back one by one; nothing is half-done in silence.
/// </summary>
public class AuditRevertService(ScmosDbContext db, JobsRepository jobs, AuditService audit)
{
    /// <summary>What happened to one requested row.</summary>
    public record Line(long Id, string JobKey, string Label, string Field, string From, string To, string Outcome, string Detail = "");

    public record Outcome(bool Ok, string Message, int Reverted, IReadOnlyList<Line> Lines);

    public async Task<Outcome> RevertAsync(AppUser actor, IReadOnlyList<long> ids, string reason, CancellationToken token)
    {
        var wanted = ids.Where(id => id > 0).Distinct().ToList();
        if (wanted.Count == 0) return new(false, "เลือกรายการที่จะย้อนกลับก่อน", 0, []);
        if (wanted.Count > AuditRevert.MaxRows) return new(false, $"ย้อนกลับได้ครั้งละไม่เกิน {AuditRevert.MaxRows} รายการ", 0, []);
        if (Formats.Clean(reason).Length < AuditRevert.MinReason)
            return new(false, $"ใส่เหตุผลอย่างน้อย {AuditRevert.MinReason} ตัวอักษร เช่น มอบหมายผิดคน", 0, []);

        var rows = await db.AuditEvents.AsNoTracking()
            .Where(row => wanted.Contains(row.Id))
            .ToListAsync(token);
        var found = rows.ToDictionary(row => row.Id);

        // A person's name in an audit row is what the register holds; the id
        // the columns need beside it comes from the directory.
        var staff = await db.Staff.AsNoTracking()
            .Select(person => new { person.Id, person.Name })
            .ToListAsync(token);
        var idOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var person in staff) idOf.TryAdd(person.Name.Trim(), person.Id);

        var lines = new List<Line>();
        var reverted = 0;
        foreach (var id in AuditRevert.InOrder(wanted, id => id))
        {
            if (!found.TryGetValue(id, out var row))
            {
                lines.Add(new(id, "", "", "", "", "", "missing", "ไม่พบรายการนี้ในประวัติ"));
                continue;
            }
            var field = AuditActions.FieldOf(row.Field);
            var problem = AuditRevert.Problem(row.Entity, row.Action, row.Field);
            if (problem is not null || field is null)
            {
                lines.Add(new(id, row.EntityId, row.EntityLabel, row.Field, row.NewValue, row.OldValue, "not-reversible",
                    problem switch
                    {
                        "not-a-job" => "ย้อนกลับได้เฉพาะรายการของงาน",
                        "not-a-cell-change" => "รายการนี้ไม่ใช่การเปลี่ยนค่าในช่อง",
                        _ => "ช่องนี้ย้อนกลับจากประวัติไม่ได้",
                    }));
                continue;
            }
            if (!actor.Can(AuditRevert.Required(field)))
            {
                lines.Add(new(id, row.EntityId, row.EntityLabel, row.Field, row.NewValue, row.OldValue, "forbidden",
                    field == "op" ? "การมอบหมายงานย้อนกลับได้เฉพาะระดับหัวหน้างานขึ้นไป" : "แก้งานของผู้อื่นได้เฉพาะระดับหัวหน้างานขึ้นไป"));
                continue;
            }

            var job = await db.OperationJobs.AsNoTracking().FirstOrDefaultAsync(one => one.Key == row.EntityId, token);
            if (job is null)
            {
                lines.Add(new(id, row.EntityId, row.EntityLabel, row.Field, row.NewValue, row.OldValue, "missing", "ไม่พบงานนี้ในทะเบียนแล้ว"));
                continue;
            }
            var current = CellOf(job, field);
            if (!AuditRevert.StillHolds(field, current, row.NewValue))
            {
                lines.Add(new(id, row.EntityId, row.EntityLabel, row.Field, row.NewValue, row.OldValue, "changed-since",
                    $"ช่องนี้ถูกแก้ต่อแล้ว ตอนนี้เป็น “{(current.Length > 0 ? current : "ว่าง")}” ไม่ใช่ “{row.NewValue}”"));
                continue;
            }

            var fields = new Dictionary<string, string> { [field] = row.OldValue };
            if (field == "op")
            {
                // The name and the id travel together, or the column says one
                // person and the row another.
                var name = row.OldValue.Trim();
                if (name.Length > 0 && !idOf.TryGetValue(name, out var ownerId))
                {
                    lines.Add(new(id, row.EntityId, row.EntityLabel, row.Field, row.NewValue, row.OldValue, "unknown-person",
                        $"ไม่พบ “{name}” ในทะเบียนพนักงาน"));
                    continue;
                }
                fields["opId"] = name.Length > 0 ? idOf[name] : "";
            }

            var written = await jobs.PatchAsync(job.Key, fields, actor.Signature, token);
            if (!written)
            {
                lines.Add(new(id, row.EntityId, row.EntityLabel, row.Field, row.NewValue, row.OldValue, "failed", "บันทึกไม่สำเร็จ"));
                continue;
            }
            await audit.RecordAsync(actor, row.Action, AuditRevert.Entity, job.Key, row.EntityLabel,
                row.Field, row.NewValue, row.OldValue, AuditRevert.ReasonFor(id, reason), token, source: "revert");
            reverted++;
            lines.Add(new(id, row.EntityId, row.EntityLabel, row.Field, row.NewValue, row.OldValue, "reverted"));
        }

        var skipped = lines.Count - reverted;
        var message = reverted == 0
            ? "ไม่มีรายการใดถูกย้อนกลับ"
            : skipped == 0 ? $"ย้อนกลับ {reverted} รายการแล้ว" : $"ย้อนกลับ {reverted} รายการแล้ว · ข้าม {skipped} รายการ";
        return new(reverted > 0 || skipped == 0, message, reverted, lines);
    }

    /// <summary>The cell as the register holds it now — the JSON's word, since that is what the grid reads.</summary>
    private static string CellOf(OperationJob job, string field)
    {
        try
        {
            using var document = JsonDocument.Parse(job.Data);
            return document.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return field switch
            {
                "op" => job.Owner, "trucker" => job.Trucker, "status" => job.Status,
                "date" => job.WorkDate, "container" => job.Container, _ => "",
            };
        }
    }
}
