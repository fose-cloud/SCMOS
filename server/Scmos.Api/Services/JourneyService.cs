using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>What a set of past prices looked like. Null where there were none.</summary>
public record PriceBand(int Count, int Low, int Mid, int High);

public record JourneyView(int Id, string FromPlace, string ToPlace, int Km,
    string SetBy, string SetAt, int UsedCount);

public record JourneyLook(
    /// <summary>The remembered distance, when this journey has been priced before.</summary>
    JourneyView? Known,
    /// <summary>What carriers have quoted for this journey — the rate inquiries.</summary>
    PriceBand? Quoted,
    /// <summary>What the rate book holds for it — the agreed contract price.</summary>
    PriceBand? Contracted,
    /// <summary>The lanes these figures came from, so a match can be judged.</summary>
    List<string> Matched,
    /// <summary>Below this, a range says more about the sample than the price.</summary>
    int Minimum);

public record JourneyResult(bool Ok, string Message, JourneyView? Journey = null);

/// <summary>
/// The journeys this company prices, what they cost last time, and how far.
///
/// Two questions with one answer between them. "How far is it" is asked because
/// the calculator needs a number; "what has this actually cost" is asked because
/// a calculated price nobody checks is a price nobody should send. Both are
/// about the same journey, so both are looked up together and the screen shows
/// them side by side.
/// </summary>
public class JourneyService(ScmosDbContext db)
{
    /// <summary>
    /// How few past prices is too few to read a range from.
    ///
    /// Two quotes that differ tell you nothing about which is typical. The
    /// screen still shows them — hiding what exists is its own kind of lie —
    /// but it says the sample is thin rather than drawing a band across it.
    /// </summary>
    public const int Minimum = 3;

    public async Task<List<JourneyView>> ListAsync(CancellationToken token) =>
        await db.JourneyDistances.AsNoTracking()
            .OrderByDescending(one => one.UsedCount).ThenBy(one => one.FromPlace)
            .Take(500)
            .Select(one => new JourneyView(one.Id, one.FromPlace, one.ToPlace, one.Km,
                one.SetBy, one.SetAt.ToString("dd/MM/yyyy"), one.UsedCount))
            .ToListAsync(token);

    /// <summary>
    /// Everything known about one journey: how far, and what it has cost.
    /// </summary>
    public async Task<JourneyLook> LookAsync(string from, string to, string vehicle,
        CancellationToken token)
    {
        var known = await FindAsync(from, to, token);
        var view = known is null ? null
            : new JourneyView(known.Id, known.FromPlace, known.ToPlace, known.Km,
                known.SetBy, known.SetAt.ToString("dd/MM/yyyy"), known.UsedCount);

        if (from.Trim().Length == 0 || to.Trim().Length == 0 || vehicle.Trim().Length == 0)
            return new JourneyLook(view, null, null, [], Minimum);

        var matched = new List<string>();
        var quoted = await QuotedAsync(from, to, vehicle, matched, token);
        var contracted = await ContractedAsync(from, to, vehicle, matched, token);

        return new JourneyLook(view, quoted, contracted, matched.Distinct().Take(6).ToList(), Minimum);
    }

    /// <summary>
    /// Prices carriers have quoted for this journey, out of the rate inquiries.
    ///
    /// Narrowed in SQL by one word from each end so the whole table is not read,
    /// then judged in memory by <see cref="JourneyKey.SameJourney"/> — the same
    /// rule the distance book is keyed on, so a journey found here is a journey
    /// the distance can be stored against.
    /// </summary>
    private async Task<PriceBand?> QuotedAsync(string from, string to, string vehicle,
        List<string> matched, CancellationToken token)
    {
        var (fromWord, toWord) = Narrow(from, to);
        if (fromWord.Length == 0 || toWord.Length == 0) return null;

        var lanes = await db.RateInquiryLanes.AsNoTracking()
            .Where(lane => EF.Functions.Like(lane.FromPlace, $"%{fromWord}%")
                && EF.Functions.Like(lane.ToPlace, $"%{toWord}%"))
            .Select(lane => new { lane.Id, lane.FromPlace, lane.ToPlace })
            .Take(400)
            .ToListAsync(token);

        var wanted = lanes
            .Where(lane => JourneyKey.SameJourney(from, to, lane.FromPlace, lane.ToPlace))
            .ToList();
        if (wanted.Count == 0) return null;

        foreach (var lane in wanted.Take(6)) matched.Add($"{lane.FromPlace} → {lane.ToPlace}");

        var ids = wanted.Select(lane => lane.Id).ToList();
        var prices = await db.RateInquiryPrices.AsNoTracking()
            .Where(price => ids.Contains(price.LaneId) && price.Vehicle == vehicle && price.Price > 0)
            .Select(price => price.Price)
            .ToListAsync(token);

        return Band(prices);
    }

    /// <summary>The same journey in the rate book — what has actually been agreed.</summary>
    private async Task<PriceBand?> ContractedAsync(string from, string to, string vehicle,
        List<string> matched, CancellationToken token)
    {
        var (fromWord, toWord) = Narrow(from, to);
        if (fromWord.Length == 0 || toWord.Length == 0) return null;

        var lanes = await db.RateLanes.AsNoTracking()
            .Where(lane => EF.Functions.Like(lane.FromPlace, $"%{fromWord}%")
                && EF.Functions.Like(lane.ToPlace, $"%{toWord}%"))
            .Select(lane => new { lane.Id, lane.FromPlace, lane.ToPlace })
            .Take(400)
            .ToListAsync(token);

        var wanted = lanes
            .Where(lane => JourneyKey.SameJourney(from, to, lane.FromPlace, lane.ToPlace))
            .ToList();
        if (wanted.Count == 0) return null;

        foreach (var lane in wanted.Take(6)) matched.Add($"{lane.FromPlace} → {lane.ToPlace}");

        var ids = wanted.Select(lane => lane.Id).ToList();
        var prices = await db.RatePrices.AsNoTracking()
            .Where(price => ids.Contains(price.LaneId) && price.Vehicle == vehicle && price.Price > 0)
            .Select(price => price.Price)
            .ToListAsync(token);

        return Band(prices);
    }

    /// <summary>
    /// One word from each end, for the SQL that narrows the search.
    ///
    /// The longest, because it is the most particular: "port" appears on
    /// hundreds of lanes and "bangsaothong" on a handful.
    /// </summary>
    private static (string From, string To) Narrow(string from, string to)
    {
        var pick = (string place) => JourneyKey.Words(place)
            .OrderByDescending(word => word.Length).FirstOrDefault() ?? "";
        return (pick(from), pick(to));
    }

    private static PriceBand? Band(List<int> prices)
    {
        if (prices.Count == 0) return null;
        prices.Sort();
        return new PriceBand(prices.Count, prices[0], prices[prices.Count / 2], prices[^1]);
    }

    private Task<JourneyDistance?> FindAsync(string from, string to, CancellationToken token)
    {
        var key = JourneyKey.Of(from, to);
        return db.JourneyDistances.FirstOrDefaultAsync(one => one.Key == key, token);
    }

    /// <summary>
    /// Records how far a journey is, or corrects it.
    ///
    /// A correction is a correction, not a second row: the key is unique, so the
    /// same road cannot end up with two lengths.
    /// </summary>
    public async Task<JourneyResult> SaveAsync(string from, string to, int km, string by,
        CancellationToken token)
    {
        if (from.Trim().Length == 0 || to.Trim().Length == 0)
            return new JourneyResult(false, "ต้องระบุทั้งต้นทางและปลายทาง");
        if (km <= 0) return new JourneyResult(false, "ระยะทางต้องมากกว่า 0 กิโลเมตร");
        if (km > 3000) return new JourneyResult(false, "ระยะทางเกิน 3,000 กม. — ตรวจสอบตัวเลขอีกครั้ง");

        var found = await FindAsync(from, to, token);
        var was = found?.Km;

        if (found is null)
        {
            found = new JourneyDistance
            {
                Key = JourneyKey.Of(from, to),
                FromPlace = from.Trim(),
                ToPlace = to.Trim(),
            };
            db.JourneyDistances.Add(found);
        }

        found.Km = km;
        found.SetBy = by;
        found.SetAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        var view = new JourneyView(found.Id, found.FromPlace, found.ToPlace, found.Km,
            found.SetBy, found.SetAt.ToString("dd/MM/yyyy"), found.UsedCount);
        return new JourneyResult(true,
            was is null ? $"จำระยะทาง {km} กม. แล้ว"
                : was == km ? "ระยะทางเท่าเดิม"
                : $"แก้ระยะทางจาก {was} เป็น {km} กม. แล้ว",
            view);
    }

    /// <summary>Counted when a journey is actually priced, so the list ranks by use.</summary>
    public async Task UsedAsync(string from, string to, CancellationToken token)
    {
        var found = await FindAsync(from, to, token);
        if (found is null) return;
        found.UsedCount++;
        await db.SaveChangesAsync(token);
    }
}
