using Scmos.Api.Rules;
using Scmos.Api.Services;

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
/// <b>Most of the messages below are the specification's, not a haulier's.</b>
/// The first real one arrived on 16 Sep 2026 and is the first case under "as
/// the hauliers actually write": a customer, a container, a driver, a plate,
/// an arrival clock and "ลงเสร็จ" — and no job number at all. Every real
/// message that shows the parser something new belongs here beside it; a
/// parser checked only against invented examples is one that passes its own
/// tests and fails on the first day.
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

        /* ---- as the hauliers actually write, 16 Sep 2026 ---- */
        new("the first real message: customer, container, driver, plate, arrival, unloaded — no job number",
            "L'Oréal / TEMU7592765 / สมใจ / 700-3232 / ถึงโรงงาน 05:00 / ลงเสร็จ", null, "DELIVERED", false, true),
        new("a container is as good a reference as a number",
            "TEMU7592765 ลงเสร็จ 09:40", null, "DELIVERED", false, true),
        new("a container written with a space is the same container",
            "TEMU 7592765 ลงเสร็จ", null, "DELIVERED", false, true),
        new("at the site, with nothing after it, is left for the job's category to settle",
            "TEMU7592765 ถึงโรงงาน 05:00", null, LineParser.SiteArrival, false, true),
        new("the later of two statuses is the one reported",
            "260600800773 ออกจากท่าแล้ว กำลังไปลูกค้า", "260600800773", "IN_TRANSIT", false, true),
        new("a plate alone finds the truck's trips, and is never enough on its own",
            "700-3232 ลงเสร็จ", null, "DELIVERED", false, false),
        new("two containers is two trips, not a guess",
            "TEMU7592765 MSKU1234567 ลงเสร็จ", null, "DELIVERED", false, false),
        new("a seal number is not a container",
            "SEAL 1234567 ลงเสร็จ", null, "DELIVERED", false, false),
        new("a date with dashes holds no plate",
            "16-09-2026 ลงเสร็จ", null, "DELIVERED", false, false),
        new("the second real message asks when, and reports nothing",
            "AKZO NOBEL // LC2606594 16/09/2026 -- 14:00 ถึงโรงงานที่โมงคะ @Vad", null, null, false, false),
        new("a booking alone is a reference, and with a report it goes to be matched",
            "AKZO NOBEL // LC2606594 ถึงโรงงาน 14:20", null, LineParser.SiteArrival, false, false),
        new("the answer to the morning reminder: a job, a plate, a name, a number, and no status",
            "260600800773 70-1234 สมชาย ใจดี 081-2345678", "260600800773", null, false, false),

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
        // An estimate looks the other way: "คาดถึง 14:30" at 11:00 is this afternoon.
        var forecasts = new (string Why, string Message, string Want)[]
        {
            ("an estimate this afternoon is today, not yesterday", "260600800773 รถติด คาดถึง 14:30", "2026-09-07T14:30:00+07:00"),
            ("an estimate an hour and a half ahead likewise", "EXFU5627436 ประมาณ 12.20 รถถึงโรงงาน", "2026-09-07T12:20:00+07:00"),
            ("an estimate already gone by more than an hour is tomorrow's", "260600800773 คาดถึง 01:00", "2026-09-08T01:00:00+07:00"),
        };
        foreach (var (why, message, want) in forecasts)
        {
            var got = LineParser.Parse(message, Received).Eta;
            var same = got is not null && got.Value.ToString("yyyy-MM-ddTHH:mm:sszzz") == want;
            if (!same) failed++;
            Console.WriteLine($"  {(same ? "ok  " : "FAIL")}  {why}");
            if (!same) Console.WriteLine($"          want {want}  got {got?.ToString("yyyy-MM-ddTHH:mm:sszzz") ?? "-"}");
        }
        foreach (var (why, message, want) in times)
        {
            var got = LineParser.Parse(message, Received).EventTime;
            var same = got is not null && got.Value.ToString("yyyy-MM-ddTHH:mm:sszzz") == want;
            if (!same) failed++;
            Console.WriteLine($"  {(same ? "ok  " : "FAIL")}  {why}");
            if (!same) Console.WriteLine($"          want {want}  got {got?.ToString("yyyy-MM-ddTHH:mm:sszzz") ?? "-"}");
        }
        Console.WriteLine();

        /* ------------------------------------------- what else it reads */

        Console.WriteLine("The box, the truck and the arrival, out of a message with no number in it.");
        Console.WriteLine();
        var real = LineParser.Parse("L'Oréal / TEMU7592765 / สมใจ / 700-3232 / ถึงโรงงาน 05:00 / ลงเสร็จ", Received);
        var reads = new (string Why, bool Ok, string Got)[]
        {
            ("the container", real.Container == "TEMU7592765", real.Container ?? "-"),
            ("the plate", real.Plates is ["700-3232"], string.Join("/", real.Plates ?? [])),
            ("the arrival, on the day the message came, in Bangkok",
                real.ArrivalTime?.ToString("yyyy-MM-ddTHH:mm:sszzz") == "2026-09-07T05:00:00+07:00",
                real.ArrivalTime?.ToString("yyyy-MM-ddTHH:mm:sszzz") ?? "-"),
            ("every status word, for the reviewer",
                real.MatchedRules.Contains("status:ถึงโรงงาน") && real.MatchedRules.Contains("status:ลงเสร็จ"),
                string.Join(", ", real.MatchedRules)),
            ("a tractor and its trailer are two plates and no warning",
                LineParser.Parse("75-4384 ชบ. / 75-4385 ชบ. ถึงโรงงาน", Received) is { Plates: ["75-4384", "75-4385"], Warnings.Count: 0 },
                string.Join("/", LineParser.Parse("75-4384 ชบ. / 75-4385 ชบ. ถึงโรงงาน", Received).Plates ?? [])),
            ("a Thai-lettered plate", LineParser.Parse("กข 1234 ถึงโรงงาน", Received).Plates is ["กข 1234"],
                string.Join("/", LineParser.Parse("กข 1234 ถึงโรงงาน", Received).Plates ?? [])),
            ("a plate with its province is the plate", LineParser.PlateKey("700-3232 กทม.") == "7003232"
                && LineParser.PlateKey("700 3232") == "7003232", LineParser.PlateKey("700-3232 กทม.")),
            ("a phone number is not a plate", LineParser.Parse("โทร 081-2345678 ลงเสร็จ", Received).Plates?.Count == 0,
                string.Join("/", LineParser.Parse("โทร 081-2345678 ลงเสร็จ", Received).Plates ?? [])),
            ("the clock after the port is not the arrival",
                LineParser.Parse("260600800773 ถึงท่าแล้ว 08:10", Received).ArrivalTime is null, "-"),
            ("an estimate is not the arrival",
                LineParser.Parse("260600800773 คาดถึง 14:30", Received).ArrivalTime is null, "-"),
            ("the clock after the customer is",
                LineParser.Parse("260600800773 ถึงลูกค้าแล้ว 10.25", Received).ArrivalTime?.ToString("HH:mm") == "10:25", "-"),
            ("the booking out of the second real message, and nothing else",
                LineParser.Parse("AKZO NOBEL // LC2606594 16/09/2026 -- 14:00 ถึงโรงงานที่โมงคะ @Vad", Received).References is ["LC2606594"],
                string.Join("/", LineParser.Parse("AKZO NOBEL // LC2606594 16/09/2026 -- 14:00 ถึงโรงงานที่โมงคะ @Vad", Received).References ?? [])),
            ("a question is marked as one, and its arrival clock is not an arrival",
                LineParser.Parse("AKZO NOBEL // LC2606594 ถึงโรงงานที่โมงคะ 14:00", Received) is { Question: true, ArrivalTime: null, Status: null }, "-"),
            ("a job number and a container are not also references",
                LineParser.Parse("260600800773 TEMU5246902 ลงเสร็จ", Received).References?.Count == 0,
                string.Join("/", LineParser.Parse("260600800773 TEMU5246902 ลงเสร็จ", Received).References ?? [])),
            ("a D-code and a delivery note are references",
                LineParser.Parse("D15441000 / 6317309804 ลงเสร็จ", Received).References is ["D15441000", "6317309804"],
                string.Join("/", LineParser.Parse("D15441000 / 6317309804 ลงเสร็จ", Received).References ?? [])),
            ("a phone number is not a reference, with or without its dash",
                LineParser.Parse("โทร 081-2345678 ลงเสร็จ", Received).References?.Count == 0
                    && LineParser.Parse("260600800773 70-5678 สมชาย 0812345678", Received).References?.Count == 0,
                string.Join("/", LineParser.Parse("260600800773 70-5678 สมชาย 0812345678", Received).References ?? [])),
            ("the truck out of the reminder's answer: plate, name, number",
                LineParser.Parse("260600800773 70-1234 สมชาย ใจดี 081-2345678", Received) is { Plates: ["70-1234"], Driver: "สมชาย ใจดี", Phone: "081-2345678" },
                $"{string.Join("/", LineParser.Parse("260600800773 70-1234 สมชาย ใจดี 081-2345678", Received).Plates ?? [])} {LineParser.Parse("260600800773 70-1234 สมชาย ใจดี 081-2345678", Received).Driver} {LineParser.Parse("260600800773 70-1234 สมชาย ใจดี 081-2345678", Received).Phone}"),
            ("the labels and the titles are not the name",
                LineParser.Parse("LC2606594 ทะเบียน 70-1234 คนขับ นาย สมชาย ใจดี โทร 0812345678 ครับ", Received) is { Driver: "สมชาย ใจดี", Phone: "081-2345678" },
                $"{LineParser.Parse("LC2606594 ทะเบียน 70-1234 คนขับ นาย สมชาย ใจดี โทร 0812345678 ครับ", Received).Driver} {LineParser.Parse("LC2606594 ทะเบียน 70-1234 คนขับ นาย สมชาย ใจดี โทร 0812345678 ครับ", Received).Phone}"),
            ("a name with no plate and no number is not a truck",
                LineParser.Parse("260600800773 สมชาย ใจดี", Received).Driver is null, "-"),
            ("a status report with a plate reads the plate and no name out of the status words",
                LineParser.Parse("260600800773 70-1234 ถึงลูกค้าแล้ว 10.25", Received) is { Driver: null, Plates: ["70-1234"], Status: "DELIVERED" }, "-"),
            ("such a message is understood, and would be matched",
                LineParser.Parse("260600800773 70-1234 สมชาย ใจดี 081-2345678", Received) is { Warnings.Count: 0, HasDetails: true }, "-"),

            /* ---- 17 Sep 2026: ถึงโรงงาน means ถึงแล้ว, and a room's small talk is left alone ---- */

            ("\"ถึงแล้ว\" on its own is the site, like ถึงโรงงาน",
                LineParser.Parse("TXGU8142057 ถึงแล้วครับ", Received).Status == LineParser.SiteArrival,
                LineParser.Parse("TXGU8142057 ถึงแล้วครับ", Received).Status ?? "-"),
            ("ถึงโรงงาน with no clock is an arrival at the minute the message was sent, and says so",
                LineParser.Parse("TXGU8142057 ถึงโรงงาน", Received) is { ArrivalAtSend: true, Status: LineParser.SiteArrival } at
                    && at.ArrivalTime?.ToString("yyyy-MM-ddTHH:mm:sszzz") == "2026-09-07T11:00:00+07:00"
                    && at.MatchedRules.Contains("arrival-at-send:11:00"),
                LineParser.Parse("TXGU8142057 ถึงโรงงาน", Received).ArrivalTime?.ToString("yyyy-MM-ddTHH:mm:sszzz") ?? "-"),
            ("the send time is taken to the minute, whatever the second and the zone",
                LineParser.Parse("TXGU8142057 ถึงลูกค้าแล้ว", new DateTimeOffset(2026, 9, 7, 4, 0, 42, TimeSpan.Zero)).ArrivalTime?.ToString("yyyy-MM-ddTHH:mm:sszzz") == "2026-09-07T11:00:00+07:00",
                LineParser.Parse("TXGU8142057 ถึงลูกค้าแล้ว", new DateTimeOffset(2026, 9, 7, 4, 0, 42, TimeSpan.Zero)).ArrivalTime?.ToString("yyyy-MM-ddTHH:mm:sszzz") ?? "-"),
            ("a clock the driver wrote still wins over the send time",
                LineParser.Parse("TXGU8142057 ถึงโรงงาน 12:40", Received) is { ArrivalAtSend: false } clocked && clocked.ArrivalTime?.ToString("HH:mm") == "12:40",
                LineParser.Parse("TXGU8142057 ถึงโรงงาน 12:40", Received).ArrivalTime?.ToString("HH:mm") ?? "-"),
            ("after ลงเสร็จ the arrival was earlier and is not the send time",
                LineParser.Parse("TXGU8142057 ถึงโรงงาน ลงเสร็จ", Received) is { ArrivalTime: null, Status: "DELIVERED" }, "-"),
            ("nor is the port, nor an estimate",
                LineParser.Parse("TXGU8142057 ถึงท่าแล้ว", Received).ArrivalTime is null
                    && LineParser.Parse("TXGU8142057 ถึงโรงงาน ประมาณ 14:00", Received).ArrivalTime is null, "-"),
            ("a clock elsewhere in the message is a time the driver gave, not one to replace",
                LineParser.Parse("10:30 TXGU8142057 ถึงโรงงาน", Received) is { ArrivalTime: null, ArrivalAtSend: false }, "-"),
            ("\"จะถึงโรงงานแล้ว\" is the truck about to arrive, and reports nothing",
                LineParser.Parse("TXGU8142057 จะถึงโรงงานแล้ว", Received) is { Status: null, ArrivalTime: null },
                LineParser.Parse("TXGU8142057 จะถึงโรงงานแล้ว", Received).Status ?? "-"),
            ("\"ยังไม่ถึง\" likewise",
                LineParser.Parse("TXGU8142057 ยังไม่ถึงโรงงาน รถติด", Received) is { Status: null, Delayed: true }, "-"),
            // The third real message, 17 Sep 2026 — approved as DELIVERED while the truck was on the road.
            ("an arrival named with an estimate is when it will happen: on the road, due at ten, not delivered",
                LineParser.Parse("EXFU5627436 พขร. เดินทางอยู่ บริเวณเส้น อ. คลองหลวงค่ะ ประมาณ 10.00 รถถึงโรงงานค่ะ", Received)
                    is { Status: "IN_TRANSIT", ArrivalTime: null, Container: "EXFU5627436" } road
                    && road.Eta?.ToString("HH:mm") == "10:00" && road.MatchedRules.Contains("arrival-is-estimate"),
                LineParser.Parse("EXFU5627436 พขร. เดินทางอยู่ บริเวณเส้น อ. คลองหลวงค่ะ ประมาณ 10.00 รถถึงโรงงานค่ะ", Received).Status ?? "-"),
            ("with no other status word, such a message reports no status at all",
                LineParser.Parse("EXFU5627436 ประมาณ 10.00 รถถึงโรงงานค่ะ", Received) is { Status: null, ArrivalTime: null, Warnings.Count: 0 } eta
                    && eta.Eta?.ToString("HH:mm") == "10:00", "-"),
            ("but an arrival said as done, with a rough clock, is a report",
                LineParser.Parse("EXFU5627436 ถึงโรงงานแล้ว ประมาณ 10.25", Received).Status == LineParser.SiteArrival,
                LineParser.Parse("EXFU5627436 ถึงโรงงานแล้ว ประมาณ 10.25", Received).Status ?? "-"),
            ("the truck about to arrive is not the driver's name",
                LineParser.Parse("TXGU8142057 70-1234 จะถึงโรงงานแล้วครับ", Received).Driver is null,
                LineParser.Parse("TXGU8142057 70-1234 จะถึงโรงงานแล้วครับ", Received).Driver ?? "-"),
            ("arrived at the pickup with a clock is not the arrival",
                LineParser.Parse("260600800773 arrived pickup 08:10", Received).ArrivalTime is null, "-"),
            ("a greeting is about no job",
                !LineParser.Parse("สวัสดีครับ", Received).AboutAJob, "-"),
            ("nor is an acknowledgement",
                !LineParser.Parse("รับทราบครับ", Received).AboutAJob && !LineParser.Parse("ok", Received).AboutAJob, "-"),
            ("a bare clock is not either",
                !LineParser.Parse("นัด 14:00 นะครับ", Received).AboutAJob, "-"),
            ("but a status with no number is — it is a report that needs its job",
                LineParser.Parse("ถึงโรงงานแล้วครับ", Received).AboutAJob, "-"),
            ("a delay word, an estimate or a phone number on their own are not",
                !LineParser.Parse("รถติดบางนา", Received).AboutAJob
                    && !LineParser.Parse("คาดถึง 14:00 ค่ะ", Received).AboutAJob
                    && !LineParser.Parse("โทร 081-2345678", Received).AboutAJob, "-"),
            // The room arranging tomorrow's loading, 17 Sep 2026: "รอลูกค้า" read as a customer delay and drew a reply.
            ("nor is the room arranging its day",
                !LineParser.Parse("รับวันนี้ได้รับเลยค่ะ ส่วนเรื่องคืนตู้รอลูกค้าขอ EARLY OPENGATE อีกทีค่ะ @จัดส่ง DCH", Received).AboutAJob
                    && !LineParser.Parse("@POMPAM พี่รบกวน งานโหลดวันที่ 18/9/26 ให้หน่อยค่ะ ตรงกันไหมค่ะ", Received).AboutAJob, "-"),

            // The fourth real message, 17 Sep 2026.
            ("\"3 ตู้ อยู่โรงงาน\": at the plant is the site, three boxes, the send time as the arrival, and no driver called ตู้",
                LineParser.Parse("CATALITE 260900760321 3 ตู้ อยู่โรงงาน", Received) is { JobNumber: "260900760321", Status: LineParser.SiteArrival, BoxCount: 3, ArrivalAtSend: true, Driver: null } boxes
                    && boxes.ArrivalTime?.ToString("HH:mm") == "11:00" && boxes.MatchedRules.Contains("boxes:3"),
                string.Join(", ", LineParser.Parse("CATALITE 260900760321 3 ตู้ อยู่โรงงาน", Received).MatchedRules)),
            ("\"ตู้ที่ 2\" names a box and is not a count; a time is not one either",
                LineParser.Parse("260900760321 ตู้ที่ 2 ถึงโรงงาน", Received).BoxCount is null
                && LineParser.Parse("260900760321 ถึงโรงงาน 10:30 ตู้", Received).BoxCount is null, "-"),
            ("an export's box and seal, for the job its booking names",
                LineParser.Parse("LC2606594 ตู้ TXGU8142057 ซีล 123456", Received) is { Container: "TXGU8142057", SealNumber: "123456", HasDetails: true, Status: null, Warnings.Count: 0 }
                    && LineParser.Parse("LC2606594 TXGU8142057 SEAL NO. TH0012345", Received).SealNumber == "TH0012345",
                LineParser.Parse("LC2606594 ตู้ TXGU8142057 ซีล 123456", Received).SealNumber ?? "-"),
            ("a container on its own is the reference, not a detail",
                !LineParser.Parse("TXGU8142057", Received).HasDetails, "-"),
            ("the seal is not the driver's name",
                LineParser.Parse("LC2606594 70-1234 ซีล 123456", Received) is { Driver: null, SealNumber: "123456" }, "-"),
            ("and so is a plate, a booking or a seal",
                LineParser.Parse("70-1234 ครับ", Received).AboutAJob
                    && LineParser.Parse("LC2606594", Received).AboutAJob
                    && LineParser.Parse("ซีล 123456", Received).AboutAJob, "-"),
        };
        foreach (var (why, ok, got) in reads)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          got {got}");
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

        /* ------------------------------------------ a photographed box */

        Console.WriteLine("A container number read off a photo is trusted only when its check digit agrees.");
        Console.WriteLine();
        var digits = new (string Why, bool Want, bool Got)[]
        {
            // The ISO 6346 worked example, and six real boxes off the register.
            ("the standard's own example", true, ContainerNumbers.IsValid("CSQU3054383")),
            ("a real box: TEMU5246902", true, ContainerNumbers.IsValid("TEMU5246902")),
            ("a real box: FSCU5037629", true, ContainerNumbers.IsValid("FSCU5037629")),
            ("a real box: SEKU9220400 — a check digit of ten is written 0", true, ContainerNumbers.IsValid("SEKU9220400")),
            ("a real box: GNCU8801516", true, ContainerNumbers.IsValid("GNCU8801516")),
            ("one digit misread fails", false, ContainerNumbers.IsValid("TEMU5246903")),
            ("two digits swapped fail", false, ContainerNumbers.IsValid("TEMU5264902")),
            ("a letter misread fails", false, ContainerNumbers.IsValid("TEMV5246902")),
            ("the haulier's typed TEMU7592765 does not pass — and is only noted, never refused", false, ContainerNumbers.IsValid("TEMU7592765")),
            ("a seal number is not shaped like a box", false, ContainerNumbers.IsValid("SEAL1234567")),
            ("lower case with a space is normalised first", true, ContainerNumbers.IsValid(ContainerNumbers.Normalise("temu 524690-2"))),
        };
        foreach (var (why, want, got) in digits)
        {
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        var answers = new (string Why, string Answer, string[] Valid, string[] Rejected)[]
        {
            ("a clean reading", "{\"containers\":[\"TEMU5246902\"],\"note\":\"a 40ft box on a trailer\"}", ["TEMU5246902"], []),
            ("a misread is kept aside, not offered", "{\"containers\":[\"TEMU5246903\"],\"note\":\"\"}", [], ["TEMU5246903"]),
            ("the same box read twice is one box", "{\"containers\":[\"TEMU5246902\",\"temu 5246902\"],\"note\":\"\"}", ["TEMU5246902"], []),
            ("two boxes in one photo are two", "{\"containers\":[\"TEMU5246902\",\"FSCU5037629\"],\"note\":\"\"}", ["TEMU5246902", "FSCU5037629"], []),
            ("no box in the photo", "{\"containers\":[],\"note\":\"a delivery note\"}", [], []),
            // The door that taught this, 17 Sep 2026: "GCXU 513490 [0]" over "45G1".
            ("the framed check digit is part of the number", "{\"containers\":[\"GCXU 513490 0\"],\"note\":\"\"}", ["GCXU5134900"], []),
            ("the size and type code under it is not", "{\"containers\":[\"GCXU 513490 0 45G1\"],\"note\":\"\"}", ["GCXU5134900"], []),
            ("a reading that stopped at the frame is a fragment, kept aside for the register to complete",
                "{\"containers\":[\"GCXU513490 45G1\"],\"note\":\"\"}", [], ["GCXU513490"]),
            ("a fragment on its own likewise", "{\"containers\":[\"GCXU513490\"],\"note\":\"\"}", [], ["GCXU513490"]),
            ("an answer that is not JSON reads as nothing, and does not throw", "not json", [], []),
            ("an empty answer likewise", "", [], []),
        };
        var noted = LineImageReading.RejectedIn("a 40ft box — เลขที่อ่านได้แต่ check digit ไม่ผ่าน: TEMU7592765, SEAL1234567, GCXU513490");
        var notedRight = noted is ["TEMU7592765", "GCXU513490"];
        if (!notedRight) failed++;
        Console.WriteLine($"  {(notedRight ? "ok  " : "FAIL")}  a set-aside number is read back off the row's note, and only a number");
        var unnoted = LineImageReading.RejectedIn("a delivery note").Count == 0;
        if (!unnoted) failed++;
        Console.WriteLine($"  {(unnoted ? "ok  " : "FAIL")}  a note with nothing set aside yields nothing");
        var completed = ContainerNumbers.Find("GCXU 513490-0", "GCXU513490") == "GCXU5134900"
            && ContainerNumbers.Find("รถ 70-1234 GCXU 513490-0 ถึงโรงงาน 05:00", "GCXU513490") == "GCXU5134900"
            && ContainerNumbers.Find("TEMU7592765", "GCXU513490") is null
            && ContainerNumbers.Find("GCXU5134900", "GCXU5134900") == "GCXU5134900";
        if (!completed) failed++;
        Console.WriteLine($"  {(completed ? "ok  " : "FAIL")}  a fragment is completed from a cell or a message that carries the whole number, and from nothing else");
        var waiting = LineAuthority.AwaitingArrival("IMPORT", "IN_TRANSIT", "", "")
            && !LineAuthority.AwaitingArrival("IMPORT", "IN_TRANSIT", "17/09/2026", "10:23")
            && !LineAuthority.AwaitingArrival("IMPORT", "DELIVERED", "", "")
            && LineAuthority.AwaitingArrival("EXPORT", "READY", "", "")
            && !LineAuthority.AwaitingArrival("EXPORT", "DISPATCHED", "", "")
            && !LineAuthority.AwaitingArrival("IMPORT", "CANCELLED", "", "");
        if (!waiting) failed++;
        Console.WriteLine($"  {(waiting ? "ok  " : "FAIL")}  a job is waiting for its truck until a stamp or the site rung, and a closed one never is");
        foreach (var (why, answer, valid, rejected) in answers)
        {
            var got = LineImageReading.Read(answer);
            var ok = got.Valid.SequenceEqual(valid) && got.Rejected.SequenceEqual(rejected);
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          valid [{string.Join(",", got.Valid)}] rejected [{string.Join(",", got.Rejected)}] note {got.Note}");
        }
        Console.WriteLine();

        /* ------------------------------------------ the morning reminder */

        Console.WriteLine("The morning message names each open job the way the department asked, and only the jobs short of a truck.");
        Console.WriteLine();
        var day = new DateOnly(2026, 9, 16);
        LineReminder.JobLine Line(string key, string cat, string status, string licence = "", string driver = "", string contact = "") =>
            new(key, cat, status, "L'OREAL", "260600800773", "LC2606594", "TEMU5246902",
                "โรงงานบางปะกง", "YUSEN W/H", "LCB A0", "14:00", licence, driver, contact, JobNo: "D15441000", Warehouse: "JWD", Province: "สมุทรปราการ 10130");
        var composed = LineReminder.Compose("SHORE", day, [
            Line("I1", "IMPORT", "READY"),
            Line("E1", "EXPORT", "READY", licence: "70-1234"),
            Line("D1", "DELIVERY", "READY", driver: "สมชาย"),
            Line("F1", "IMPORT", "READY", "70-1234", "สมชาย ใจดี", "081-2345678"),
            Line("C1", "IMPORT", "CANCELLED"),
            Line("X1", "EXPORT", "COMPLETED"),
            // An export with its truck but no box and no seal yet (17 Sep 2026).
            Line("E2", "EXPORT", "READY", "70-1234", "สมชาย ใจดี", "081-2345678") with { Container = "", Seal = "" },
        ]);
        var whole = composed.Count == 1 ? composed[0] : "";
        var reminder = new (string Why, bool Ok)[]
        {
            ("one message for a short day", composed.Count == 1),
            ("the heading names the day and the haulier", whole.StartsWith("งานวันนี้ 16/09/2026 — SHORE", StringComparison.Ordinal)),
            ("an import is named by job, container, customer and delivery place",
                whole.Contains("Job 260600800773 · ตู้ TEMU5246902 · ลูกค้า L'OREAL · ส่งที่ โรงงานบางปะกง", StringComparison.Ordinal)),
            ("an export by booking, job, container, customer, loading plant and the return yard",
                whole.Contains("Booking LC2606594 · Job 260600800773 · ตู้ TEMU5246902 · ลูกค้า L'OREAL · โหลดที่ YUSEN W/H · คืนตู้ที่ LCB A0", StringComparison.Ordinal)),
            ("a Domestic run by its own job number and warehouse", whole.Contains("Job D15441000 · คลัง JWD", StringComparison.Ordinal)),
            ("each job says what it is short of", whole.Contains("ขาด: ทะเบียนรถ, ชื่อ-สกุลคนขับ, เบอร์ติดต่อ", StringComparison.Ordinal)
                && whole.Contains("ขาด: ชื่อ-สกุลคนขับ, เบอร์ติดต่อ", StringComparison.Ordinal)),
            ("a job with its truck is not asked about", !whole.Contains("F1", StringComparison.Ordinal) && whole.Split("ขาด:").Length == 5),
            // 17 Sep 2026: an export's box and seal are asked for too.
            ("an export with its truck but no box and no seal is asked for those, and only those",
                whole.Contains("ขาด: เลขตู้, เลขซีล", StringComparison.Ordinal)
                && whole.Contains("Booking LC2606594 · Job 260600800773 · ตู้ — · ลูกค้า L'OREAL", StringComparison.Ordinal)),
            ("an import with a box is not asked for a seal",
                LineReminder.Missing(Line("I9", "IMPORT", "READY", "70-1234", "สมชาย", "081-2345678") with { Seal = "" }).Count == 0),
            ("the answer line says how an export sends its box and seal", whole.Contains("ตู้ XXXU1234567 ซีล 123456", StringComparison.Ordinal)),
            ("a cancelled or finished job is not asked about", !whole.Contains("CANCELLED", StringComparison.Ordinal)),
            ("it says how to answer so the answer can be read — leading with the container, since a job number is several boxes",
                whole.Contains("ตอบในกลุ่มนี้ทีละตู้", StringComparison.Ordinal)
                && whole.Contains("TXGU8142057 70-1234 สมชาย ใจดี 081-2345678", StringComparison.Ordinal)),
            ("nothing missing, nothing sent", LineReminder.Compose("SHORE", day, [Line("F1", "IMPORT", "READY", "70-1234", "สมชาย", "081-2345678")]).Count == 0),
            ("the hours are 08:00 and 12:00 unless set", LineReminder.Times(null).Select(at => at.ToString("HH:mm")).SequenceEqual(["08:00", "12:00"])),
            ("a setting may name its own hours, in any of the ways people write them",
                LineReminder.Times("7.30, 13:00").Select(at => at.ToString("HH:mm")).SequenceEqual(["07:30", "13:00"])),
            ("\"off\" is no hours at all", LineReminder.Times("off").Count == 0),
            ("a blank setting is the default, not off", LineReminder.Times("").Count == 2 && LineReminder.Times("  ").Count == 2),
            ("a setting that is not a time falls back to the default", LineReminder.Times("noon").Count == 2),
            ("a long day is split into whole parts under LINE's limit",
                LineReminder.Compose("SHORE", day, Enumerable.Range(1, 80).Select(i => Line($"I{i}", "IMPORT", "READY")).ToList()) is { Count: > 1 } parts
                    && parts.All(part => part.Length <= 5000)),
        };
        foreach (var (why, ok) in reminder)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }
        if (reminder.Any(r => !r.Ok)) Console.WriteLine(whole);
        Console.WriteLine();

        /* ------------------------------------------ the status chase */

        Console.WriteLine("A job with no truck reported is chased half an hour after its plan time and every two hours on — once each, never before.");
        Console.WriteLine();
        LineReminder.JobLine Planned(string key, string cat, string status, string planTime, string arrDate = "", string arrTime = "") =>
            new(key, cat, status, "ALLNEX", "260917600162", "LC2606594", "TXGU8142057", "WH ALLNEX", "YUSEN W/H", "LCB A0",
                planTime, "70-1234", "สมชาย", "081-2345678", Date: "16/09/2026", ArrDate: arrDate, ArrTime: arrTime);
        DateTimeOffset At(string clock) => new(2026, 9, 16, int.Parse(clock[..2]), int.Parse(clock[3..]), 0, TimeSpan.FromHours(7));
        var chase = new (string Why, bool Ok)[]
        {
            ("nothing due an hour before the plan time", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("12:00"), 30) is null),
            // Settled 17 Sep 2026: the first ask is after the plan time, not before it.
            ("nothing due inside the half hour ahead either — the ask before the plan time is off",
                LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("12:35"), 30) is null
                && LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("12:59"), 30) is null),
            ("switched on with its own margin, 'before' comes inside that margin and nowhere else",
                LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("12:35"), 30, 2, 30) == LineChase.Before
                && LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("12:00"), 30, 2, 30) is null
                && LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("12:35"), 30, 2, 15) is null),
            ("nothing between the plan time and the margin after it", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("13:10"), 30) is null),
            ("due 'overdue' once the plan time is the margin behind", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("13:31"), 30) == LineChase.Overdue),
            ("still 'overdue' — not asked again — an hour and a half later", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("15:00"), 30) == LineChase.Overdue),
            ("asked again two hours after the overdue ask", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("15:31"), 30) == "overdue#2"),
            ("and every two hours after that, each its own stage", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("17:45"), 30) == "overdue#3"
                && LineChase.AskNumber("overdue#3") == 3),
            ("not through the night: the repeats stop after six", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "03:00"), At("20:00"), 30) is null),
            ("repeats switched off leave the one overdue ask", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), At("17:45"), 30, 0) == LineChase.Overdue),
            ("an arrival keyed in between ends the repeats", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00", "16/09/2026", "15:10"), At("17:45"), 30) is null),
            ("a repeat says how long it has been and which ask this is",
                LineChase.Compose("SHORE", [(Planned("J", "IMPORT", "IN_TRANSIT", "13:00"), "overdue#3")], At("17:45")) is { } again
                    && again.Contains("เลยมา 4 ชม. 45 นาที ยังไม่มีรายงานถึง (ติดตามครั้งที่ 3)", StringComparison.Ordinal)),
            ("an arrival stamp on the job ends the chase", LineChase.Stage(Planned("J", "IMPORT", "IN_TRANSIT", "13:00", "16/09/2026", "12:50"), At("13:40"), 30) is null),
            ("an import at DELIVERED has arrived", LineChase.Stage(Planned("J", "IMPORT", "DELIVERED", "13:00"), At("13:40"), 30) is null),
            ("an export at DISPATCHED has reached its plant", LineChase.Stage(Planned("J", "EXPORT", "DISPATCHED", "13:00"), At("13:40"), 30) is null),
            ("an export still READY has not", LineChase.Stage(Planned("J", "EXPORT", "READY", "13:00"), At("13:40"), 30) == LineChase.Overdue),
            ("a cancelled job is left alone", LineChase.Stage(Planned("J", "IMPORT", "CANCELLED", "13:00"), At("13:40"), 30) is null),
            ("no plan time, nothing to chase", LineChase.Stage(Planned("J", "IMPORT", "READY", ""), At("13:40"), 30) is null),
            ("the chase switched off chases nothing", LineChase.Stage(Planned("J", "IMPORT", "READY", "13:00"), At("13:40"), 0) is null),
            ("the message names the job the department's way and says how to answer",
                LineChase.Compose("SHORE", [(Planned("J", "EXPORT", "READY", "13:00"), LineChase.Overdue)], At("13:40")) is { } text
                    && text.Contains("Booking LC2606594 · Job 260917600162 · ตู้ TXGU8142057 · ลูกค้า ALLNEX · โหลดที่ YUSEN W/H · รถ 70-1234", StringComparison.Ordinal)
                    && text.Contains("แผน 13:00 — เลยมา 40 นาที", StringComparison.Ordinal)
                    && text.Contains("TXGU8142057 ถึงโรงงาน 12:40", StringComparison.Ordinal)),
            ("nothing due, no message", LineChase.Compose("SHORE", [], At("13:40")).Length == 0),
            // "ประมาณ 10.00 รถถึงโรงงาน" at 08:37: at 10:00 the room is asked whether it did.
            ("an estimate is due its ask at its clock, once the clock has come, and only if it lay ahead of the message",
                !LineChase.EtaDue(Planned("J", "IMPORT", "IN_TRANSIT", "09:00"), At("10:00"), At("08:37"), At("09:55"))
                && LineChase.EtaDue(Planned("J", "IMPORT", "IN_TRANSIT", "09:00"), At("10:00"), At("08:37"), At("10:00"))
                && !LineChase.EtaDue(Planned("J", "IMPORT", "IN_TRANSIT", "09:00"), At("10:00"), At("10:20"), At("10:25"))
                && !LineChase.EtaDue(Planned("J", "IMPORT", "DELIVERED", "09:00"), At("10:00"), At("08:37"), At("10:05"))),
            ("the ask says what the driver promised",
                LineChase.Compose("DGT", [(Planned("J", "IMPORT", "IN_TRANSIT", "09:00"), LineChase.EtaStage(At("10:00")))], At("10:00"))
                    .Contains("แจ้งไว้ว่าคาดถึง 10:00 — ถึงโรงงานแล้วหรือยังครับ", StringComparison.Ordinal)
                && LineChase.IsEtaStage("eta:10:00") && !LineChase.IsEtaStage("overdue#2")),
        };
        foreach (var (why, ok) in chase)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }
        Console.WriteLine();

        /* ------------------------------------------ what the bot says back */

        Console.WriteLine("The room hears one line back, and never learns about another haulier's job.");
        Console.WriteLine();
        var arrivalRead = LineParser.Parse("TXGU8142057 ถึงโรงงาน 12:40", Received);
        var truckRead = LineParser.Parse("260600800773 70-1234 สมชาย ใจดี 081-2345678", Received);
        var replies = new (string Why, bool Ok, string? Got)[]
        {
            ("an arrival that will be applied is acknowledged with what will be written",
                LineReply.ForMessage(arrivalRead, "ready-to-apply", "DELIVERED") == "รับทราบ TXGU8142057 — DELIVERED · ถึง 12:40 · รอเจ้าหน้าที่ยืนยันครับ",
                LineReply.ForMessage(arrivalRead, "ready-to-apply", "DELIVERED")),
            ("the truck's details likewise",
                LineReply.ForMessage(truckRead, "ready-to-apply", "") == "รับทราบ 260600800773 — ทะเบียน 70-1234 · คนขับ สมชาย ใจดี · เบอร์ 081-2345678 · รอเจ้าหน้าที่ยืนยันครับ",
                LineReply.ForMessage(truckRead, "ready-to-apply", "")),
            ("a number that is nobody's and one that is somebody else's get the same words",
                LineReply.ForMessage(arrivalRead, LineAuthority.Outcome.NoSuchJob, "") == LineReply.ForMessage(arrivalRead, LineAuthority.Outcome.NotYourJob, "")
                    && LineReply.ForMessage(arrivalRead, LineAuthority.Outcome.NotYourJob, "")!.Contains("ไม่พบ TXGU8142057", StringComparison.Ordinal),
                LineReply.ForMessage(arrivalRead, LineAuthority.Outcome.NotYourJob, "")),
            ("a question gets no answer",
                LineReply.ForMessage(LineParser.Parse("AKZO NOBEL // LC2606594 ถึงโรงงานที่โมงคะ", Received), "question", "") is null, "-"),
            ("an unbound room is not spoken to",
                LineReply.ForMessage(arrivalRead, LineAuthority.Outcome.UnknownGroup, "") is null, "-"),
            // The department's rule, 17 Sep 2026: nothing that names a job, nothing said back.
            ("a message with nothing to find the job by is not answered, even a status report",
                LineReply.ForMessage(LineParser.Parse("ถึงแล้วครับ", Received), "no-reference", "") is null
                && LineReply.ForMessage(LineParser.Parse("รถติดบางนา", Received), "no-reference", "") is null, "-"),
            ("the room's own arranging is not answered either",
                LineReply.ForMessage(LineParser.Parse("รับวันนี้ได้รับเลยค่ะ ส่วนเรื่องคืนตู้รอลูกค้าขอ EARLY OPENGATE อีกทีค่ะ @จัดส่ง DCH", Received), "no-reference", "") is null, "-"),
            ("a seal alone is a reference, and is answered by",
                LineReply.Reference(LineParser.Parse("ซีล 123456 ลงเสร็จ", Received)) == "ซีล 123456", LineReply.Reference(LineParser.Parse("ซีล 123456 ลงเสร็จ", Received))),
            ("a greeting gets no answer at all",
                LineReply.ForMessage(LineParser.Parse("สวัสดีครับ", Received), LineEventWorker.NotAboutAJob, "") is null, "-"),
            ("an arrival taken from the send time says so",
                LineReply.ForMessage(LineParser.Parse("TXGU8142057 ถึงโรงงาน", Received), "ready-to-apply", "DELIVERED") == "รับทราบ TXGU8142057 — DELIVERED · ถึง 11:00 (เวลาที่ส่งข้อความ) · รอเจ้าหน้าที่ยืนยันครับ",
                LineReply.ForMessage(LineParser.Parse("TXGU8142057 ถึงโรงงาน", Received), "ready-to-apply", "DELIVERED")),
            // A photo is never answered (17 Sep 2026): there is no ForPhoto to check.
            ("a photo is never answered", typeof(LineReply).GetMethod("ForPhoto") is null, "-"),
        };
        foreach (var (why, ok, got) in replies)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          got {got ?? "(null)"}");
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
