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
/// <summary>
/// One place name SCMOS already knows, offered as a suggestion.
/// </summary>
/// <param name="Name">The spelling to put in the box, as somebody already wrote it.</param>
/// <param name="Used">How many times it appears. The ordering, so the common ones come first.</param>
/// <param name="Saved">Whether a measured journey already uses it, so picking it is likely to
/// land on a distance that is already known.</param>
public record KnownPlace(string Name, int Used, bool Saved);

public class JourneyService(ScmosDbContext db, JobRegisterCache register)
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
    /// The place names this company actually uses, for the two boxes on the
    /// quotation screen.
    ///
    /// <para>
    /// The reason this exists rather than a better geocoder: the register's
    /// destinations are "HAZCHEM", "FGL-WH KM9", "LS WH", "DC.KM39", "BPK".
    /// Those are not places on a map and no geocoding service will ever find
    /// them — they are this company's own names for gates it has been driving
    /// to for years. Searched for on a map they either miss, or, worse, match
    /// something confidently wrong and return a distance to it.
    /// </para>
    ///
    /// <para>
    /// So the search is over what SCMOS already holds: every place a measured
    /// journey names, and every destination, yard and plant written on a job.
    /// Picking a suggestion means typing the same spelling as last time, which
    /// is what makes a saved distance findable at all — <c>LookAsync</c> matches
    /// a journey by its key, and "BKK PORT" and "BKK Port " are two lanes to it
    /// until somebody types one of them the same way twice.
    /// </para>
    ///
    /// <para>
    /// The whole list at once, not a server-side search. It is a few hundred
    /// names — the browser filters them as fast as anybody types, and a
    /// suggestion that appears without a round trip is the difference between a
    /// box people use and one they type past.
    /// </para>
    /// </summary>
    public async Task<List<KnownPlace>> PlacesAsync(CancellationToken token)
    {
        // Case-insensitive, because the point is to stop the same gate existing
        // under three spellings. The spelling kept is the one used most, so the
        // list offers what the office already agrees on.
        // Best is the largest single contribution any one spelling made, and it
        // is what decides which spelling is displayed. Used is the total across
        // all of them, which is what decides the order. Keeping both apart
        // matters: a name written once in the register and fifty times on a
        // measured journey should show the journey's spelling, not the register's.
        var seen = new Dictionary<string, (string Name, int Used, int Best, bool Saved)>(
            StringComparer.OrdinalIgnoreCase);

        void Add(string? raw, bool saved, int weight = 1)
        {
            var name = Formats.Clean(raw ?? "");
            if (name.Length == 0) return;

            if (!seen.TryGetValue(name, out var had))
            {
                seen[name] = (name, weight, weight, saved);
                return;
            }

            // "Saved" is sticky: a name is worth marking if any journey uses it.
            seen[name] = weight > had.Best
                ? (name, had.Used + weight, weight, had.Saved || saved)
                : (had.Name, had.Used + weight, had.Best, had.Saved || saved);
        }

        // The measured journeys first, so their spellings win ties and a place
        // with a known distance is marked as one.
        var lanes = await db.JourneyDistances.AsNoTracking()
            .Select(one => new { one.FromPlace, one.ToPlace, one.UsedCount })
            .ToListAsync(token);
        foreach (var lane in lanes)
        {
            Add(lane.FromPlace, saved: true, weight: Math.Max(1, lane.UsedCount));
            Add(lane.ToPlace, saved: true, weight: Math.Max(1, lane.UsedCount));
        }

        // Then the register, read from the snapshot the other first-page
        // services share rather than with a query of its own.
        var snapshot = await register.ReadAsync(token);
        foreach (var row in snapshot.Rows)
        {
            if (row.Raw.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            foreach (var field in PlaceFields)
            {
                if (row.Raw.TryGetProperty(field, out var value)
                    && value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    Add(value.GetString(), saved: false);
                }
            }
        }

        return [.. seen.Values
            // The container yard's own block codes — D1, A0, B4, "17", EA — are
            // written in cyYard beside a real destination, and the same code
            // appears against a dozen different ones. They are positions inside
            // a yard, not places a lorry is quoted to, and thirteen of them sat
            // at the top of this list by sheer frequency. Every real name in the
            // register is three characters or more, "BPK" included.
            //
            // A place a measured journey uses is kept whatever its length:
            // somebody typed it deliberately and there is a distance against it.
            .Where(one => one.Saved || one.Name.Length >= 3)
            .OrderByDescending(one => one.Saved)
            .ThenByDescending(one => one.Used)
            .ThenBy(one => one.Name, StringComparer.OrdinalIgnoreCase)
            .Select(one => new KnownPlace(one.Name, one.Used, one.Saved))];
    }

    /// <summary>
    /// The job fields that name a place a lorry goes to.
    ///
    /// Read off the blob rather than from <c>JobRecord</c>, which does not
    /// promote them. Not the customer's name: a customer is not a destination,
    /// and offering one in the box is how a quotation gets measured to a company
    /// rather than to the gate it ships from.
    /// </summary>
    private static readonly string[] PlaceFields = ["destination", "cyYard", "plant", "returnLoc"];

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
