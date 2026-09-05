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
        Dictionary<string, int>? Prices, bool Domestic = false);

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

        // The provinces the sheet already uses, rather than a list of Thailand's
        // seventy-seven. Offering all of them would suggest the register knows
        // about provinces it has never seen a lane in, and the spelling that
        // matters here is the one the existing rows are written with.
        var counties = await db.RateInquiryLanes.AsNoTracking()
            .Where(lane => lane.County != "")
            .Select(lane => lane.County)
            .Distinct().OrderBy(name => name).Take(200)
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
            counties,
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
    /// <summary>
    /// The bar above the sheet, as one value.
    ///
    /// Every picker carries several values at once, pipe-separated, the way My
    /// Job's do — see <see cref="AnyOfFilter"/>, which both bars read through.
    /// A record rather than ten parameters, because the next filter should cost
    /// a field and not another argument threaded through three files.
    /// </summary>
    public record SheetQuery(
        string Search = "", string Customer = "", string Requestor = "",
        string Carrier = "", string County = "",
        string Year = "", string Month = "", string Day = "",
        int Page = 1, int Per = 50);

    /// <summary>
    /// What each picker may offer, taken from the whole register.
    ///
    /// Not from the page, and not from the filtered result. A list built out of
    /// what is already showing makes the second picker useless — choose a
    /// customer and the carrier list narrows to that customer's carriers, so
    /// there is no way to widen back out except by clearing what you just set.
    /// </summary>
    public record SheetChoices(
        IReadOnlyList<string> Customers, IReadOnlyList<string> Requestors,
        IReadOnlyList<string> Carriers, IReadOnlyList<string> Counties,
        IReadOnlyList<string> Years, IReadOnlyList<string> Months,
        IReadOnlyList<string> Dates, int Undated);

    public async Task<SheetPage> SheetAsync(SheetQuery query, CancellationToken token)
    {
        var lanes = from lane in db.RateInquiryLanes.AsNoTracking()
                    join inquiry in db.RateInquiries.AsNoTracking() on lane.InquiryId equals inquiry.Id
                    select new { lane, inquiry };

        // Anything SQL can answer is answered in SQL. An any-of picker becomes
        // an IN clause; a customer chosen from the list is that customer, not
        // everything containing their name — "Thai Oil" would otherwise drag in
        // "Thai Oil Marine" and the count would not match the tick.
        var customers = AnyOfFilter.Wanted(query.Customer);
        if (customers.Length > 0) lanes = lanes.Where(row => customers.Contains(row.inquiry.Customer));

        var requestors = AnyOfFilter.Wanted(query.Requestor);
        if (requestors.Length > 0) lanes = lanes.Where(row => requestors.Contains(row.inquiry.Requestor));

        var counties = AnyOfFilter.Wanted(query.County);
        if (counties.Length > 0) lanes = lanes.Where(row => counties.Contains(row.lane.County));

        var wanted = query.Search.Trim();
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

        var ordered = lanes.OrderByDescending(row => row.inquiry.Id).ThenBy(row => row.lane.Id);
        var size = Math.Clamp(query.Per, 1, 500);
        var at = Math.Max(1, query.Page);

        /*
         * Two filters SQL cannot be asked for, and what they cost.
         *
         * Subcon is a list inside one column — "SANGJA,SSL,PHURADA" — so it has
         * to be split before it can be matched, and the period rule is the one
         * My Job uses, which is C#. Both are applied here, over a projection of
         * two short strings per lane rather than over the rows themselves:
         * three thousand of those is nothing, three thousand full rows with
         * their prices is the quarter of a million cells this screen was paged
         * to avoid.
         *
         * And only when one of them is actually set. With the bar untouched —
         * which is how the screen opens — nothing is materialised and the page
         * comes back from SQL as it always did.
         */
        List<long> pageIds;
        int total;
        var narrowing = AnyOfFilter.Wanted(query.Carrier).Length > 0
            || AnyOfFilter.Wanted(query.Year).Length > 0
            || AnyOfFilter.Wanted(query.Month).Length > 0
            || AnyOfFilter.Wanted(query.Day).Length > 0;

        if (narrowing)
        {
            var narrow = await ordered
                .Select(row => new { row.lane.Id, row.lane.Carriers, row.inquiry.InquiredOn })
                .ToListAsync(token);
            var kept = narrow
                .Where(row => AnyOfFilter.IsAnyOfList(row.Carriers, query.Carrier)
                    && AnyOfFilter.InPeriod(row.InquiredOn, query.Year, query.Month, query.Day))
                .ToList();
            total = kept.Count;
            pageIds = [.. kept.Skip((at - 1) * size).Take(size).Select(row => row.Id)];
        }
        else
        {
            total = await ordered.CountAsync(token);
            pageIds = await ordered.Skip((at - 1) * size).Take(size)
                .Select(row => row.lane.Id).ToListAsync(token);
        }

        var slice = await (from lane in db.RateInquiryLanes.AsNoTracking()
                           join inquiry in db.RateInquiries.AsNoTracking() on lane.InquiryId equals inquiry.Id
                           where pageIds.Contains(lane.Id)
                           select new { lane, inquiry }).ToListAsync(token);
        // `IN` promises no order, and this page's order was settled above.
        // Restored rather than sorted a second time, so the two cannot disagree
        // about which fifty rows page three is.
        var place = pageIds.Select((id, index) => (id, index)).ToDictionary(one => one.id, one => one.index);
        slice = [.. slice.OrderBy(row => place[row.lane.Id])];

        var prices = await db.RateInquiryPrices.AsNoTracking()
            .Where(price => pageIds.Contains(price.LaneId))
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
    /// Every value each picker can offer, and how many rows have no usable date.
    ///
    /// Asked for once when the screen opens rather than with every page: the
    /// lists do not change while somebody turns pages, and a scan of the whole
    /// register on each page turn would be paid for nothing.
    ///
    /// The undated count is returned because it is the one number the period
    /// pickers cannot show. Every other choice on that bar hides those rows, so
    /// without it "no date" is an option with no sign that anything is behind it.
    /// </summary>
    public async Task<SheetChoices> SheetChoicesAsync(CancellationToken token)
    {
        var customers = await db.RateInquiries.AsNoTracking()
            .Select(one => one.Customer).Distinct().ToListAsync(token);
        var requestors = await db.RateInquiries.AsNoTracking()
            .Select(one => one.Requestor).Distinct().ToListAsync(token);
        var dates = await db.RateInquiries.AsNoTracking()
            .Select(one => one.InquiredOn).Distinct().ToListAsync(token);
        var counties = await db.RateInquiryLanes.AsNoTracking()
            .Select(one => one.County).Distinct().ToListAsync(token);
        // Distinct over the whole column first, so the split below runs over a
        // hundred strings rather than three thousand.
        var carrierLists = await db.RateInquiryLanes.AsNoTracking()
            .Select(one => one.Carriers).Distinct().ToListAsync(token);

        var carriers = carrierLists.SelectMany(list =>
            (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var years = new SortedSet<string>(StringComparer.Ordinal);
        var months = new SortedSet<string>(StringComparer.Ordinal);
        var days = new List<string>();
        var undated = 0;
        foreach (var date in dates)
        {
            var parts = (date ?? "").Split('/');
            if (parts.Length != 3) { undated++; continue; }
            years.Add(parts[2]);
            months.Add(parts[1]);
            days.Add(date!);
        }

        return new SheetChoices(
            Named(customers), Named(requestors), Named(carriers), Named(counties),
            [.. years], [.. months],
            // Newest first, the way the sheet itself is ordered.
            [.. days.Distinct().OrderByDescending(Rank)],
            undated);

        static IReadOnlyList<string> Named(IEnumerable<string?> values) =>
        [.. values.Select(one => (one ?? "").Trim())
            .Where(one => one.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(one => one, StringComparer.OrdinalIgnoreCase)];

        static int Rank(string date)
        {
            var parts = date.Split('/');
            return parts.Length == 3 && int.TryParse(parts[2], out var y)
                && int.TryParse(parts[1], out var m) && int.TryParse(parts[0], out var d)
                ? (y * 10000) + (m * 100) + d
                : 0;
        }
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

    /* ------------------------------------------------------------ removing */

    /// <summary>
    /// What each of these rows says, written out before it is taken away.
    ///
    /// There is no history table behind the rate book. A deleted lane is gone
    /// the moment the transaction commits, and the audit row is the only thing
    /// that will still know it existed — so the audit row has to carry enough
    /// to type it back in, not just the id of something nobody can look up.
    /// </summary>
    public async Task<Dictionary<long, string>> SnapshotLanesAsync(
        IReadOnlyList<long> laneIds, CancellationToken token)
    {
        var lanes = await db.RateInquiryLanes.AsNoTracking()
            .Where(lane => laneIds.Contains(lane.Id)).ToListAsync(token);
        var inquiryIds = lanes.Select(lane => lane.InquiryId).Distinct().ToList();
        var inquiries = await db.RateInquiries.AsNoTracking()
            .Where(one => inquiryIds.Contains(one.Id))
            .ToDictionaryAsync(one => one.Id, token);
        var prices = await db.RateInquiryPrices.AsNoTracking()
            .Where(price => laneIds.Contains(price.LaneId)).ToListAsync(token);

        return lanes.ToDictionary(lane => lane.Id, lane =>
        {
            var head = inquiries.GetValueOrDefault(lane.InquiryId);
            var quoted = prices.Where(price => price.LaneId == lane.Id)
                .OrderBy(price => price.Vehicle)
                .Select(price => $"{price.Vehicle} {price.Price}");
            var modes = new[] { lane.Fcl ? "FCL" : "", lane.Lcl ? "LCL" : "", lane.Domestic ? "Domestic" : "" }
                .Where(one => one.Length > 0);
            return string.Join(" · ", new[]
            {
                head is null ? "" : $"{head.InquiredOn} NO.{head.Number}",
                head?.Customer ?? "",
                head?.Requestor ?? "",
                $"{lane.FromPlace} → {lane.ToPlace}",
                lane.County,
                lane.Carriers,
                string.Join("/", modes),
                string.Join(" ", quoted),
                lane.Remark,
            }.Where(part => part.Length > 0));
        });
    }

    /// <summary>
    /// Takes these lanes out, with their prices, and any request left empty.
    ///
    /// An inquiry whose last lane goes has nothing left to show: the sheet is a
    /// row per lane, so it would disappear from the screen while staying in the
    /// register, counted by the totals and offered by the pickers, for ever. It
    /// goes with the lane that was holding it up.
    ///
    /// The whole set in one transaction, because a half-deleted request is a
    /// worse state than either end of this.
    /// </summary>
    public async Task<(int Lanes, int Inquiries)> DeleteLanesAsync(
        IReadOnlyList<long> laneIds, CancellationToken token) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);

            var lanes = await db.RateInquiryLanes
                .Where(lane => laneIds.Contains(lane.Id)).ToListAsync(token);
            if (lanes.Count == 0) return (0, 0);

            var touched = lanes.Select(lane => lane.InquiryId).Distinct().ToList();
            var going = lanes.Select(lane => lane.Id).ToList();

            db.RateInquiryPrices.RemoveRange(
                await db.RateInquiryPrices.Where(price => going.Contains(price.LaneId)).ToListAsync(token));
            db.RateInquiryLanes.RemoveRange(lanes);
            await db.SaveChangesAsync(token);

            // Asked after the lanes are gone rather than counted before, so a
            // request that already had other lanes is left exactly alone.
            var emptied = await db.RateInquiries
                .Where(one => touched.Contains(one.Id)
                    && !db.RateInquiryLanes.Any(lane => lane.InquiryId == one.Id))
                .ToListAsync(token);
            db.RateInquiries.RemoveRange(emptied);
            await db.SaveChangesAsync(token);

            await transaction.CommitAsync(token);
            return (lanes.Count, emptied.Count);
        });

    public async Task<Result> CreateAsync(AppUser user, InquiryPost post, CancellationToken token)
    {
        // A calculator save already owns a transaction containing its receipt.
        if (db.Database.CurrentTransaction is not null)
            return await CreateLockedAsync(user, post, token);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var result = await CreateLockedAsync(user, post, token);
            if (result.Ok) await transaction.CommitAsync(token);
            return result;
        });
    }

    private async Task<Result> CreateLockedAsync(AppUser user, InquiryPost post, CancellationToken token)
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
            if (!lane.Fcl && !lane.Lcl && !lane.Domestic)
                return new Result(false, $"เส้นทางที่ {position}: ต้องเลือก FCL, LCL หรือ Domestic อย่างน้อยหนึ่งอย่าง");

            foreach (var (code, price) in lane.Prices ?? [])
            {
                if (!RateVehicles.IsKnown(code))
                    return new Result(false, $"เส้นทางที่ {position}: ไม่รู้จักประเภทรถ \"{code}\"");
                if (price < 0)
                    return new Result(false, $"เส้นทางที่ {position}: ราคาติดลบไม่ได้ ({code})");
            }
        }

        var written = TrainingRules.Write(date.Value);
        // All create paths share this lock, including Excel import. Two callers
        // must not both read the same max(number) before either has committed.
        await RateWriteLock.TakeAsync(db, "scmos-rate-inquiry-number", token);
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
                Domestic = posted.Domestic,
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
