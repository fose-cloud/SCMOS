using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Raising a rate inquiry, and reading the ones already raised.
///
/// The form the team fills today is a workbook with a sheet per month. What it
/// cannot do is answer a question across months — "how often have we asked
/// about Rayong to Laem Chabang", "which carriers do we actually put questions
/// to" — because each answer lives in a different sheet, and the sheet for the
/// month in question is open on somebody else's screen.
/// </summary>
public class RateInquiryService(ScmosDbContext db)
{
    public record LanePost(
        string? FromPlace, string? ToPlace, string? County, string? Carriers,
        bool Fcl, bool Lcl, string? Remark,
        Dictionary<string, int>? Prices);

    public record InquiryPost(
        string? InquiredOn, string? Customer, string? FuelBand, List<LanePost>? Lanes);

    public record Result(bool Ok, string Message, long Id = 0, int Number = 0);

    /// <summary>
    /// One row of the workbook, which is one lane rather than one inquiry.
    ///
    /// The sheet repeats the date, number, requestor and customer down every
    /// row of a request, so a lane is what a person actually looks at and edits.
    /// The register normalises that into an inquiry with lanes under it; this
    /// puts it back into the shape the file has, because that is the shape the
    /// team reads a rate in.
    /// </summary>
    public record SheetRow(
        long LaneId, long InquiryId,
        string Date, int No, string Requestor, string Customer, string FuelBand,
        string FromPlace, string ToPlace, string County, string Carriers,
        bool Fcl, bool Lcl, bool Domestic, string Remark,
        Dictionary<string, int> Prices);

    public record SheetPage(IReadOnlyList<SheetRow> Rows, int Total, int Page, int Per);

    public record LaneView(
        long Id, string FromPlace, string ToPlace, string County, string Carriers,
        bool Fcl, bool Lcl, string Remark, Dictionary<string, int> Prices);

    public record InquiryView(
        long Id, int Number, string InquiredOn, string Requestor, string RequestorId,
        string Customer, string FuelBand, string Status, string CreatedBy,
        IReadOnlyList<LaneView> Lanes);

    /* ------------------------------------------------------------- reading */

    /// <summary>
    /// Everything the form needs to draw itself: the vehicles a price may be
    /// quoted against, the fuel bands, and the carriers worth offering.
    ///
    /// Sent from here rather than kept in the browser so the list the form
    /// offers and the list the API accepts cannot come apart.
    /// </summary>
    public async Task<object> FormAsync(CancellationToken token)
    {
        var bands = await db.FuelBands.AsNoTracking()
            .OrderBy(band => band.Position)
            .Select(band => new { label = band.Label, position = band.Position })
            .ToListAsync(token);

        var carriers = await db.Suppliers.AsNoTracking()
            .Where(supplier => supplier.Status == "approved" || supplier.Status == "pending-audit")
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => supplier.Name)
            .ToListAsync(token);

        // The customers already asked about, so the same name is not typed three
        // ways across three months.
        var customers = await db.RateInquiries.AsNoTracking()
            .Where(inquiry => inquiry.Customer != "")
            .Select(inquiry => inquiry.Customer)
            .Distinct().OrderBy(name => name).Take(400)
            .ToListAsync(token);

        return new
        {
            vehicles = RateVehicles.All.Select(vehicle => new
            {
                code = vehicle.Code, label = vehicle.Label, group = vehicle.Group,
                dg = vehicle.Dg, reefer = vehicle.Reefer,
            }),
            groups = new[]
            {
                new { key = RateVehicles.Truck, label = "รถบรรทุก · LCL" },
                new { key = RateVehicles.Container, label = "ตู้คอนเทนเนอร์ · FCL" },
                new { key = RateVehicles.Tank, label = "ISO Tank" },
                new { key = RateVehicles.Special, label = "รถพิเศษ" },
            },
            bands,
            carriers,
            customers,
            statuses = RateInquiryStatus.All,
        };
    }

    /// <summary>The inquiries, newest first, optionally narrowed to one person's.</summary>
    public async Task<IReadOnlyList<InquiryView>> ListAsync(
        string? requestorId, string? customer, int take, CancellationToken token)
    {
        var query = db.RateInquiries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(requestorId)) query = query.Where(i => i.RequestorId == requestorId);
        if (!string.IsNullOrWhiteSpace(customer)) query = query.Where(i => i.Customer.Contains(customer));

        var inquiries = await query.OrderByDescending(i => i.Id)
            .Take(Math.Clamp(take, 1, 200)).ToListAsync(token);
        if (inquiries.Count == 0) return [];

        var ids = inquiries.Select(i => i.Id).ToList();
        var lanes = await db.RateInquiryLanes.AsNoTracking()
            .Where(lane => ids.Contains(lane.InquiryId)).OrderBy(lane => lane.Id).ToListAsync(token);

        var laneIds = lanes.Select(lane => lane.Id).ToList();
        var prices = await db.RateInquiryPrices.AsNoTracking()
            .Where(price => laneIds.Contains(price.LaneId)).ToListAsync(token);

        var byLane = prices.GroupBy(price => price.LaneId)
            .ToDictionary(group => group.Key,
                group => group.ToDictionary(price => price.Vehicle, price => price.Price));

        return inquiries.Select(inquiry => new InquiryView(
            inquiry.Id, inquiry.Number, inquiry.InquiredOn, inquiry.Requestor, inquiry.RequestorId,
            inquiry.Customer, inquiry.FuelBand, inquiry.Status, inquiry.CreatedBy,
            lanes.Where(lane => lane.InquiryId == inquiry.Id).Select(lane => new LaneView(
                lane.Id, lane.FromPlace, lane.ToPlace, lane.County, lane.Carriers,
                lane.Fcl, lane.Lcl, lane.Remark,
                byLane.GetValueOrDefault(lane.Id) ?? [])).ToList())).ToList();
    }

    /* ------------------------------------------------------------- writing */

    /// <summary>
    /// Files a new inquiry.
    ///
    /// The requestor is the signed-in person, never a field on the form: an
    /// inquiry is a question somebody asked, and one that could be filed under
    /// a colleague's name answers "who wanted this price" with a guess.
    /// </summary>
    /// <summary>
    /// The register as the workbook lays it out, a page at a time.
    ///
    /// Paged in the database rather than in the browser: three thousand lanes
    /// with twenty-eight price columns each is a quarter of a million cells, and
    /// sending them all to draw fifty rows is how a screen becomes unusable on
    /// the machines this runs on.
    /// </summary>
    public async Task<SheetPage> SheetAsync(string search, string customer, string month,
        int page, int per, CancellationToken token)
    {
        var wanted = search.Trim();
        var lanes = from lane in db.RateInquiryLanes.AsNoTracking()
                    join inquiry in db.RateInquiries.AsNoTracking() on lane.InquiryId equals inquiry.Id
                    select new { lane, inquiry };

        if (customer.Trim().Length > 0)
            lanes = lanes.Where(row => row.inquiry.Customer.Contains(customer.Trim()));

        // The month as the sheet writes a date: dd/MM/yyyy, so "07/2026" is the
        // tail of it.
        if (month.Trim().Length > 0)
            lanes = lanes.Where(row => row.inquiry.InquiredOn.EndsWith(month.Trim()));

        if (wanted.Length > 0)
        {
            lanes = lanes.Where(row =>
                row.inquiry.Customer.Contains(wanted)
                || row.inquiry.Requestor.Contains(wanted)
                || row.lane.FromPlace.Contains(wanted)
                || row.lane.ToPlace.Contains(wanted)
                || row.lane.County.Contains(wanted)
                || row.lane.Carriers.Contains(wanted)
                || row.lane.Remark.Contains(wanted));
        }

        var total = await lanes.CountAsync(token);
        var size = Math.Clamp(per, 1, 500);
        var at = Math.Max(1, page);

        var slice = await lanes
            .OrderByDescending(row => row.inquiry.Id).ThenBy(row => row.lane.Id)
            .Skip((at - 1) * size).Take(size)
            .ToListAsync(token);

        var laneIds = slice.Select(row => row.lane.Id).ToList();
        var prices = await db.RateInquiryPrices.AsNoTracking()
            .Where(price => laneIds.Contains(price.LaneId))
            .ToListAsync(token);
        var byLane = prices.GroupBy(price => price.LaneId)
            .ToDictionary(group => group.Key,
                group => group.ToDictionary(price => price.Vehicle, price => price.Price));

        var rows = slice.Select(row => new SheetRow(
            row.lane.Id, row.inquiry.Id,
            row.inquiry.InquiredOn, row.inquiry.Number, row.inquiry.Requestor,
            row.inquiry.Customer, row.inquiry.FuelBand,
            row.lane.FromPlace, row.lane.ToPlace, row.lane.County, row.lane.Carriers,
            row.lane.Fcl, row.lane.Lcl, row.lane.Domestic, row.lane.Remark,
            byLane.TryGetValue(row.lane.Id, out var found) ? found : [])).ToList();

        return new SheetPage(rows, total, at, size);
    }

    /// <summary>
    /// Writes one cell of that sheet.
    ///
    /// A cell at a time, because that is how the grid is used and because a
    /// whole-row save would overwrite the columns somebody else was editing in
    /// the same second. The field names are the sheet's own; a price is
    /// "price:20F", named for the vehicle it belongs to.
    ///
    /// <para>A price of nothing removes the row rather than storing a zero. Zero
    /// is a free journey, which is a mistake in a rate book, and telling the two
    /// apart later is impossible once it is written.</para>
    /// </summary>
    public async Task<Result> SaveCellAsync(long laneId, string field, string value,
        CancellationToken token)
    {
        var result = await ApplyCellAsync(laneId, field, value, token);
        if (result.Ok) await db.SaveChangesAsync(token);
        return result;
    }

    /// <summary>
    /// A block of cells — a paste, or a Delete that empties them — in one go.
    ///
    /// One save for the lot, so a paste of forty cells is one round trip and one
    /// transaction rather than forty of each. A cell the register refuses is
    /// skipped and named; the rest still land, because a paste that rolls back
    /// forty good values over one bad one is a paste nobody can use.
    /// </summary>
    public async Task<(int Saved, List<string> Refused)> SaveCellsAsync(
        IReadOnlyList<(long LaneId, string Field, string Value)> edits, CancellationToken token)
    {
        var saved = 0;
        var refused = new List<string>();

        foreach (var (laneId, field, value) in edits)
        {
            var result = await ApplyCellAsync(laneId, field, value, token);
            if (result.Ok) saved++;
            else refused.Add($"{field}: {result.Message}");
        }

        if (saved > 0) await db.SaveChangesAsync(token);
        return (saved, refused);
    }

    /// <summary>
    /// Changes one cell in memory, without writing. The two callers above decide
    /// when to save — one cell saves at once, a block saves after the last.
    /// </summary>
    private async Task<Result> ApplyCellAsync(long laneId, string field, string value,
        CancellationToken token)
    {
        var lane = await db.RateInquiryLanes.FirstOrDefaultAsync(one => one.Id == laneId, token);
        if (lane is null) return new Result(false, "ไม่พบเส้นทางนี้");

        var inquiry = await db.RateInquiries.FirstOrDefaultAsync(one => one.Id == lane.InquiryId, token);
        if (inquiry is null) return new Result(false, "ไม่พบใบขอราคาของเส้นทางนี้");

        var text = (value ?? "").Trim();
        var name = (field ?? "").Trim();

        if (name.StartsWith("price:", StringComparison.OrdinalIgnoreCase))
        {
            var code = name["price:".Length..];
            if (!RateVehicles.All.Any(vehicle => vehicle.Code == code))
                return new Result(false, $"ไม่รู้จักรถประเภท {code}");

            var digits = text.Replace(",", "");
            var held = await db.RateInquiryPrices
                .FirstOrDefaultAsync(price => price.LaneId == laneId && price.Vehicle == code, token);

            if (digits.Length == 0 || digits == "0")
            {
                if (held is not null) db.RateInquiryPrices.Remove(held);
                return new Result(true, $"ล้างราคา {code} แล้ว", inquiry.Id, inquiry.Number);
            }

            if (!int.TryParse(digits, out var amount) || amount <= 0)
                return new Result(false, "ราคาต้องเป็นตัวเลขมากกว่า 0");

            if (held is null) db.RateInquiryPrices.Add(new RateInquiryPrice
            { LaneId = laneId, Vehicle = code, Price = amount });
            else held.Price = amount;

            return new Result(true, $"บันทึกราคา {code} = {amount:N0}", inquiry.Id, inquiry.Number);
        }

        var tick = text.Length > 0 && text is not ("0" or "false" or "-" or "no");

        switch (name)
        {
            case "fromPlace": lane.FromPlace = text; break;
            case "toPlace": lane.ToPlace = text; break;
            case "county": lane.County = text; break;
            case "carriers": lane.Carriers = text; break;
            case "remark": lane.Remark = text; break;
            case "fcl": lane.Fcl = tick; break;
            case "lcl": lane.Lcl = tick; break;
            case "domestic": lane.Domestic = tick; break;

            // The request's own fields. Editing one moves every lane under it,
            // which is what the sheet does too — the value is written once and
            // repeated down the rows of that request.
            case "customer": inquiry.Customer = text; break;
            case "requestor": inquiry.Requestor = text; break;
            case "fuelBand": inquiry.FuelBand = text; break;
            case "date":
                if (text.Length > 0 && !Formats.IsDate(text))
                    return new Result(false, "วันที่ต้องเป็น วว/ดด/ปปปป");
                inquiry.InquiredOn = text;
                break;
            case "no":
                if (!int.TryParse(text, out var number) || number < 0)
                    return new Result(false, "เลขที่ต้องเป็นตัวเลข");
                inquiry.Number = number;
                break;

            default: return new Result(false, $"แก้ช่อง {name} ไม่ได้");
        }

        return new Result(true, "บันทึกแล้ว", inquiry.Id, inquiry.Number);
    }

    public async Task<Result> CreateAsync(AppUser user, InquiryPost post, CancellationToken token)
    {
        var customer = (post.Customer ?? "").Trim();
        if (customer.Length == 0) return new Result(false, "ต้องระบุชื่อลูกค้า");

        var date = TrainingRules.ParseDate(post.InquiredOn ?? "");
        if (date is null) return new Result(false, "ต้องระบุวันที่ขอราคา (วว/ดด/ปปปป)");

        var lanes = post.Lanes ?? [];
        if (lanes.Count == 0) return new Result(false, "ต้องมีเส้นทางอย่างน้อยหนึ่งเส้นทาง");

        // Checked before anything is written, so a bad vehicle code on the last
        // lane does not leave the first three saved and the inquiry half filed.
        for (var index = 0; index < lanes.Count; index++)
        {
            var lane = lanes[index];
            var position = index + 1;
            if ((lane.FromPlace ?? "").Trim().Length == 0 || (lane.ToPlace ?? "").Trim().Length == 0)
                return new Result(false, $"เส้นทางที่ {position}: ต้องระบุทั้งต้นทางและปลายทาง");
            if (!lane.Fcl && !lane.Lcl)
                return new Result(false, $"เส้นทางที่ {position}: ต้องเลือก FCL หรือ LCL อย่างน้อยหนึ่งอย่าง");

            foreach (var (code, price) in lane.Prices ?? [])
            {
                if (!RateVehicles.IsKnown(code))
                    return new Result(false, $"เส้นทางที่ {position}: ไม่รู้จักประเภทรถ \"{code}\"");
                if (price < 0)
                    return new Result(false, $"เส้นทางที่ {position}: ราคาติดลบไม่ได้ ({code})");
            }
        }

        var written = TrainingRules.Write(date.Value);
        var inquiry = new RateInquiry
        {
            Number = await NextNumberAsync(written, token),
            InquiredOn = written,
            Requestor = user.DisplayName,
            RequestorId = user.OperatorId,
            Customer = customer,
            FuelBand = (post.FuelBand ?? "").Trim(),
            // Prices may be filled in as the answers arrive, so a new inquiry is
            // only "quoted" when it already carries one.
            Status = lanes.Any(lane => (lane.Prices?.Count ?? 0) > 0)
                ? RateInquiryStatus.Quoted : RateInquiryStatus.Open,
            CreatedBy = user.Signature,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.RateInquiries.Add(inquiry);
        await db.SaveChangesAsync(token);

        foreach (var posted in lanes)
        {
            var lane = new RateInquiryLane
            {
                InquiryId = inquiry.Id,
                FromPlace = (posted.FromPlace ?? "").Trim(),
                ToPlace = (posted.ToPlace ?? "").Trim(),
                County = (posted.County ?? "").Trim(),
                Carriers = (posted.Carriers ?? "").Trim(),
                Fcl = posted.Fcl,
                Lcl = posted.Lcl,
                Remark = (posted.Remark ?? "").Trim(),
            };
            db.RateInquiryLanes.Add(lane);
            await db.SaveChangesAsync(token);

            foreach (var (code, price) in posted.Prices ?? [])
            {
                db.RateInquiryPrices.Add(new RateInquiryPrice
                {
                    LaneId = lane.Id,
                    Vehicle = RateVehicles.Canonical(code),
                    Price = price,
                });
            }
        }

        await db.SaveChangesAsync(token);
        return new Result(true,
            $"บันทึกใบขอราคาเลขที่ {inquiry.Number} · {lanes.Count} เส้นทาง", inquiry.Id, inquiry.Number);
    }

    /// <summary>
    /// The next running number inside the inquiry's own month.
    ///
    /// The workbook restarts at 1 each month and the team refer to inquiries by
    /// that number, so it is kept. It is not the identity of the row — two
    /// inquiries a year apart share a number quite legitimately.
    /// </summary>
    private async Task<int> NextNumberAsync(string date, CancellationToken token)
    {
        // dd/MM/yyyy — the month is the last seven characters.
        var month = date.Length >= 10 ? date[3..] : "";
        if (month.Length == 0) return 1;

        var used = await db.RateInquiries.AsNoTracking()
            .Where(inquiry => inquiry.InquiredOn.EndsWith(month))
            .Select(inquiry => inquiry.Number)
            .ToListAsync(token);

        return used.Count == 0 ? 1 : used.Max() + 1;
    }
}

/// <summary>Where an inquiry has got to.</summary>
public static class RateInquiryStatus
{
    public const string Open = "Open";
    public const string Quoted = "Quoted";
    public const string Closed = "Closed";

    public static readonly string[] All = [Open, Quoted, Closed];

    public static bool IsValid(string status) =>
        All.Contains((status ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
}
