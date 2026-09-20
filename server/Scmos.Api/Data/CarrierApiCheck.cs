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
        /* ---- phase 2: idempotency ---- */
        failed += Say("an Idempotency-Key is kept as sent when it is printable ASCII", CarrierApi.IdempotencyKeyOf("  tms-42:accept/J25  "), "tms-42:accept/J25");
        failed += Say("a missing key is no key", CarrierApi.IdempotencyKeyOf(null), null);
        failed += Say("a blank key is no key", CarrierApi.IdempotencyKeyOf("   "), null);
        failed += Say("a key with a space inside is no key", CarrierApi.IdempotencyKeyOf("a b"), null);
        failed += Say("a key over 128 characters is no key", CarrierApi.IdempotencyKeyOf(new string('k', 129)), null);
        failed += Say("128 exactly is a key", CarrierApi.IdempotencyKeyOf(new string('k', 128))?.Length, 128);
        failed += Say("a key with a non-ASCII character is no key", CarrierApi.IdempotencyKeyOf("คีย์"), null);
        var hashA = CarrierApi.RequestHash("post", "/api/carrier/v1/assignments/J25/accept", "{\"licence\":\"70-1234\"}");
        failed += Say("the request hash is 64 hex characters", hashA.Length == 64 && hashA.All(char.IsAsciiHexDigitLower), true);
        failed += Say("the same request hashes the same, whatever the case of the method", CarrierApi.RequestHash("POST", "/api/carrier/v1/assignments/J25/accept", "{\"licence\":\"70-1234\"}"), hashA);
        failed += Say("a different body is a different request", CarrierApi.RequestHash("POST", "/api/carrier/v1/assignments/J25/accept", "{\"licence\":\"70-1235\"}") == hashA, false);
        failed += Say("a different path is a different request", CarrierApi.RequestHash("POST", "/api/carrier/v1/assignments/J26/accept", "{\"licence\":\"70-1234\"}") == hashA, false);
        failed += Say("a reused key with another request is 422", CarrierApi.ProblemOf(CarrierApi.KeyReused).Status, 422);
        failed += Say("a request still in flight is 409", CarrierApi.ProblemOf(CarrierApi.InProgress).Status, 409);

        Console.WriteLine();
        /* ---- phase 2: the truck ---- */
        var (full, fullProblems, fullWarnings) = CarrierApi.ReadTruck(" 71-5111 ชบ. ", "เต๋า ใจเงิน", "085 089 2487", "temu 524690-2", " SL123 ", requireTruck: true);
        failed += Say("a plate with its province is a plate", fullProblems.Count == 0 && full.Licence == "71-5111 ชบ.", true);
        failed += Say("the phone is written back as the register writes one", full.Contact, "085-0892487");
        failed += Say("the container is normalised to four letters and seven digits", full.Container, "TEMU5246902");
        failed += Say("a genuine container draws no warning", fullWarnings.Count, 0);
        failed += Say("the seal is trimmed", full.Seal, "SL123");
        var (_, missing, _) = CarrierApi.ReadTruck("", "", "", null, null, requireTruck: true);
        failed += Say("an acceptance without plate, driver and number names all three", missing.Count, 3);
        var (partial, partialProblems, _) = CarrierApi.ReadTruck(null, null, "081-2345678", null, null, requireTruck: false);
        failed += Say("a truck update may send one cell", partialProblems.Count == 0 && partial.Contact == "081-2345678" && partial.Licence == "", true);
        failed += Say("an empty update is empty", CarrierApi.ReadTruck(null, null, null, null, null, requireTruck: false).Fields.IsEmpty, true);
        var (_, badPlate, _) = CarrierApi.ReadTruck("1500", "x", "081-2345678", null, null, requireTruck: true);
        failed += Say("digits with no dash are not a plate", badPlate.Count == 1 && badPlate[0].StartsWith("licence", StringComparison.Ordinal), true);
        var (_, badPhone, _) = CarrierApi.ReadTruck("70-1234", "x", "12345", null, null, requireTruck: true);
        failed += Say("five digits are not a phone number", badPhone.Count == 1 && badPhone[0].StartsWith("contact", StringComparison.Ordinal), true);
        var (_, badBox, _) = CarrierApi.ReadTruck(null, null, null, "TEMU12", null, requireTruck: false);
        failed += Say("a container of the wrong shape is refused", badBox.Count == 1 && badBox[0].StartsWith("container", StringComparison.Ordinal), true);
        var (typo, typoProblems, typoWarnings) = CarrierApi.ReadTruck(null, null, null, "TEMU7592765", null, requireTruck: false);
        failed += Say("a container whose check digit disagrees is written as sent, with a warning — the register may carry the same number",
            typoProblems.Count == 0 && typo.Container == "TEMU7592765" && typoWarnings.Count == 1, true);
        var (_, longSeal, _) = CarrierApi.ReadTruck(null, null, null, null, new string('s', 41), requireTruck: false);
        failed += Say("a seal over forty characters is refused", longSeal.Count, 1);

        Console.WriteLine();
        /* ---- phase 3: status events ---- */
        failed += Say("a type is read whatever its case or dashes", CarrierEvent.TryType(" In-Transit ", out var read1) && read1 == CarrierEvent.InTransit, true);
        failed += Say("a type the API does not know is refused", CarrierEvent.TryType("teleported", out _), false);
        failed += Say("'arrived' reports the site, for the job's category to settle", CarrierEvent.StatusOf(CarrierEvent.Arrived), LineParser.SiteArrival);
        failed += Say("'dispatched' is DISPATCHED, 'delivered' is DELIVERED", CarrierEvent.StatusOf(CarrierEvent.Dispatched) == JobStatus.Dispatched && CarrierEvent.StatusOf(CarrierEvent.Delivered) == JobStatus.Delivered, true);
        failed += Say("a note reports no status", CarrierEvent.StatusOf(CarrierEvent.Note), null);
        failed += Say("'arrived' resolves to DELIVERED on an import and DISPATCHED on an export, as a LINE message does",
            LineAuthority.ResolveSite("IMPORT", CarrierEvent.StatusOf(CarrierEvent.Arrived)) == JobStatus.Delivered
            && LineAuthority.ResolveSite("EXPORT", CarrierEvent.StatusOf(CarrierEvent.Arrived)) == JobStatus.Dispatched, true);
        var clock = new DateTimeOffset(2026, 9, 20, 3, 20, 0, TimeSpan.Zero);
        failed += Say("no 'at' is now", CarrierEvent.ReadAt(null, clock).At, clock);
        failed += Say("an ISO 8601 moment with its offset is read as that moment", CarrierEvent.ReadAt("2026-09-20T10:20:00+07:00", clock).At, clock);
        failed += Say("a moment in the future is refused", CarrierEvent.ReadAt("2026-09-20T11:00:00+07:00", clock).Problem is not null, true);
        failed += Say("a moment eight days ago is refused", CarrierEvent.ReadAt("2026-09-12T10:00:00+07:00", clock).Problem is not null, true);
        failed += Say("a moment that is not a moment is refused", CarrierEvent.ReadAt("this morning", clock).Problem is not null, true);
        var payload = new CarrierEvent.Payload("ck_abc", "SHORE TMS", 19, "SHORE", "J25", CarrierEvent.Arrived,
            new DateTimeOffset(2026, 9, 20, 3, 20, 0, TimeSpan.Zero), "หน้าโรงงาน", "corr-1");
        var back = CarrierEvent.Payload.Read(payload.ToJson());
        failed += Say("the payload survives the round trip through the row", back, payload);
        failed += Say("a payload that will not read is null, not an exception", CarrierEvent.Payload.Read("{not json"), null);
        var reading = payload.AsParsed();
        failed += Say("an 'arrived' event reads as the site with the arrival clock, in Bangkok", reading.Status == LineParser.SiteArrival && reading.ArrivalTime?.ToString("HH:mm zzz") == "10:20 +07:00", true);
        failed += Say("a 'dispatched' event carries no arrival clock", (payload with { Type = CarrierEvent.Dispatched }).AsParsed().ArrivalTime, null);
        failed += Say("a note reads as no status", (payload with { Type = CarrierEvent.Note }).AsParsed().Status, null);
        failed += Say("the event names its rule", reading.MatchedRules.SequenceEqual(["carrier-api:arrived"]), true);
        failed += Say("the queue's line says who, what and when, Bangkok", payload.Text(), "TMS SHORE: ถึงโรงงาน 20/09/2026 10:20 · หน้าโรงงาน");
        failed += Say("the room's place is taken by the supplier and the credential", payload.GroupLabel, "SHORE · SHORE TMS");
        failed += Say("a queued row reads as queued, an applied one as applied, a refused one by its reason",
            CarrierEvent.StateOf(LineProcessing.NeedReview, "ready-to-apply") == "queued"
            && CarrierEvent.StateOf(LineProcessing.Processed, "") == "applied"
            && CarrierEvent.StateOf(LineProcessing.Processed, LineRemark.Written) == "remark-written"
            && CarrierEvent.StateOf(LineProcessing.Ignored, "backwards") == "backwards", true);
        failed += Say("a TMS event judged by the rule: forward on the ladder applies, the same rung is already there, backwards is refused",
            LineAuthority.Move(new LineAuthority.JobCandidate("J", "IMPORT", "SHORE", "SUPPLIER_CONFIRMED"), CarrierEvent.StatusOf(CarrierEvent.Dispatched)).Applies
            && LineAuthority.Move(new LineAuthority.JobCandidate("J", "IMPORT", "SHORE", "DISPATCHED"), CarrierEvent.StatusOf(CarrierEvent.Dispatched)).Result == LineAuthority.Outcome.AlreadyThere
            && LineAuthority.Move(new LineAuthority.JobCandidate("J", "IMPORT", "SHORE", "DELIVERED"), CarrierEvent.StatusOf(CarrierEvent.Dispatched)).Result == LineAuthority.Outcome.Backwards, true);

        Console.WriteLine();
        /* ---- phase 4: webhooks ---- */
        failed += Say("a public https URL is a webhook", CarrierWebhooks.UrlProblem("https://tms.example.com/scmos/hook"), null);
        failed += Say("http is not, outside a developer's machine", CarrierWebhooks.UrlProblem("http://tms.example.com/hook") is not null, true);
        failed += Say("http is allowed where insecure is allowed", CarrierWebhooks.UrlProblem("http://localhost:9999/hook", allowInsecure: true), null);
        failed += Say("localhost is refused", CarrierWebhooks.UrlProblem("https://localhost/hook") is not null, true);
        failed += Say("a bare host with no dot is refused", CarrierWebhooks.UrlProblem("https://scmos-api-3936/hook") is not null, true);
        failed += Say("a private address is refused", CarrierWebhooks.UrlProblem("https://10.0.0.5/hook") is not null && CarrierWebhooks.UrlProblem("https://192.168.1.4/hook") is not null && CarrierWebhooks.UrlProblem("https://172.16.0.9/hook") is not null, true);
        failed += Say("a private address is refused even where insecure is allowed; the loopback is allowed there", CarrierWebhooks.UrlProblem("http://10.0.0.5/hook", allowInsecure: true) is not null && CarrierWebhooks.UrlProblem("http://127.0.0.1:9999/hook", allowInsecure: true) is null, true);
        failed += Say("the loopback and the metadata addresses are refused", CarrierWebhooks.UrlProblem("https://127.0.0.1/hook") is not null && CarrierWebhooks.UrlProblem("https://169.254.169.254/latest") is not null, true);
        failed += Say("credentials in the URL are refused", CarrierWebhooks.UrlProblem("https://user:pw@tms.example.com/hook") is not null, true);
        failed += Say("a relative URL is refused", CarrierWebhooks.UrlProblem("/hook") is not null, true);
        failed += Say("an empty URL is refused", CarrierWebhooks.UrlProblem("") is not null, true);
        failed += Say("events are read case-forgiving and without duplicates", CarrierWebhooks.ReadEvents(["Assignment.Offered", "assignment.offered", "event.decided"])?.SequenceEqual(["assignment.offered", "event.decided"]), true);
        failed += Say("no events is all three", CarrierWebhooks.ReadEvents([])?.SequenceEqual(CarrierWebhooks.Types), true);
        failed += Say("an event the API does not send is refused", CarrierWebhooks.ReadEvents(["assignment.paid"]), null);
        failed += Say("a webhook wants what it subscribed to, and every webhook wants a ping",
            CarrierWebhooks.Wants("assignment.offered,event.decided", CarrierWebhooks.EventDecided)
            && !CarrierWebhooks.Wants("assignment.offered", CarrierWebhooks.Cancelled)
            && CarrierWebhooks.Wants("assignment.offered", CarrierWebhooks.Ping), true);
        var secret = CarrierWebhooks.NewSecret();
        failed += Say("a secret carries its prefix and 32 bytes", secret.StartsWith(CarrierWebhooks.SecretPrefix, StringComparison.Ordinal) && secret.Length == CarrierWebhooks.SecretPrefix.Length + 43, true);
        var signature = CarrierWebhooks.Signature(secret, "1758340800", "{\"id\":1}");
        failed += Say("a signature is sha256= and 64 hex characters", signature.StartsWith("sha256=", StringComparison.Ordinal) && signature.Length == 7 + 64, true);
        failed += Say("the same secret, timestamp and body verify", CarrierWebhooks.Verify(secret, "1758340800", "{\"id\":1}", signature), true);
        failed += Say("another timestamp does not — a captured delivery cannot be replayed as fresh", CarrierWebhooks.Verify(secret, "1758340801", "{\"id\":1}", signature), false);
        failed += Say("another body does not", CarrierWebhooks.Verify(secret, "1758340800", "{\"id\":2}", signature), false);
        failed += Say("another secret does not", CarrierWebhooks.Verify(CarrierWebhooks.NewSecret(), "1758340800", "{\"id\":1}", signature), false);
        failed += Say("a missing signature does not", CarrierWebhooks.Verify(secret, "1758340800", "{\"id\":1}", null), false);
        failed += Say("the known vector: HMAC-SHA256 of 'ts.body' under 'whsec_test'",
            CarrierWebhooks.Signature("whsec_test", "1", "{}"), "sha256=" + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes("whsec_test"), System.Text.Encoding.UTF8.GetBytes("1.{}"))).ToLowerInvariant());
        var t0 = new DateTimeOffset(2026, 9, 20, 3, 0, 0, TimeSpan.Zero);
        failed += Say("the first retry is a minute on, the second five, the third half an hour",
            CarrierWebhooks.NextAttempt(1, t0) == t0.AddMinutes(1) && CarrierWebhooks.NextAttempt(2, t0) == t0.AddMinutes(5) && CarrierWebhooks.NextAttempt(3, t0) == t0.AddMinutes(30), true);
        failed += Say("the fourth two hours, the fifth twelve, and then it is dead",
            CarrierWebhooks.NextAttempt(4, t0) == t0.AddHours(2) && CarrierWebhooks.NextAttempt(5, t0) == t0.AddHours(12) && CarrierWebhooks.NextAttempt(6, t0) is null, true);
        failed += Say("six attempts in all", CarrierWebhooks.MaxAttempts, 6);
        failed += Say("a 2xx is delivered; a 3xx, 4xx or 5xx is not", CarrierWebhooks.IsDelivered(200) && CarrierWebhooks.IsDelivered(204) && !CarrierWebhooks.IsDelivered(302) && !CarrierWebhooks.IsDelivered(404) && !CarrierWebhooks.IsDelivered(500), true);

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
