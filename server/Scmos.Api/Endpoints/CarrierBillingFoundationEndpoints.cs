using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>Phase 1 configuration only; no invoice or billing UI lives here.</summary>
public static class CarrierBillingFoundationEndpoints
{
    public record CalendarInput(string? Kind, string? Name);
    public record SlaInput(string? StartDay, int? TargetWorkingDays, string? EffectiveTo, bool? Active);

    public static void MapCarrierBillingFoundation(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/carrier-billing/foundation")
            .WithTags("Carrier Billing Foundation");

        group.MapGet("/calendar", async (string? from, string? to, HttpContext context,
            IUserAccessor users, ScmosDbContext db, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            if (!IsoDate(from, out var first) || !IsoDate(to, out var last) || last < first)
                return ApiResults.Error("from และ to ต้องเป็นวันที่ yyyy-MM-dd ที่ถูกต้อง", StatusCodes.Status400BadRequest);
            if (last.DayNumber - first.DayNumber > 370)
                return ApiResults.Error("อ่านปฏิทินได้ครั้งละไม่เกิน 370 วัน", StatusCodes.Status400BadRequest);

            var rows = await db.BusinessCalendarDays.AsNoTracking()
                .Where(row => row.Date >= first && row.Date <= last)
                .OrderBy(row => row.Date).ToListAsync(token);
            return Results.Json(rows);
        });

        group.MapPut("/calendar/{date}", async (string date, [FromBody] CalendarInput? body,
            HttpContext context, IUserAccessor users, ScmosDbContext db, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("แก้ปฏิทินธุรกิจได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.AdministerData) is { } weak) return weak;
            if (!IsoDate(date, out var day))
                return ApiResults.Error("วันที่ต้องเป็น yyyy-MM-dd", StatusCodes.Status400BadRequest);

            var kind = (body?.Kind ?? "").Trim().ToLowerInvariant();
            if (!BusinessCalendarKind.All.Contains(kind, StringComparer.Ordinal))
                return ApiResults.Error("ประเภทวันไม่ถูกต้อง", StatusCodes.Status400BadRequest);

            var row = await db.BusinessCalendarDays.FirstOrDefaultAsync(one => one.Date == day, token);
            var before = row is null ? "" : $"{row.Kind}|{row.Name}";
            if (row is null)
            {
                row = new BusinessCalendarDay { Date = day };
                db.BusinessCalendarDays.Add(row);
            }
            row.Kind = kind;
            row.Name = (body?.Name ?? "").Trim();
            row.UpdatedBy = user.Signature;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            audit.Stage(user, AuditActions.Configure, "business-calendar", day.ToString("yyyy-MM-dd"),
                row.Name, "day", before, $"{row.Kind}|{row.Name}", "");
            await db.SaveChangesAsync(token);
            return Results.Json(new { message = "บันทึกปฏิทินธุรกิจแล้ว", row });
        });

        group.MapDelete("/calendar/{date}", async (string date, HttpContext context,
            IUserAccessor users, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("แก้ปฏิทินธุรกิจได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.AdministerData) is { } weak) return weak;
            if (!IsoDate(date, out var day))
                return ApiResults.Error("วันที่ต้องเป็น yyyy-MM-dd", StatusCodes.Status400BadRequest);

            var row = await db.BusinessCalendarDays.FirstOrDefaultAsync(one => one.Date == day, token);
            if (row is null) return ApiResults.Error("ไม่พบวันที่นี้ในปฏิทิน", StatusCodes.Status404NotFound);
            db.BusinessCalendarDays.Remove(row);
            audit.Stage(user, AuditActions.Configure, "business-calendar", day.ToString("yyyy-MM-dd"),
                row.Name, "day", $"{row.Kind}|{row.Name}", "", "ลบข้อยกเว้นปฏิทิน");
            await db.SaveChangesAsync(token);
            return Results.Json(new { message = "ลบข้อยกเว้นปฏิทินแล้ว" });
        });

        group.MapGet("/sla", async (HttpContext context, IUserAccessor users,
            ScmosDbContext db, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await db.BillingSlaRules.AsNoTracking()
                .OrderBy(row => row.Code).ThenByDescending(row => row.EffectiveFrom).ToListAsync(token));
        });

        group.MapPut("/sla/{code}/{effectiveFrom}", async (string code, string effectiveFrom,
            [FromBody] SlaInput? body, HttpContext context, IUserAccessor users,
            ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("แก้กฎ SLA ได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.AdministerData) is { } weak) return weak;
            if (!IsoDate(effectiveFrom, out var from))
                return ApiResults.Error("effectiveFrom ต้องเป็น yyyy-MM-dd", StatusCodes.Status400BadRequest);
            DateOnly? until = null;
            if (!string.IsNullOrWhiteSpace(body?.EffectiveTo))
            {
                if (!IsoDate(body.EffectiveTo, out var parsed))
                    return ApiResults.Error("effectiveTo ต้องเป็น yyyy-MM-dd", StatusCodes.Status400BadRequest);
                until = parsed;
            }

            var cleanCode = (code ?? "").Trim().ToUpperInvariant();
            var start = (body?.StartDay ?? "").Trim().ToLowerInvariant();
            var target = body?.TargetWorkingDays ?? 0;
            if (BillingSlaRules.Problem(cleanCode, start, target, from, until) is { } problem)
                return ApiResults.Error(problem, StatusCodes.Status400BadRequest);

            var row = await db.BillingSlaRules
                .FirstOrDefaultAsync(one => one.Code == cleanCode && one.EffectiveFrom == from, token);
            var before = row is null ? "" : $"{row.StartDay}|{row.TargetWorkingDays}|{row.EffectiveTo}|{row.Active}";
            if (row is null)
            {
                row = new BillingSlaRule { Code = cleanCode, EffectiveFrom = from };
                db.BillingSlaRules.Add(row);
            }
            row.StartDay = start;
            row.TargetWorkingDays = target;
            row.EffectiveTo = until;
            row.Active = body?.Active ?? true;
            row.UpdatedBy = user.Signature;
            row.UpdatedAt = DateTimeOffset.UtcNow;

            var overlaps = row.Active && await db.BillingSlaRules.AsNoTracking().AnyAsync(other =>
                other.Id != row.Id && other.Active && other.Code == cleanCode
                && other.EffectiveFrom <= (until ?? DateOnly.MaxValue)
                && (other.EffectiveTo == null || other.EffectiveTo >= from), token);
            if (overlaps)
                return ApiResults.Error("ช่วงวันที่ของกฎ SLA ซ้อนกับกฎที่ใช้งานอยู่", StatusCodes.Status409Conflict);

            audit.Stage(user, AuditActions.Configure, "billing-sla-rule", $"{cleanCode}:{from:yyyy-MM-dd}",
                cleanCode, "rule", before,
                $"{row.StartDay}|{row.TargetWorkingDays}|{row.EffectiveTo}|{row.Active}", "");
            await db.SaveChangesAsync(token);
            return Results.Json(new { message = "บันทึกกฎ SLA แล้ว", row });
        });
    }

    private static bool IsoDate(string? value, out DateOnly day) =>
        DateOnly.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
