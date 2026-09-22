using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Proves who may move which job, as part of <c>--check-line</c>.
///
/// <para>
/// Separate from <see cref="LineParserCheck"/> because it proves a different
/// thing. That one asks whether a message was read correctly; this asks whether
/// the reader was allowed to act on it. The second question is the one that
/// matters if the first is answered wrongly — a misread message that cannot
/// reach a job is a nuisance, and a correctly read one that reaches the wrong
/// vendor's job is an incident.
/// </para>
///
/// <para>
/// Arithmetic and string rules only. The rows below are shaped after the
/// register — the carrier spellings, the shared job numbers and the one legacy
/// status are all things that are really in it — but nothing here reads it.
/// </para>
/// </summary>
public static class LineAuthorityCheck
{
    private static LineAuthority.SpeakerGroup Vendor(string supplier) =>
        new(Known: true, Active: true, GroupType: LineGroupType.Vendor, SupplierName: supplier);

    private static LineAuthority.JobCandidate Job(
        string key, string carrier, string status, string category = "IMPORT",
        string customer = "", string plate = "", string workDate = "", string container = "") =>
        new(key, category, carrier, status, customer, container, plate, workDate);

    /// <summary>What the first real message said, beside the rows it could mean.</summary>
    private static LineAuthority.Clue Said(string text, string? container = "TEMU7592765",
        string plate = "700-3232", string day = "2026-09-16") =>
        new("ตู้ TEMU7592765", container, plate.Length > 0 ? [plate] : [], text, DateOnly.Parse(day));

    private record Case(
        string Why,
        LineAuthority.SpeakerGroup Group,
        string? Status,
        LineAuthority.JobCandidate[] Rows,
        string Want);

    private static readonly Case[] Cases =
    [
        /* ---------------------------------------------- who is speaking */

        new("a group nobody has mapped to a supplier cannot act",
            new(Known: false, Active: true, LineGroupType.Vendor, ""),
            "DELIVERED", [Job("J1", "WEALTHY", "IN_TRANSIT")],
            LineAuthority.Outcome.UnknownGroup),

        new("a group that has been switched off cannot act",
            new(Known: true, Active: false, LineGroupType.Vendor, "WEALTHY"),
            "DELIVERED", [Job("J1", "WEALTHY", "IN_TRANSIT")],
            LineAuthority.Outcome.GroupInactive),

        new("a customer's room does not move a job",
            new(Known: true, Active: true, LineGroupType.Customer, "WEALTHY"),
            "DELIVERED", [Job("J1", "WEALTHY", "IN_TRANSIT")],
            LineAuthority.Outcome.GroupNotVendor),

        new("nor does an internal one, however well meant",
            new(Known: true, Active: true, LineGroupType.Internal, "WEALTHY"),
            "DELIVERED", [Job("J1", "WEALTHY", "IN_TRANSIT")],
            LineAuthority.Outcome.GroupNotVendor),

        /* ------------------------------------------------- which job */

        new("a number on no row at all",
            Vendor("WEALTHY"), "DELIVERED", [],
            LineAuthority.Outcome.NoSuchJob),

        // The one this module exists for.
        new("a number that belongs to another haulier is refused",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "SANGJA", "IN_TRANSIT")],
            LineAuthority.Outcome.NotYourJob),

        new("a job with no carrier yet is nobody's, not everybody's",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "", "WAITING_SUPPLIER")],
            LineAuthority.Outcome.NotYourJob),

        new("a speaker with no name of its own matches nothing",
            Vendor(""), "DELIVERED", [Job("J1", "", "WAITING_SUPPLIER")],
            LineAuthority.Outcome.NotYourJob),

        new("one number shared by two hauliers picks the speaker's own row",
            Vendor("WEALTHY"), "DELIVERED",
            [Job("J1", "SANGJA", "IN_TRANSIT"), Job("J2", "WEALTHY", "IN_TRANSIT")],
            LineAuthority.Outcome.Ok),

        // 236 numbers in the register sit on more than one row.
        new("two of the speaker's own rows is a question, not a guess",
            Vendor("WEALTHY"), "DELIVERED",
            [Job("J1", "WEALTHY", "IN_TRANSIT"), Job("J2", "WEALTHY", "IN_TRANSIT")],
            LineAuthority.Outcome.ManyJobs),

        /* --------------------------------- the carrier spellings that exist */

        new("the register's \"T.O.\" is the supplier list's TO",
            Vendor("TO"), "DELIVERED", [Job("J1", "T.O.", "IN_TRANSIT")],
            LineAuthority.Outcome.Ok),

        new("and so are \"T.O\" and \"TO.\"",
            Vendor("T.O."), "DELIVERED", [Job("J1", "TO.", "IN_TRANSIT")],
            LineAuthority.Outcome.Ok),

        new("the register's short name is the room's supplier when the alias table says so",
            new(Known: true, Active: true, LineGroupType.Vendor, "DGT Cross Haul Co., Ltd.", ["DGT", "DGT CROSS HAUL"]),
            "DELIVERED", [Job("J1", "DGT", "IN_TRANSIT")],
            LineAuthority.Outcome.Ok),

        new("and a spelling the alias table does not know is still somebody else's",
            new(Known: true, Active: true, LineGroupType.Vendor, "DGT Cross Haul Co., Ltd.", ["DGT"]),
            "DELIVERED", [Job("J1", "D.G.T. LOGISTICS", "IN_TRANSIT")],
            LineAuthority.Outcome.NotYourJob),

        new("but punctuation is all that is forgiven — WEALTHY is not WEALTH",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "WEALTH", "IN_TRANSIT")],
            LineAuthority.Outcome.NotYourJob),

        new("nor are the two real look-alikes the same haulier",
            Vendor("TATIYAPOL"), "DELIVERED", [Job("J1", "TATIYAPON", "IN_TRANSIT")],
            LineAuthority.Outcome.NotYourJob),

        /* ------------------------------------------------- which move */

        new("forward along the ladder is what this is for",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "WEALTHY", "IN_TRANSIT")],
            LineAuthority.Outcome.Ok),

        new("saying it twice changes nothing",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "WEALTHY", "DELIVERED")],
            LineAuthority.Outcome.AlreadyThere),

        new("a delivered job is not put back on the road",
            Vendor("WEALTHY"), "IN_TRANSIT", [Job("J1", "WEALTHY", "DELIVERED")],
            LineAuthority.Outcome.Backwards),

        new("a finished job is not reopened from a chat room",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "WEALTHY", "COMPLETED")],
            LineAuthority.Outcome.JobClosed),

        new("nor is a cancelled one",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "WEALTHY", "CANCELLED")],
            LineAuthority.Outcome.JobClosed),

        new("a job somebody parked stays parked until they release it",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "WEALTHY", "HOLD")],
            LineAuthority.Outcome.JobHeld),

        // One row in the register still says "Truck Confirmed".
        new("a status from before the codes existed has no position to compare",
            Vendor("WEALTHY"), "DELIVERED", [Job("J1", "WEALTHY", "Truck Confirmed")],
            LineAuthority.Outcome.StatusUnknown),

        new("an import has no LOADING step to report",
            Vendor("WEALTHY"), "LOADING", [Job("J1", "WEALTHY", "PICKED_UP", "IMPORT")],
            LineAuthority.Outcome.NotOnLadder),

        new("an export does, and it is forward of PICKED_UP",
            Vendor("WEALTHY"), "LOADING", [Job("J1", "WEALTHY", "PICKED_UP", "EXPORT")],
            LineAuthority.Outcome.Ok),

        new("a number with nothing said about it is filed, not applied",
            Vendor("WEALTHY"), "", [Job("J1", "WEALTHY", "IN_TRANSIT")],
            LineAuthority.Outcome.NoStatus),
    ];

    public static int Run()
    {
        var failed = 0;

        Console.WriteLine("Who may move which job.");
        Console.WriteLine();
        foreach (var one in Cases)
        {
            var got = LineAuthority.Decide(one.Group, one.Status, one.Rows);
            var ok = got.Result == one.Want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {one.Why}");
            if (!ok)
                Console.WriteLine($"          want {one.Want}, got {got.Result} ({got.Detail})");
        }
        Console.WriteLine();

        /* ------------------------------------------- the order of the questions */

        Console.WriteLine("The speaker is settled before anything is said about a job.");
        Console.WriteLine();

        // An unmapped room asking about somebody else's job must be answered as
        // an unknown room. Answering "not your job" would confirm the number is
        // real to a sender nobody has identified.
        var stranger = LineAuthority.Decide(
            new(Known: false, Active: true, LineGroupType.Vendor, ""),
            "DELIVERED", [Job("J1", "SANGJA", "IN_TRANSIT")]);
        var quiet = stranger.Result == LineAuthority.Outcome.UnknownGroup && stranger.Keys.Count == 0;
        if (!quiet) failed++;
        Console.WriteLine($"  {(quiet ? "ok  " : "FAIL")}  an unknown room learns nothing about the job it named");
        if (!quiet) Console.WriteLine($"          got {stranger.Result} with {stranger.Keys.Count} key(s)");

        /* ------------------------------------------------ what comes back */

        Console.WriteLine();
        Console.WriteLine("What the decision hands on.");
        Console.WriteLine();

        var single = LineAuthority.Decide(Vendor("WEALTHY"), "DELIVERED",
            [Job("J7", "WEALTHY", "IN_TRANSIT")]);
        var singleRight = single.Applies && single.Keys.Count == 1 && single.Keys[0] == "J7"
            && single.From == "IN_TRANSIT" && single.To == "DELIVERED";
        if (!singleRight) failed++;
        Console.WriteLine($"  {(singleRight ? "ok  " : "FAIL")}  a match carries the key it matched and both statuses");

        var several = LineAuthority.Decide(Vendor("WEALTHY"), "DELIVERED",
            [Job("J1", "WEALTHY", "IN_TRANSIT"), Job("J2", "WEALTHY", "IN_TRANSIT")]);
        // The review screen has to show what the choice is between, so the keys
        // come back even though nothing is applied.
        var severalRight = !several.Applies && several.Keys.Count == 2;
        if (!severalRight) failed++;
        Console.WriteLine($"  {(severalRight ? "ok  " : "FAIL")}  an ambiguous match carries every key a person must choose from");

        var refused = LineAuthority.Decide(Vendor("WEALTHY"), "DELIVERED",
            [Job("J1", "SANGJA", "IN_TRANSIT")]);
        var refusedRight = !refused.Applies && refused.Keys.Count == 0;
        if (!refusedRight) failed++;
        Console.WriteLine($"  {(refusedRight ? "ok  " : "FAIL")}  a refusal carries no key at all");

        // Nothing but Ok may change a job. Asserted over every case rather than
        // trusted, because Applies is what the worker will branch on.
        var leaks = Cases
            .Select(one => LineAuthority.Decide(one.Group, one.Status, one.Rows))
            .Count(got => got.Applies && got.Result != LineAuthority.Outcome.Ok);
        if (leaks > 0) failed++;
        Console.WriteLine($"  {(leaks == 0 ? "ok  " : "FAIL")}  only an \"ok\" decision is allowed to change anything");

        /* ------------------------------ telling the rows apart */

        Console.WriteLine();
        Console.WriteLine("When a box or a plate is on several of the haulier's rows, the rest of the message decides.");
        Console.WriteLine();

        var loreal = Job("L1", "SHORE", "IN_TRANSIT", customer: "L'OREAL (THAILAND) LTD.", plate: "700-3232", workDate: "16/09/2026");
        var henkel = Job("H1", "SHORE", "IN_TRANSIT", customer: "HENKEL BPK", plate: "72-1585", workDate: "16/09/2026");
        var older = Job("L0", "SHORE", "DELIVERED", customer: "L'OREAL (THAILAND) LTD.", plate: "700-3232", workDate: "20/08/2026");

        var narrowing = new (string Why, LineAuthority.LineDecision Got, string Want, string? Key)[]
        {
            ("the customer in the message picks the row, accent and all",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED", [henkel, loreal],
                    Said("L'Oréal / TEMU7592765 / สมใจ / 700-3232 / ถึงโรงงาน 05:00 / ลงเสร็จ")),
                LineAuthority.Outcome.Ok, "L1"),
            ("the plate picks the row when the customer is not written",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED", [henkel, loreal],
                    Said("TEMU7592765 / 700-3232 / ลงเสร็จ")),
                LineAuthority.Outcome.Ok, "L1"),
            ("the day picks the row when nothing else does",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED", [older, loreal],
                    Said("TEMU7592765 / ลงเสร็จ", plate: "")),
                LineAuthority.Outcome.Ok, "L1"),
            ("two rows on the same day for the same customer and truck is a question",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED",
                    [loreal, loreal with { Key = "L2" }],
                    Said("L'Oréal / TEMU7592765 / 700-3232 / ลงเสร็จ")),
                LineAuthority.Outcome.ManyJobs, null),
            ("a clue that matches nothing narrows nothing",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED", [loreal, loreal with { Key = "L2" }],
                    Said("DANA / TEMU7592765 / 99-9999 / ลงเสร็จ", plate: "99-9999")),
                LineAuthority.Outcome.ManyJobs, null),
            ("the haulier's other customer's row is not offered",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED", [henkel, loreal],
                    Said("Henkel / 72-1585 / ลงเสร็จ", container: null, plate: "72-1585")),
                LineAuthority.Outcome.Ok, "H1"),
            ("the day keeps every row nearest it — two trips today stay a question",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED", [older, loreal, loreal with { Key = "L2" }],
                    Said("TEMU7592765 / ลงเสร็จ", plate: "")),
                LineAuthority.Outcome.ManyJobs, null),
            ("a truck's trip from last month is not today's message, even when it is the only one",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED", [older],
                    Said("700-3232 / ลงเสร็จ", container: null) with { PlateOnly = true }),
                LineAuthority.Outcome.NoSuchJob, null),
            ("a truck's trip this week is",
                LineAuthority.Decide(Vendor("SHORE"), "DELIVERED", [older, loreal],
                    Said("700-3232 / ลงเสร็จ", container: null) with { PlateOnly = true }),
                LineAuthority.Outcome.Ok, "L1"),
        };
        foreach (var (why, got, want, key) in narrowing)
        {
            var ok = got.Result == want && (key is null || (got.Keys.Count == 1 && got.Keys[0] == key));
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          want {want} {key ?? ""}, got {got.Result} [{string.Join(", ", got.Keys)}] {got.Detail}");
        }

        /* ---------------------------- a message about every box */

        // "CATALITE 260900760321 3 ตู้ อยู่โรงงาน", 17 Sep 2026: three rows,
        // one job number, one message for all of them.
        var box1 = Job("C1", "SHORE", "IN_TRANSIT", customer: "CATALITE", workDate: "17/09/2026", container: "AAAU1111111");
        var box2 = box1 with { Key = "C2", Container = "BBBU2222222" };
        var box3 = box1 with { Key = "C3", Container = "CCCU3333333", Status = "DELIVERED" };
        var three = LineAuthority.Decide(Vendor("SHORE"), LineParser.SiteArrival, [box1, box2, box3],
            Said("CATALITE 260900760321 3 ตู้ อยู่โรงงาน", container: null, plate: "", day: "2026-09-17") with { BoxCount = 3 });
        var two = LineAuthority.Decide(Vendor("SHORE"), LineParser.SiteArrival, [box1, box2, box3],
            Said("CATALITE 260900760321 2 ตู้ อยู่โรงงาน", container: null, plate: "", day: "2026-09-17") with { BoxCount = 2 });
        var every = new (string Why, bool Ok)[]
        {
            ("three boxes and three rows: every row, and the status the category gives",
                three is { Result: LineAuthority.Outcome.AllJobs, Applies: true, Every: true, To: "DELIVERED" } && three.Keys.SequenceEqual(["C1", "C2", "C3"])),
            ("two boxes and three rows is still a question", two.Result == LineAuthority.Outcome.ManyJobs && !two.Applies),
            ("the note carries the keys and gives them back",
                LineAuthority.KeysIn(LineAuthority.KeysNote(three.Detail, three.Keys)).SequenceEqual(["C1", "C2", "C3"])
                && LineAuthority.KeysIn(LineAuthority.KeysNote("one", ["C1"])).Count == 0
                && LineAuthority.KeysIn("งานนี้ปิดแล้ว (COMPLETED)").SequenceEqual(["COMPLETED"])),
            ("a row already there is skipped at approval, not refused for the rest — Move says so per row",
                !LineAuthority.Move(box3, LineParser.SiteArrival).Applies && LineAuthority.Move(box1, LineParser.SiteArrival).Applies),
            // 17 Sep 2026: the chase asks a DELIVERED job with no time for the time; the answer must be writable.
            ("a job already at the site takes an arrival time alone, and nothing without one",
                LineAuthority.Move(box3, LineParser.SiteArrival, arrival: true) is { Result: LineAuthority.Outcome.ArrivalOnly, Applies: true, To: "" }
                && LineAuthority.Move(box3, LineParser.SiteArrival, arrival: false).Result == LineAuthority.Outcome.AlreadyThere
                && LineAuthority.Move(box1, LineParser.SiteArrival, arrival: true).Result == LineAuthority.Outcome.Ok),
        };
        foreach (var (why, ok) in every)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        var mentions = new (string Why, bool Want, bool Got)[]
        {
            ("L'Oréal names L'OREAL (THAILAND) LTD.", true,
                LineAuthority.CustomerMentioned("L'OREAL (THAILAND) LTD.", "L'Oréal / TEMU7592765 / ลงเสร็จ")),
            ("Henkel names HENKEL BPK", true, LineAuthority.CustomerMentioned("HENKEL BPK", "Henkel ถึงโรงงาน")),
            ("Lotus names LOTUS ASIA", true, LineAuthority.CustomerMentioned("LOTUS ASIA", "lotus ลงเสร็จ")),
            ("THAILAND names nobody", false, LineAuthority.CustomerMentioned("L'OREAL (THAILAND) LTD.", "Thailand ลงเสร็จ")),
            ("AIR inside REPAIR is not AIR INTERNATIONAL", false,
                LineAuthority.CustomerMentioned("AIR INTERNATIONAL", "repair done ลงเสร็จ")),
            ("a plate with its province matches the register's tractor-and-trailer cell", true,
                LineAuthority.PlateMatches("75-4384 ชบ. / 75-4385 ชบ.", ["75-4385 ชลบุรี"])),
            ("a different truck does not", false, LineAuthority.PlateMatches("75-4384 ชบ.", ["75-4385"])),
        };
        foreach (var (why, want, got) in mentions)
        {
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        /* --------------------------------- the truck's details, no status */

        Console.WriteLine();
        Console.WriteLine("The answer to the morning reminder goes onto the open job it names.");
        Console.WriteLine();
        var truck = LineAuthority.Decide(Vendor("SHORE"), "", [Job("T1", "SHORE", "READY")],
            new LineAuthority.Clue("เลขงาน 260600800773", null, ["70-1234"], "260600800773 70-1234 สมชาย ใจดี 081-2345678", null, Details: true));
        var truckRight = truck.Result == LineAuthority.Outcome.TruckDetails && truck.Applies && truck.Keys is ["T1"] && truck.To == "";
        if (!truckRight) failed++;
        Console.WriteLine($"  {(truckRight ? "ok  " : "FAIL")}  a plate and a name with no status may be written, and move no status");
        var truckClosed = LineAuthority.Decide(Vendor("SHORE"), "", [Job("T2", "SHORE", "COMPLETED")],
            new LineAuthority.Clue("เลขงาน 260600800773", null, ["70-1234"], "", null, Details: true));
        var closedRight = truckClosed.Result == LineAuthority.Outcome.JobClosed && !truckClosed.Applies;
        if (!closedRight) failed++;
        Console.WriteLine($"  {(closedRight ? "ok  " : "FAIL")}  not onto a finished job");
        var bare = LineAuthority.Decide(Vendor("SHORE"), "", [Job("T3", "SHORE", "READY")],
            new LineAuthority.Clue("เลขงาน 260600800773", null, [], "260600800773 ครับ", null));
        var bareRight = bare.Result == LineAuthority.Outcome.NoStatus;
        if (!bareRight) failed++;
        Console.WriteLine($"  {(bareRight ? "ok  " : "FAIL")}  a number and nothing else is still filed, not applied");

        // 20 Sep 2026: a cell the department keyed is not pulled from the room again.
        var kept = Job("T4", "SHORE", "READY", plate: "70-1234") with { Driver = "สมชาย ใจดี", Contact = "081-2345678" };
        var saidAgain = new LineAuthority.Clue("เลขงาน 260600800773", null, ["70-1234"], "260600800773 70-1234 สมชาย ใจดี 081-2345678", null,
            Details: true, Fills: [LineAuthority.Cells.Licence, LineAuthority.Cells.Driver, LineAuthority.Cells.Contact]);
        var repeat = LineAuthority.Decide(Vendor("SHORE"), "", [kept], saidAgain);
        var partly = LineAuthority.Decide(Vendor("SHORE"), "", [kept with { Contact = "" }], saidAgain);
        var repeatRight = repeat.Result == LineAuthority.Outcome.AlreadyThere && !repeat.Applies
            && repeat.Detail.Contains("ไม่ดึงจากไลน์ซ้ำ", StringComparison.Ordinal)
            && partly.Result == LineAuthority.Outcome.TruckDetails && partly.Applies;
        if (!repeatRight) failed++;
        Console.WriteLine($"  {(repeatRight ? "ok  " : "FAIL")}  details the job already holds are filed as already there; one empty cell still queues it");
        // 18 Sep 2026: the reminder's answer names no job; the room's jobs still short of the details are what it can be about.
        var answer = new LineAuthority.Clue("งานที่ยังขาดข้อมูลรถ (ตอบแจ้งเตือน)", null, ["71-5111"], "พขร.เต๋า ใจเงิน 085-0892487 ทะเบียน 71-5111ชบ. ค่ะ",
            new DateOnly(2026, 9, 18), Details: true, Fills: [LineAuthority.Cells.Licence, LineAuthority.Cells.Driver, LineAuthority.Cells.Contact]);
        var oneShort = LineAuthority.Decide(Vendor("SHORE"), "", [Job("A1", "SHORE", "READY", workDate: "18/09/2026")], answer);
        var twoShort = LineAuthority.Decide(Vendor("SHORE"), "", [Job("A1", "SHORE", "READY", workDate: "18/09/2026"), Job("A2", "SHORE", "READY", workDate: "18/09/2026")], answer);
        var todayFirst = LineAuthority.Decide(Vendor("SHORE"), "", [Job("A1", "SHORE", "READY", workDate: "18/09/2026"), Job("A3", "SHORE", "READY", workDate: "19/09/2026")], answer);
        var answerRight = oneShort is { Result: LineAuthority.Outcome.TruckDetails, Applies: true } && oneShort.Keys.SequenceEqual(["A1"])
            && twoShort is { Result: LineAuthority.Outcome.ManyJobs, Applies: false } && twoShort.Keys.SequenceEqual(["A1", "A2"])
            && todayFirst is { Result: LineAuthority.Outcome.TruckDetails } && todayFirst.Keys.SequenceEqual(["A1"]);
        if (!answerRight) failed++;
        Console.WriteLine($"  {(answerRight ? "ok  " : "FAIL")}  the reminder's answer lands on the one job short of it, waits on a choice between two, and prefers today's over tomorrow's");

        var stamped = Job("T5", "SHORE", "DELIVERED") with { ArrDate = "16/09/2026", ArrTime = "10:20" };
        var stampedRight = LineAuthority.Move(stamped, LineParser.SiteArrival, arrival: true).Result == LineAuthority.Outcome.AlreadyThere
            && LineAuthority.Move(stamped with { ArrTime = "" }, LineParser.SiteArrival, arrival: true).Result == LineAuthority.Outcome.ArrivalOnly
            && LineAuthority.Move(stamped with { Status = "IN_TRANSIT" }, LineParser.SiteArrival, arrival: true).Result == LineAuthority.Outcome.Ok;
        if (!stampedRight) failed++;
        Console.WriteLine($"  {(stampedRight ? "ok  " : "FAIL")}  an arrival the register already holds is not taken again; a missing time still is; a status move still is");

        // 22 Sep 2026: a job holding every designated cell takes nothing more
        // from a room unless the message moves it forward — a message with
        // nothing to write is filed, not lit "LINE 1" on the row.
        var full = Job("T6", "SHORE", "DELIVERED", plate: "70-1234") with { ArrDate = "22/09/2026", ArrTime = "08:00", Driver = "สมชาย", Contact = "081-2345678" };
        var noStatus = LineAuthority.Move(full, null);
        var backwards = LineAuthority.Move(full, "IN_TRANSIT");
        var forward = LineAuthority.Move(full with { Status = "IN_TRANSIT" }, LineParser.SiteArrival);
        var closed = LineAuthority.Move(full with { Status = "COMPLETED" }, LineParser.SiteArrival);
        var redundantRight = noStatus.Redundant(full) && backwards.Redundant(full) && closed.Redundant(full with { Status = "COMPLETED" })
            && !forward.Redundant(full with { Status = "IN_TRANSIT" })
            && !noStatus.Redundant(full with { Driver = "" }) && !noStatus.Redundant(full with { ArrTime = "" })
            && !noStatus.Redundant(null)
            && LineAuthority.Cells.Designated.SequenceEqual([LineAuthority.Cells.Licence, LineAuthority.Cells.Driver, LineAuthority.Cells.Contact, LineAuthority.Cells.ArrDate, LineAuthority.Cells.ArrTime]);
        if (!redundantRight) failed++;
        Console.WriteLine($"  {(redundantRight ? "ok  " : "FAIL")}  a message that writes nothing to a job holding every designated cell is filed; a forward move, a missing cell or no pinned job still waits");

        /* ------------------------------------- at the site, by category */

        Console.WriteLine();
        Console.WriteLine("\"ถึงโรงงาน\" is the delivery on an import and the pickup on an export.");
        Console.WriteLine();
        var site = new (string Why, string Category, string From, string Want)[]
        {
            ("an import at the plant is delivered", "IMPORT", "IN_TRANSIT", "DELIVERED"),
            ("a Domestic run at the plant is delivered", "DELIVERY", "IN_TRANSIT", "DELIVERED"),
            ("an export at the plant is at its pickup", "EXPORT", "READY", "DISPATCHED"),
        };
        foreach (var (why, category, from, want) in site)
        {
            var move = LineAuthority.Move(Job("S1", "SHORE", from, category), LineParser.SiteArrival);
            var ok = move.Result == LineAuthority.Outcome.Ok && move.To == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          want {want}, got {move.Result} {move.To} ({move.Detail})");
        }
        var plain = LineAuthority.ResolveSite("IMPORT", "PICKED_UP") == "PICKED_UP";
        if (!plain) failed++;
        Console.WriteLine($"  {(plain ? "ok  " : "FAIL")}  every other status is its own");

        /* ------------------------------- the two ways in must agree */

        Console.WriteLine();
        Console.WriteLine("Approving a message asks the same question receiving one did.");
        Console.WriteLine();

        // Decide() ends by calling Move(), and the apply endpoint calls Move()
        // directly once an operator has picked a row. If those two ever drifted,
        // a message refused on arrival could be approved anyway — so the whole
        // move half of the case list is replayed through Move() and must give
        // the same answer.
        var disagreed = 0;
        foreach (var one in Cases.Where(c => c.Rows.Length == 1))
        {
            var whole = LineAuthority.Decide(one.Group, one.Status, one.Rows);
            // Only where Decide actually got as far as the move.
            if (whole.Keys.Count != 1) continue;

            var move = LineAuthority.Move(one.Rows[0], one.Status);
            if (move.Result != whole.Result || move.From != whole.From || move.To != whole.To)
            {
                disagreed++;
                Console.WriteLine($"  FAIL  {one.Why}");
                Console.WriteLine($"          Decide said {whole.Result}, Move said {move.Result}");
            }
        }
        if (disagreed > 0) failed += disagreed;
        Console.WriteLine($"  {(disagreed == 0 ? "ok  " : "FAIL")}  Decide and Move give the same verdict on every single-row case");

        Console.WriteLine();
        return failed;
    }
}
