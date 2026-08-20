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
