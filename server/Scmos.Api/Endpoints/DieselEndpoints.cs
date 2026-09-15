using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The published diesel prices, which decide what band a month's work is billed
/// at.
///
/// <para>
/// Reading needs <see cref="Capability.ViewRates"/> — the figure sits on the
/// Domestic grid and behind every quotation, so anyone who may see a rate may
/// see what chose it. Replacing the whole table needs <see cref="Capability.EditRates"/>,
/// because a diesel figure <i>is</i> a rate: move it across a band and every
/// lane on the card moves a step with it. Keying one day's published price
/// needs <see cref="Capability.RecordDiesel"/>, which the operators hold — see
/// the note on that flag.
/// </para>
/// </summary>
public static partial class DieselEndpoints
{
    public record DieselInput(string EffectiveDate, decimal Price, string? Source);
    public record DayInput(decimal Price, string? Source);

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4}$")]
    private static partial Regex DayPattern();

    public static void MapDiesel(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/diesel").WithTags("Diesel");

        group.MapGet("", async (HttpContext context, IUserAccessor users,
            ScmosDbContext db, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูราคาน้ำมัน", StatusCodes.Status403Forbidden);

            var rows = await db.DieselPrices.AsNoTracking().ToListAsync(token);

            // Newest first, sorted as yyyyMMdd rather than as the dd/MM/yyyy
            // text — which would put the 30th of a month before the 3rd.
            return Results.Json(rows
                .OrderByDescending(one => Sortable(one.EffectiveDate), StringComparer.Ordinal)
                .Select(one => new
                {
                    one.Id,
                    date = one.EffectiveDate,
                    price = one.Price,
                    one.Source,
                    recordedBy = one.RecordedBy,
                })
                .ToList());
        });

        // One day, written on its own — the way the operators keep the table,
        // a price a day, each saved as it is keyed. An upsert rather than a
        // replace, so two people keying two days never delete each other's.
        group.MapPut("/{**date}", async (string date, [FromBody] DayInput? body, HttpContext context,
            IUserAccessor users, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.RecordDiesel))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์บันทึกราคาน้ำมัน", StatusCodes.Status403Forbidden);

            var day = (date ?? "").Trim();
            if (!DayPattern().IsMatch(day) || Formats.ParseDay(day) is null)
                return ApiResults.Error("วันที่ต้องเป็น dd/MM/yyyy และมีจริงตามปฏิทิน", StatusCodes.Status400BadRequest);
            if (body is null || body.Price <= 0 || body.Price >= 200)
                return ApiResults.Error("ราคาน้ำมันดูไม่ถูกต้อง (บาทต่อลิตร)", StatusCodes.Status400BadRequest);
            // Not tomorrow's: the pump price is published on the day, and a
            // figure keyed ahead would sit in the average as though it had
            // been read off the board.
            if (Formats.ParseDay(day) > DateOnly.FromDateTime(Formats.Now.DateTime))
                return ApiResults.Error("ยังบันทึกราคาล่วงหน้าไม่ได้ — ใส่ได้ถึงวันนี้", StatusCodes.Status400BadRequest);

            var row = await db.DieselPrices.FirstOrDefaultAsync(one => one.EffectiveDate == day, token);
            var before = row?.Price;
            if (row is null)
            {
                db.DieselPrices.Add(new DieselPrice
                {
                    EffectiveDate = day, Price = body.Price, Source = body.Source ?? "",
                    RecordedBy = user.Signature, RecordedAt = DateTimeOffset.UtcNow,
                });
            }
            else if (row.Price != body.Price)
            {
                row.Price = body.Price;
                row.Source = body.Source ?? row.Source;
                row.RecordedBy = user.Signature;
                row.RecordedAt = DateTimeOffset.UtcNow;
            }
            else return Results.Json(new { message = "ราคาเดิมอยู่แล้ว", date = day, price = row.Price });

            await db.SaveChangesAsync(token);
            await audit.RecordAsync(user, "save", "diesel", day, $"ราคาน้ำมันดีเซล {day}",
                "บาท/ลิตร", before?.ToString() ?? "", body.Price.ToString(), "", token);
            return Results.Json(new { message = $"บันทึกราคาวันที่ {day} แล้ว", date = day, price = body.Price });
        });

        group.MapDelete("/{**date}", async (string date, HttpContext context,
            IUserAccessor users, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.RecordDiesel))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์บันทึกราคาน้ำมัน", StatusCodes.Status403Forbidden);

            var day = (date ?? "").Trim();
            var row = await db.DieselPrices.FirstOrDefaultAsync(one => one.EffectiveDate == day, token);
            if (row is null) return ApiResults.Error("ไม่มีราคาของวันนี้ให้ลบ", StatusCodes.Status404NotFound);

            db.DieselPrices.Remove(row);
            await db.SaveChangesAsync(token);
            await audit.RecordAsync(user, "delete", "diesel", day, $"ราคาน้ำมันดีเซล {day}",
                "บาท/ลิตร", row.Price.ToString(), "", "ลบราคาที่กรอกผิดวัน", token);
            return Results.Json(new { message = $"ลบราคาวันที่ {day} แล้ว", date = day });
        });

        group.MapPut("", async ([FromBody] List<DieselInput> body, HttpContext context,
            IUserAccessor users, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ราคาน้ำมัน", StatusCodes.Status403Forbidden);

            if (ApiResults.NeedsSecondFactor(users, user, Capability.EditRates) is { } weak)
                return weak;

            var wanted = body
                .Where(one => DayPattern().IsMatch((one.EffectiveDate ?? "").Trim()))
                // A pump price is between about 20 and 50. The bound is wide
                // because this is here to catch a figure typed into the wrong
                // box, not to have an opinion about the market.
                .Where(one => one.Price > 0 && one.Price < 200)
                .GroupBy(one => one.EffectiveDate.Trim(), StringComparer.Ordinal)
                // One day given twice in a single save is a typing slip, not two
                // prices. The last wins, which is what an editor expects.
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

            if (wanted.Count == 0)
                return ApiResults.Error("ไม่มีราคาที่บันทึกได้", StatusCodes.Status400BadRequest);

            var held = await db.DieselPrices.ToListAsync(token);
            var byDate = held.ToDictionary(one => one.EffectiveDate, StringComparer.Ordinal);
            var added = 0;
            var changed = 0;

            foreach (var (date, input) in wanted)
            {
                if (byDate.TryGetValue(date, out var row))
                {
                    if (row.Price == input.Price) continue;
                    row.Price = input.Price;
                    row.Source = input.Source ?? row.Source;
                    row.RecordedBy = user.Signature;
                    row.RecordedAt = DateTimeOffset.UtcNow;
                    changed++;
                    continue;
                }
                db.DieselPrices.Add(new DieselPrice
                {
                    EffectiveDate = date,
                    Price = input.Price,
                    Source = input.Source ?? "",
                    RecordedBy = user.Signature,
                    RecordedAt = DateTimeOffset.UtcNow,
                });
                added++;
            }

            // Rows the editor removed. Deleted rather than kept: a price nobody
            // published is not history, it is a mistake, and leaving it in would
            // move a month's average.
            var doomed = held.Where(one => !wanted.ContainsKey(one.EffectiveDate)).ToList();
            if (doomed.Count > 0) db.DieselPrices.RemoveRange(doomed);

            await db.SaveChangesAsync(token);

            // A diesel figure is a rate, and a rate change is one of the things
            // somebody has to be able to point at afterwards.
            await audit.RecordAsync(user, "save", "diesel", "", "ราคาน้ำมันดีเซล",
                "", "", $"เพิ่ม {added} · แก้ {changed} · ลบ {doomed.Count}", "", token);

            return Results.Json(new { added, changed, removed = doomed.Count, total = wanted.Count });
        });
    }

    /// <summary>dd/MM/yyyy as yyyyMMdd, so the 30th does not sort before the 3rd.</summary>
    private static string Sortable(string date) =>
        date.Length == 10 ? date[6..] + date[3..5] + date[..2] : "";
}
