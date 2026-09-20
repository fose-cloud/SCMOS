using Scmos.Api.Endpoints;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// The Carrier TMS API's rules, proved without a database or a network, with
/// <c>--check-carrier-api</c>: what a key is and how it is kept, what the
/// header must look like, what a correlation id may be, how a window and a
/// page are read, what each refusal is called, and — the one that matters —
/// that the contract a carrier is shown carries nothing of another carrier's
/// and nothing commercial beyond its own quoted price.
/// </summary>
public static class CarrierApiCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-api")) return null;

        var failed = 0;
        Console.WriteLine("The Carrier TMS API: a key is a credential, a hash is what is kept, and a carrier sees its own rows.");
        Console.WriteLine();

        /* ---- the key ---- */
        var key = CarrierApi.NewKey();
        var again = CarrierApi.NewKey();
        failed += Say("a new key carries the prefix and 32 bytes of base64url", CarrierApi.IsKeyShaped(key), true);
        failed += Say("and is 52 characters long", key.Length, 52);
        failed += Say("two keys are never the same", key == again, false);
        failed += Say("what is kept is the SHA-256, 64 hex characters, never the key",
            CarrierApi.HashOf(key).Length == 64 && CarrierApi.HashOf(key) != key && CarrierApi.HashOf(key).All(c => char.IsAsciiHexDigitLower(c)), true);
        failed += Say("the same key always hashes the same", CarrierApi.HashOf(key) == CarrierApi.HashOf(key), true);
        failed += Say("a different key hashes differently", CarrierApi.HashOf(key) == CarrierApi.HashOf(again), false);
        failed += Say("the shown prefix is the prefix and four characters, then an ellipsis",
            CarrierApi.ShownPrefixOf(key), key[..13] + "…");
        failed += Say("a client id is 'ck_' and ten characters", CarrierApi.NewClientId() is { Length: 13 } id && id.StartsWith("ck_", StringComparison.Ordinal), true);
        failed += Say("a shortened key is not a key", CarrierApi.IsKeyShaped(key[..^1]), false);
        failed += Say("a key with a character outside base64url is not a key", CarrierApi.IsKeyShaped(key[..^1] + "+"), false);
        failed += Say("a key without the prefix is not a key", CarrierApi.IsKeyShaped(key[CarrierApi.KeyPrefix.Length..]), false);

        Console.WriteLine();
        /* ---- the header ---- */
        failed += Say("the key is read out of 'Authorization: Bearer <key>'", CarrierApi.KeyFrom("Bearer " + key), key);
        failed += Say("the scheme is not case-sensitive, and spaces around the key do not matter", CarrierApi.KeyFrom("bearer  " + key + " "), key);
        failed += Say("no header is no key", CarrierApi.KeyFrom(null), null);
        failed += Say("a basic credential is no key", CarrierApi.KeyFrom("Basic dXNlcjpwYXNz"), null);
        failed += Say("a bearer token that is not shaped like a key is not looked up", CarrierApi.KeyFrom("Bearer eyJhbGciOiJIUzI1NiJ9.e30.x"), null);

        Console.WriteLine();
        /* ---- the correlation id ---- */
        failed += Say("a caller's correlation id is kept when it is plain", CarrierApi.CorrelationId("tms-2026-09-20_00042"), "tms-2026-09-20_00042");
        failed += Say("a missing one is made, 32 hex characters", CarrierApi.CorrelationId(null).Length, 32);
        failed += Say("a blank one is made too", CarrierApi.CorrelationId("   ").Length, 32);
        failed += Say("one carrying a space, a quote or a newline is replaced, not copied into the log",
            CarrierApi.CorrelationId("abc def").Length == 32 && CarrierApi.CorrelationId("a\"b").Length == 32 && CarrierApi.CorrelationId("a\nb").Length == 32, true);
        failed += Say("one longer than 64 characters is replaced", CarrierApi.CorrelationId(new string('a', 65)).Length, 32);
        failed += Say("64 exactly is kept", CarrierApi.CorrelationId(new string('a', 64)).Length, 64);

        Console.WriteLine();
        /* ---- the bucket ---- */
        var bucket = CarrierApi.PartitionOf("Bearer " + key, "10.0.0.5");
        failed += Say("a keyed call is bucketed by its key's hash, not its address", bucket.StartsWith("key:", StringComparison.Ordinal) && !bucket.Contains("10.0.0.5"), true);
        failed += Say("two keys from one address are two buckets", CarrierApi.PartitionOf("Bearer " + again, "10.0.0.5") == bucket, false);
        failed += Say("a call with no key is bucketed by its address", CarrierApi.PartitionOf(null, "10.0.0.5"), "ip:10.0.0.5");
        failed += Say("a bad key is an address bucket too — it cannot spend a real key's allowance", CarrierApi.PartitionOf("Bearer nope", "10.0.0.5"), "ip:10.0.0.5");
        failed += Say("the keyed allowance is 120 a minute", CarrierApi.AllowanceOf(bucket), CarrierApi.RequestsPerMinute);
        failed += Say("the keyless allowance is 20 a minute", CarrierApi.AllowanceOf("ip:10.0.0.5"), CarrierApi.AnonymousRequestsPerMinute);
        failed += Say("the bucket never carries the key itself", bucket.Contains(key), false);

        Console.WriteLine();
        /* ---- the window and the page ---- */
        var today = new DateOnly(2026, 9, 20);
        var window = CarrierApi.Window(null, null, today);
        failed += Say("no bounds is a week back", window?.From, today.AddDays(-7));
        failed += Say("and two weeks ahead", window?.To, today.AddDays(14));
        failed += Say("bounds are read as yyyy-MM-dd", CarrierApi.Window("2026-09-01", "2026-09-30", today), (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        failed += Say("and as the register's dd/MM/yyyy", CarrierApi.Window("01/09/2026", "30/09/2026", today), (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        failed += Say("an end before the start is refused", CarrierApi.Window("2026-09-30", "2026-09-01", today), null);
        failed += Say("a span over 92 days is refused", CarrierApi.Window("2026-01-01", "2026-06-30", today), null);
        failed += Say("a bound that is not a date is refused", CarrierApi.Window("soon", null, today), null);
        failed += Say("an impossible date is refused", CarrierApi.Window("31/02/2026", null, today), null);
        failed += Say("a job dated inside the window is in it", CarrierApi.InWindow("15/09/2026", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), true);
        failed += Say("one dated outside is not", CarrierApi.InWindow("15/10/2026", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), false);
        failed += Say("one with no readable date is in no window", CarrierApi.InWindow("WAIT", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), false);
        failed += Say("a page defaults to one of fifty", CarrierApi.Page(null, null), (1, 50));
        failed += Say("a page is held to two hundred rows", CarrierApi.Page(3, 1000), (3, 200));
        failed += Say("page zero is page one", CarrierApi.Page(0, 10), (1, 10));
        failed += Say("a status of 'offered' or 'accepted' is one group, blank is both, anything else is refused",
            CarrierApi.TryGroup("offered", out var g1) && g1 == CarrierApi.Offered
            && CarrierApi.TryGroup("Accepted", out var g2) && g2 == CarrierApi.Accepted
            && CarrierApi.TryGroup("", out var g3) && g3 is null
            && !CarrierApi.TryGroup("mine", out _), true);

        Console.WriteLine();
        /* ---- the refusals ---- */
        failed += Say("no key is 401", CarrierApi.ProblemOf(CarrierApi.Unauthorized).Status, 401);
        failed += Say("another carrier's assignment is 404 — its existence is not confirmed", CarrierApi.ProblemOf(CarrierApi.NotFound).Status, 404);
        failed += Say("a request that cannot be read is 400", CarrierApi.ProblemOf(CarrierApi.Invalid).Status, 400);
        failed += Say("a state that does not allow the action is 409", CarrierApi.ProblemOf(CarrierApi.Conflict).Status, 409);
        failed += Say("an empty bucket is 429", CarrierApi.ProblemOf(CarrierApi.RateLimited).Status, 429);
        failed += Say("an unknown code is 503, never a stack trace", CarrierApi.ProblemOf("something-else").Status, 503);
        failed += Say("every type is a stable URN a TMS can switch on", CarrierApi.ProblemOf(CarrierApi.NotFound).Type, "urn:scmos:carrier-api:not-found");

        Console.WriteLine();
        /* ---- the contract ---- */
        var offered = new CarrierService.CarrierJob("K1", "260900800316", "EVONIK", "MAPTAPHUT", "1X40'", "LCB A0", "24,000", "TXGU8142057",
            "21/09/2026", "08:00", "READY", 17, 5800, new DateTimeOffset(2026, 9, 20, 3, 0, 0, TimeSpan.Zero),
            "", "", "", Category: "IMPORT", Booking: "LC2606594", PlanTime: "09:00");
        var accepted = new CarrierService.CarrierJob("K2", "260900800317", "L'OREAL", "WANGNOI", "1X40'", "", "", "TEMU5246902",
            "WAIT", "", "DELIVERED", null, null, null, "70-1234", "สมชาย ใจดี", "081-2345678",
            Category: "IMPORT", ArrDate: "20/09/2026", ArrTime: "10:20");
        var portal = new CarrierService.Portal(19, "SHORE", [offered], [accepted]);
        var rows = CarrierApiEndpoints.Assignments(portal).ToList();
        failed += Say("the portal's two lists are one contract, each row saying which group it is in",
            rows.Count == 2 && rows[0].Group == CarrierApi.Offered && rows[1].Group == CarrierApi.Accepted, true);
        failed += Say("an assignment's id is the job's register key — what accept, decline, LINE and the audit trail name it by", rows[0].Id, "K1");
        failed += Say("a readable date is given as yyyy-MM-dd beside the register's own text", rows[0].Date == "2026-09-21" && rows[0].DateText == "21/09/2026", true);
        failed += Say("a date the register cannot read is null beside its text, never guessed", rows[1].Date is null && rows[1].DateText == "WAIT", true);
        failed += Say("an offered row carries its request: id, quoted price, when", rows[0].Request is { Id: 17, QuotedPrice: 5800 }, true);
        failed += Say("an accepted row keyed by an operator carries no request", rows[1].Request, null);
        failed += Say("the truck's details ride on the row", rows[1].Truck.Licence == "70-1234" && rows[1].Truck.Driver == "สมชาย ใจดี", true);
        failed += Say("the arrival is on the row when it is written, and absent when it is not", rows[1].Arrival is { Date: "2026-09-20", Time: "10:20" } && rows[0].Arrival is null, true);
        var names = typeof(CarrierApiEndpoints.Assignment).GetProperties().Select(one => one.Name.ToLowerInvariant()).ToList();
        failed += Say("nothing on the contract names a rate, a cost, a selling price or another carrier",
            names.Any(one => one.Contains("rate") || one.Contains("cost") || one.Contains("sell") || one.Contains("margin") || one.Contains("trucker")), false);

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "All Carrier API checks passed." : $"{failed} Carrier API check(s) FAILED.");
        return failed == 0 ? 0 : 1;
    }

    private static int Say<T>(string why, T got, T want)
    {
        var ok = Equals(got, want);
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}{(ok ? "" : $"  (got {got}, wanted {want})")}");
        return ok ? 0 : 1;
    }
}
