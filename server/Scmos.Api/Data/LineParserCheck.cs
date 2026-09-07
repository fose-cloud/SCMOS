using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the LINE message parser against a fixed list of messages, with
/// <c>--check-line</c>.
///
/// <para>
/// Arithmetic and string rules only: no database, no network, no LINE account.
/// That is the point — the parser is the part of this integration that can be
/// got right before a single credential exists, and this is how it gets
/// checked.
/// </para>
///
/// <para>
/// <b>The messages below are not real yet.</b> They are the examples from the
/// specification plus the shapes the register makes likely. Real messages from
/// a vendor group have been asked for and have not arrived. When they do, they
/// belong here — a parser checked only against invented examples is one that
/// passes its own tests and fails on the first day.
/// </para>
/// </summary>
public static class LineParserCheck
{
    /// <summary>A fixed instant to resolve clock readings against — 07 Sep 2026, 11:00 Bangkok.</summary>
    private static readonly DateTimeOffset Received =
        new(2026, 9, 7, 11, 0, 0, TimeSpan.FromHours(7));

    private record Case(
        string Why,
        string Message,
        string? Job,
        string? Status,
        bool Delayed,
        bool Auto);

    private static readonly Case[] Cases =
    [
        /* ---- the specification's own examples ---- */
        new("an arrival with a time, written with a full stop",
            "260600800773 ถึงลูกค้าแล้ว 10.25", "260600800773", "DELIVERED", false, true),
        new("the same in English",
            "260600800773 arrived customer 10:25", "260600800773", "DELIVERED", false, true),
        new("traffic with an estimate",
            "260600800774 รถติดบางนา คาดถึง 14.30", "260600800774", null, true, true),
        new("a breakdown with an ETA",
            "260600800775 รถเสีย ETA 16:00", "260600800775", null, true, true),
        new("loading finished",
            "260600800776 โหลดเสร็จแล้ว", "260600800776", "PICKED_UP", false, true),
        new("on the way to the customer",
            "260600800777 กำลังไปลูกค้า", "260600800777", "IN_TRANSIT", false, true),

        /* ---- messages that must NOT be acted on ---- */
        new("a message with no job number waits for a person",
            "ถึงลูกค้าแล้ว 10.25", null, "DELIVERED", false, false),
        new("two job numbers is not a message to guess at",
            "260600800773 260600800774 ถึงลูกค้าแล้ว", null, "DELIVERED", false, false),
        new("a job number and nothing else understood",
            "260600800773 ครับ", "260600800773", null, false, false),
        new("an empty message",
            "", null, null, false, false),
        new("a placeholder is not a message",
            "-", null, null, false, false),

        /* ---- the distinctions that cost money if got wrong ---- */
        new("finishing the delivery is DELIVERED, never COMPLETED",
            "260600800773 ส่งเสร็จแล้ว", "260600800773", "DELIVERED", false, true),
        new("arriving at the port is not arriving at the customer",
            "260600800773 ถึงท่าแล้ว 08:10", "260600800773", "DISPATCHED", false, true),
        new("a delivery note is ten digits and is not a job number",
            "6317309804 ถึงลูกค้าแล้ว", null, "DELIVERED", false, false),
        new("nor is a SAP order",
            "8505076096 ถึงลูกค้าแล้ว", null, "DELIVERED", false, false),
        new("a longer run of digits does not yield a job number inside it",
            "2606008007731234 ถึงลูกค้าแล้ว", null, "DELIVERED", false, false),

        /* ---- delay classified through the register's own rules ---- */
        new("a truck problem is a truck problem",
            "260600800773 รถเสียกลางทาง", "260600800773", null, true, true),
        new("a document problem is one too",
            "260600800773 เอกสารไม่ครบ", "260600800773", null, true, true),
        new("a delay and a status together",
            "260600800773 รถติด กำลังไปลูกค้า", "260600800773", "IN_TRANSIT", true, true),
    ];

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-line")) return null;

        var failed = 0;

        Console.WriteLine("What the parser reads out of a vendor's message.");
        Console.WriteLine();
        foreach (var one in Cases)
        {
            var got = LineParser.Parse(one.Message, Received);
            var ok = got.JobNumber == one.Job
                && got.Status == one.Status
                && got.Delayed == one.Delayed
                && got.CanAutoProcess == one.Auto;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {one.Why}");
            if (!ok)
            {
                Console.WriteLine($"          message   {one.Message}");
                Console.WriteLine($"          want      job={one.Job ?? "-"} status={one.Status ?? "-"} "
                    + $"delayed={one.Delayed} auto={one.Auto}");
                Console.WriteLine($"          got       job={got.JobNumber ?? "-"} status={got.Status ?? "-"} "
                    + $"delayed={got.Delayed} auto={got.CanAutoProcess} confidence={got.Confidence}");
                if (got.Warnings.Count > 0)
                    Console.WriteLine($"          warnings  {string.Join(", ", got.Warnings)}");
            }
        }
        Console.WriteLine();

        /* ---------------------------------------------------- the clock */

        Console.WriteLine("A clock reading put on the right day, in Bangkok.");
        Console.WriteLine();
        var times = new (string Why, string Message, string Want)[]
        {
            ("a time earlier today is today", "260600800773 ถึงลูกค้าแล้ว 09:30", "2026-09-07T09:30:00+07:00"),
            ("an hour ahead is clock drift, not tomorrow", "260600800773 ถึงลูกค้าแล้ว 11:45", "2026-09-07T11:45:00+07:00"),
            // A driver writing 23:50 at 11:00 is reporting last night's trip.
            ("a time far ahead of the message is yesterday", "260600800773 ถึงลูกค้าแล้ว 23:50", "2026-09-06T23:50:00+07:00"),
        };
        foreach (var (why, message, want) in times)
        {
            var got = LineParser.Parse(message, Received).EventTime;
            var same = got is not null && got.Value.ToString("yyyy-MM-ddTHH:mm:sszzz") == want;
            if (!same) failed++;
            Console.WriteLine($"  {(same ? "ok  " : "FAIL")}  {why}");
            if (!same) Console.WriteLine($"          want {want}  got {got?.ToString("yyyy-MM-ddTHH:mm:sszzz") ?? "-"}");
        }
        Console.WriteLine();

        /* ------------------------------------- forecast against event */

        Console.WriteLine("A time that has happened, against one that has not.");
        Console.WriteLine();
        var arrival = LineParser.Parse("260600800773 ถึงลูกค้าแล้ว 10:25", Received);
        var forecast = LineParser.Parse("260600800773 รถติด คาดถึง 14:30", Received);

        var arrivalRight = arrival.EventTime is not null && arrival.Eta is null;
        if (!arrivalRight) failed++;
        Console.WriteLine($"  {(arrivalRight ? "ok  " : "FAIL")}  an arrival is an event time, not an estimate");

        var forecastRight = forecast.Eta is not null && forecast.EventTime is null;
        if (!forecastRight) failed++;
        Console.WriteLine($"  {(forecastRight ? "ok  " : "FAIL")}  an estimate is not written as an arrival");
        Console.WriteLine();

        /* --------------------------------------------- the delay rules */

        Console.WriteLine("Delay read through the register's own classifier, not a second table.");
        Console.WriteLine();
        var traffic = LineParser.Parse("260600800773 รถติด", Received);
        var trafficRight = traffic.DelayCategory == DelayCategory.Traffic;
        if (!trafficRight) failed++;
        Console.WriteLine($"  {(trafficRight ? "ok  " : "FAIL")}  รถติด is Traffic, the category the delay register counts");
        if (!trafficRight) Console.WriteLine($"          got {traffic.DelayCategory?.ToString() ?? "-"}");

        var truck = LineParser.Parse("260600800773 รถเสีย", Received);
        var truckRight = truck.DelayCategory == DelayCategory.Truck;
        if (!truckRight) failed++;
        Console.WriteLine($"  {(truckRight ? "ok  " : "FAIL")}  รถเสีย is Truck");
        if (!truckRight) Console.WriteLine($"          got {truck.DelayCategory?.ToString() ?? "-"}");

        // The classifier drops its own confidence when a reason fits two
        // categories. That has to reach the review queue rather than being
        // rounded away here.
        var both = LineParser.Parse("260600800773 รอเอกสารจากลูกค้า", Received);
        var bothRight = !both.CanAutoProcess;
        if (!bothRight) failed++;
        Console.WriteLine($"  {(bothRight ? "ok  " : "FAIL")}  a reason that fits two categories waits for a person");
        Console.WriteLine();

        /* -------------------------------------------- the safety rule */

        Console.WriteLine("Nothing uncertain updates a job on its own.");
        Console.WriteLine();
        foreach (var one in Cases.Where(one => !one.Auto))
        {
            var got = LineParser.Parse(one.Message, Received);
            var safe = !got.CanAutoProcess;
            if (!safe) failed++;
            Console.WriteLine($"  {(safe ? "ok  " : "FAIL")}  {one.Why}");
        }
        Console.WriteLine();

        /* ------------------------------------------ the signature */

        Console.WriteLine("Proving a delivery came from LINE.");
        Console.WriteLine();

        // A body with the awkward things in it: unicode, a quote, and the
        // whitespace a JSON round trip would change.
        const string secret = "not-a-real-secret-only-for-this-check";
        const string body = "{ \"events\": [ {\"type\":\"message\",\"message\":{\"text\":\"ถึงลูกค้าแล้ว \\\"10.25\\\"\"}} ] }";
        var good = LineSignature.Sign(secret, body);

        var signatureCases = new (string Why, bool Want, string? Secret, string? Header, string? Body)[]
        {
            ("a body signed with the channel secret is accepted", true, secret, good, body),
            ("a body altered by one character is not", false, secret, good, body + " "),
            ("nor is the same body under another secret", false, secret, LineSignature.Sign("other", body), body),
            // The one that matters most: an unset secret must never pass, or the
            // endpoint accepts anything the day somebody forgets an app setting.
            ("an unconfigured secret refuses everything", false, "", good, body),
            ("so does a null one", false, null, good, body),
            ("a missing header is refused", false, secret, null, body),
            ("an empty header is refused", false, secret, "", body),
            ("a header that is not base64 is refused, not thrown on", false, secret, "!!not base64!!", body),
            ("a base64 header of the wrong length is refused", false, secret, "YWJj", body),
            ("a null body is refused", false, secret, good, null),
        };

        foreach (var (why, want, aSecret, aHeader, aBody) in signatureCases)
        {
            var got = LineSignature.Verify(aSecret, aHeader, aBody);
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          want {want}, got {got}");
        }

        // Signing is deterministic, or a retry of the same delivery would be
        // rejected the second time.
        var twice = LineSignature.Sign(secret, body) == LineSignature.Sign(secret, body);
        if (!twice) failed++;
        Console.WriteLine($"  {(twice ? "ok  " : "FAIL")}  signing the same body twice gives the same signature");
        Console.WriteLine();

        /* ------------------------------------------ who may act on it */

        // The other half of the integration's correctness, kept in its own file
        // and run here so CI's --check-line covers both without a second entry
        // in the workflow's list.
        failed += LineAuthorityCheck.Run();

        Console.WriteLine(failed == 0
            ? "All LINE checks passed."
            : $"{failed} LINE check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
