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

        /* ------------------------------ a photographed container */

        Console.WriteLine();
        Console.WriteLine("A photographed box goes into the haulier's job that is waiting for one.");
        Console.WriteLine();
        var waiting = Job("W1", "SHORE", "READY", "EXPORT", customer: "L'OREAL", workDate: "16/09/2026");
        var waiting2 = Job("W2", "SHORE", "READY", "EXPORT", customer: "HENKEL BPK", workDate: "16/09/2026");
        var filled = Job("F1", "SHORE", "IN_TRANSIT", "EXPORT", customer: "L'OREAL", workDate: "16/09/2026", container: "FSCU5037629");
        var same = Job("S1", "SHORE", "IN_TRANSIT", "EXPORT", customer: "L'OREAL", workDate: "16/09/2026", container: "TEMU5246902");
        var done = Job("D1", "SHORE", "COMPLETED", "EXPORT", customer: "L'OREAL", workDate: "16/09/2026");
        var theirs = Job("T1", "SANGJA", "READY", "EXPORT", customer: "L'OREAL", workDate: "16/09/2026");

        var photos = new (string Why, LineAuthority.LineDecision Got, string Want, string? Key)[]
        {
            ("one job waiting for a number takes it",
                LineAuthority.DecideContainer(Vendor("SHORE"), "TEMU5246902", [waiting, filled]), LineAuthority.Outcome.Ok, "W1"),
            ("two jobs waiting is a choice",
                LineAuthority.DecideContainer(Vendor("SHORE"), "TEMU5246902", [waiting, waiting2]), LineAuthority.Outcome.ManyJobs, null),
            ("a job that already carries the number is agreement, not a change",
                LineAuthority.DecideContainer(Vendor("SHORE"), "TEMU5246902", [waiting, same]), LineAuthority.Outcome.AlreadyThere, "S1"),
            ("a job with a different number keyed is never offered",
                LineAuthority.DecideContainer(Vendor("SHORE"), "TEMU5246902", [filled]), LineAuthority.Outcome.NoOpenJob, null),
            ("a finished job is not reopened for a photo",
                LineAuthority.DecideContainer(Vendor("SHORE"), "TEMU5246902", [done]), LineAuthority.Outcome.NoOpenJob, null),
            ("another haulier's waiting job is not this haulier's",
                LineAuthority.DecideContainer(Vendor("SHORE"), "TEMU5246902", [theirs]), LineAuthority.Outcome.NoOpenJob, null),
            ("an unmapped room writes nothing",
                LineAuthority.DecideContainer(new(Known: false, Active: true, LineGroupType.Vendor, ""), "TEMU5246902", [waiting]),
                LineAuthority.Outcome.UnknownGroup, null),
            ("a customer's room writes nothing",
                LineAuthority.DecideContainer(new(Known: true, Active: true, LineGroupType.Customer, "SHORE"), "TEMU5246902", [waiting]),
                LineAuthority.Outcome.GroupNotVendor, null),
        };
        foreach (var (why, got, want, key) in photos)
        {
            var ok = got.Result == want && (key is null || (got.Keys.Count == 1 && got.Keys[0] == key));
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          want {want} {key ?? ""}, got {got.Result} [{string.Join(", ", got.Keys)}] {got.Detail}");
        }
        var written = LineAuthority.DecideContainer(Vendor("SHORE"), "TEMU5246902", [waiting]);
        var carries = written.Applies && written.To == "TEMU5246902" && written.From == "";
        if (!carries) failed++;
        Console.WriteLine($"  {(carries ? "ok  " : "FAIL")}  the decision carries the number to write and nothing to move from");

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
