using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record QuoteVehicleView(int Id, string Code, string Label, int PerKm,
    int BaseCharge, decimal Chill, int DangerousGoods, int Position);

public record QuoteExtraView(int Id, string Label, string Basis, decimal Rate,
    bool Active, int Position);

public record QuoteCardView(List<QuoteVehicleView> Vehicles, List<QuoteExtraView> Extras,
    decimal MarginPercent, string UpdatedBy, string UpdatedAt);

public record QuoteCardResult(bool Ok, string Message);

/// <summary>
/// The card a journey is priced from, and the only copy of it.
///
/// Reading seeds an empty table with the rates the team wrote down, so a fresh
/// environment needs no manual step and every environment starts from the same
/// eleven rows. Nothing about those numbers is fixed after that — they are
/// edited here, by anyone who may change a rate, and the change is audited like
/// any other price movement.
/// </summary>
public class QuoteCardService(ScmosDbContext db)
{
    /// <summary>
    /// The rates as the team priced them, used only when the table is empty.
    ///
    /// Eleven rows exactly as written, including the two that look like errors
    /// and are not: a 6W refrigerated is 20 baht a kilometre where a plain 6W is
    /// 18, and a 40' is priced the same as a 20' — which 1,708 journeys quoting
    /// both agree with, the middle half of the ratio being 1.00 to 1.00.
    ///
    /// The refrigerated rows take the dangerous-goods figure of the vehicle they
    /// are built on. The team's note did not say either way, and the alternative
    /// was to leave them at nought, which would have quietly priced a hazardous
    /// load as an ordinary one.
    /// </summary>
    private static readonly (string Code, string Label, int PerKm, int Base, decimal Chill, int Dg)[] Seed =
    [
        ("4W", "4WH", 8, 1500, 1m, 300),
        ("4W RF", "4WH RF", 8, 1500, 1.5m, 300),
        ("6W", "6WH", 18, 2700, 1m, 500),
        ("6W RF", "6WH RF", 20, 2700, 1.5m, 500),
        ("10W", "10WH", 25, 3500, 1m, 800),
        ("10W RF", "10WH RF", 28, 3500, 1.5m, 800),
        ("20F", "20'", 40, 4000, 1m, 500),
        ("40F", "40'", 40, 4000, 1m, 500),
        ("20RF", "20' Reefer", 40, 4000, 1.5m, 500),
        ("40RF", "40' Reefer", 40, 4000, 1.5m, 500),
        ("20TK", "20' Tank", 40, 4000, 1.5m, 500),
    ];

    /// <summary>
    /// The extras the team already charges for, from the contract's surcharge
    /// list. Seeded once so the screen opens with something to tick rather than
    /// an empty panel and no hint of what belongs in it.
    /// </summary>
    private static readonly (string Label, string Basis, decimal Rate)[] SeedExtras =
    [
        ("ค่ารอ (Waiting time)", QuoteBasis.PerHour, 250m),
        ("ค้างคืน (Overnight)", QuoteBasis.Flat, 800m),
        ("ค่าน้ำมันส่วนเพิ่ม", QuoteBasis.Percent, 5m),
        ("ค่าทางด่วน", QuoteBasis.Flat, 0m),
        ("ค่าผ่านท่า", QuoteBasis.Flat, 0m),
    ];

    public async Task<QuoteCardView> ReadAsync(CancellationToken token)
    {
        if (!await db.QuoteVehicleRates.AnyAsync(token))
        {
            var at = 0;
            foreach (var (code, label, perKm, basePrice, chill, dg) in Seed)
            {
                db.QuoteVehicleRates.Add(new QuoteVehicleRate
                {
                    Code = code, Label = label, PerKm = perKm, BaseCharge = basePrice,
                    Chill = chill, DangerousGoods = dg, Position = at++,
                });
            }
            at = 0;
            foreach (var (label, basis, rate) in SeedExtras)
            {
                db.QuoteExtras.Add(new QuoteExtra
                { Label = label, Basis = basis, Rate = rate, Active = true, Position = at++ });
            }
            await db.SaveChangesAsync(token);
        }

        await OfferEveryVehicleAsync(token);

        var setting = await db.QuoteSettings.FirstOrDefaultAsync(one => one.Id == 1, token);
        var vehicles = await db.QuoteVehicleRates.AsNoTracking()
            .OrderBy(one => one.Position).ThenBy(one => one.Id).ToListAsync(token);
        var extras = await db.QuoteExtras.AsNoTracking()
            .OrderBy(one => one.Position).ThenBy(one => one.Id).ToListAsync(token);

        return new QuoteCardView(
            vehicles.Select(one => new QuoteVehicleView(one.Id, one.Code, one.Label, one.PerKm,
                one.BaseCharge, one.Chill, one.DangerousGoods, one.Position)).ToList(),
            extras.Select(one => new QuoteExtraView(one.Id, one.Label, one.Basis, one.Rate,
                one.Active, one.Position)).ToList(),
            setting?.MarginPercent ?? 10m,
            setting?.UpdatedBy ?? "",
            setting?.UpdatedAt.ToString("dd/MM/yyyy HH:mm") ?? "");
    }

    /// <summary>
    /// Every vehicle the sheet has a column for gets a line on the card.
    ///
    /// The card was seeded with the eleven rates the team had written down, and
    /// stopped there. The sheet prices eighteen vehicles before the DG variants
    /// — side curtain, flat-bed, open top, the two Hiabs, the 6-wheel flatbed
    /// and the 40' tank were all columns somebody could type a figure into and
    /// nothing the calculator would offer. So a quotation for one of them was
    /// worked out on paper and typed straight into the sheet, which is the
    /// arrangement this screen exists to replace.
    ///
    /// The new lines arrive <b>unpriced</b>, and that is the whole point. A rate
    /// is agreed with a carrier; nothing here may invent one. What this does is
    /// put the vehicle on the card so somebody who may change a rate can see it
    /// is missing and type it — and until they do, <c>quoteRate</c> refuses to
    /// price it rather than quietly charging the base fare.
    ///
    /// Only the non-DG vehicles. Dangerous goods is a surcharge on the card's
    /// row and a tick on the form, not a second row: one card line per vehicle
    /// is what keeps the DG column and the plain column the same journey.
    /// </summary>
    private async Task OfferEveryVehicleAsync(CancellationToken token)
    {
        var held = await db.QuoteVehicleRates.Select(one => one.Code).ToListAsync(token);
        var known = new HashSet<string>(held, StringComparer.OrdinalIgnoreCase);

        var missing = RateVehicles.All
            .Where(vehicle => !vehicle.Dg && !known.Contains(vehicle.Code))
            .ToList();
        if (missing.Count == 0) return;

        // After what is there, in the register's own order — which is the
        // workbook's, so the card reads down in the order the sheet reads across.
        var last = await db.QuoteVehicleRates.MaxAsync(one => (int?)one.Position, token) ?? -1;
        foreach (var vehicle in missing)
        {
            db.QuoteVehicleRates.Add(new QuoteVehicleRate
            {
                Code = vehicle.Code,
                Label = vehicle.Label,
                // Nought means "nobody has priced this yet". SaveVehicleAsync
                // refuses a nought, so this is the one way a row can hold one,
                // and the calculator reads it as the refusal it is.
                PerKm = 0, BaseCharge = 0, Chill = 1m, DangerousGoods = 0,
                Position = ++last,
            });
        }

        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            // Two readers can arrive here at once on a cold environment. The
            // unique index on `code` settles it; the loser drops its copy and
            // reads what the winner wrote, because this is a GET and a card is
            // more use than an error.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Changes one vehicle's rate.
    ///
    /// A rate of nought is refused rather than saved. It would price every
    /// journey in that vehicle at the base charge alone and look like a working
    /// answer, which is worse than an obvious refusal.
    /// </summary>
    public async Task<QuoteCardResult> SaveVehicleAsync(int id, int perKm, int baseCharge,
        decimal chill, int dg, CancellationToken token)
    {
        var row = await db.QuoteVehicleRates.FirstOrDefaultAsync(one => one.Id == id, token);
        if (row is null) return new QuoteCardResult(false, "ไม่พบรถประเภทนี้ในตาราง");
        if (perKm <= 0) return new QuoteCardResult(false, "ราคาต่อกิโลเมตรต้องมากกว่า 0");
        if (baseCharge < 0) return new QuoteCardResult(false, "ค่าเริ่มต้นติดลบไม่ได้");
        if (chill < 1m || chill > 5m) return new QuoteCardResult(false, "ตัวคูณห้องเย็นต้องอยู่ระหว่าง 1 ถึง 5");
        if (dg < 0) return new QuoteCardResult(false, "ค่า DG ติดลบไม่ได้");

        row.PerKm = perKm;
        row.BaseCharge = baseCharge;
        row.Chill = chill;
        row.DangerousGoods = dg;
        await db.SaveChangesAsync(token);
        return new QuoteCardResult(true, $"บันทึกอัตรา {row.Label} แล้ว");
    }

    public async Task<QuoteCardResult> SaveExtraAsync(int id, string label, string basis,
        decimal rate, bool active, CancellationToken token)
    {
        var wantedBasis = QuoteBasis.Read(basis);
        if (wantedBasis.Length == 0)
            return new QuoteCardResult(false, "ฐานการคิดต้องเป็น flat, perKm, perHour หรือ percent");
        if (label.Trim().Length == 0) return new QuoteCardResult(false, "ต้องระบุชื่อรายการ");
        if (rate < 0) return new QuoteCardResult(false, "อัตราติดลบไม่ได้");

        // id 0 adds; anything else edits what is there.
        var row = id > 0
            ? await db.QuoteExtras.FirstOrDefaultAsync(one => one.Id == id, token)
            : null;
        if (id > 0 && row is null) return new QuoteCardResult(false, "ไม่พบรายการนี้");

        if (row is null)
        {
            row = new QuoteExtra
            {
                Position = await db.QuoteExtras.AnyAsync(token)
                    ? await db.QuoteExtras.MaxAsync(one => one.Position, token) + 1
                    : 0,
            };
            db.QuoteExtras.Add(row);
        }

        row.Label = label.Trim();
        row.Basis = wantedBasis;
        row.Rate = rate;
        row.Active = active;
        await db.SaveChangesAsync(token);
        return new QuoteCardResult(true, id > 0 ? "บันทึกรายการแล้ว" : "เพิ่มรายการแล้ว");
    }

    public async Task<QuoteCardResult> SetMarginAsync(decimal percent, string by, CancellationToken token)
    {
        if (percent < 0m || percent > 100m)
            return new QuoteCardResult(false, "กำไรต้องอยู่ระหว่าง 0 ถึง 100 เปอร์เซ็นต์");

        var setting = await db.QuoteSettings.FirstOrDefaultAsync(one => one.Id == 1, token);
        if (setting is null)
        {
            setting = new QuoteSetting { Id = 1 };
            db.QuoteSettings.Add(setting);
        }
        setting.MarginPercent = percent;
        setting.UpdatedBy = by;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        return new QuoteCardResult(true, $"ตั้งกำไรเป็น {percent}% แล้ว");
    }
}
