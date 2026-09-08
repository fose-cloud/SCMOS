using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the mail extractor against a fixed list of emails, with
/// <c>--check-email</c>.
///
/// <para>
/// String rules and arithmetic only: no mailbox, no Graph, no credential. That
/// is the point — the extractor is the part of the Outlook integration that can
/// be got right before an Entra app registration exists, and this is how it gets
/// checked.
/// </para>
///
/// <para>
/// <b>The emails below are invented.</b> They are the shapes the register makes
/// likely — its own container numbers, bookings and bills of lading, in the
/// wording shipping lines use — but twenty real emails per category have been
/// asked for and have not arrived. When they do they belong here. An extractor
/// checked only against examples written by the person who wrote the patterns
/// is one that passes its own tests and fails on the first day.
/// </para>
/// </summary>
public static class EmailExtractionCheck
{
    private record Case(string Why, string Subject, string Body, string Type, string? Value);

    /// <summary>
    /// Container numbers taken from the register, with their real check digits.
    ///
    /// The first eight are genuine and pass. The last three are genuine too and
    /// do <b>not</b> pass — they are real containers on real jobs with a digit
    /// typed wrong, and they are here so that nobody later "fixes" the extractor
    /// by refusing anything that fails the check.
    /// </summary>
    private static readonly string[] GoodContainers =
    [
        "MRSU4470591", "TLLU2164930", "FTAU2808940", "TLLU3202721",
        "BSIU2888650", "NSSU0196628", "TEMU0404097", "TXGU5007562",
    ];

    /// <summary>
    /// Real containers whose check digit is wrong — a digit typed badly.
    ///
    /// Still the right shape, so they are still read out of an email and left
    /// for the matcher to weigh. Refusing them here would make three real
    /// containers unmatchable forever.
    /// </summary>
    private static readonly string[] MistypedCheckDigit =
    [
        "CCAU2296185", "TTLU3509940", "OOLU6234939",
    ];

    /// <summary>
    /// A real container in the register whose fourth character is I, not one of
    /// U, J or Z.
    ///
    /// A different kind of typo, and the extractor genuinely cannot read it:
    /// ISO 6346 has no category I, and widening the pattern to any fourth
    /// letter would make "LOAD1234567" a container number. So this one is not
    /// extracted, and that is the right answer rather than a gap to paper over
    /// — the fix belongs in the register, where YMLI is a mistyped YMLU.
    /// </summary>
    private const string MistypedCategory = "YMLI3584489";

    private static readonly Case[] Cases =
    [
        /* ---- the identifiers, in the wording the lines use ---- */
        new("a job number in the subject",
            "SCMOS Job 260600800773 — delivery order attached", "",
            EmailExtraction.Kind.JobCode, "260600800773"),
        new("a container named in the subject",
            "Container TEMU0404097 discharged", "",
            EmailExtraction.Kind.Container, "TEMU0404097"),
        new("a container written with a space after the prefix",
            "CNTR TEMU 0404097 ready for pickup", "",
            EmailExtraction.Kind.Container, "TEMU0404097"),
        new("a booking behind its label",
            "Booking No: BKKGG8920900 confirmed", "",
            EmailExtraction.Kind.Booking, "BKKGG8920900"),
        new("a bill of lading behind its label",
            "B/L A15GA02185 released", "",
            EmailExtraction.Kind.BillOfLading, "A15GA02185"),
        new("a bill of lading written the register's other way",
            "Please find BL/260631000583 attached", "",
            EmailExtraction.Kind.BillOfLading, "260631000583"),
        new("read out of the body when the subject says nothing",
            "Re: shipment", "Container is BSIU2888650, arriving Thursday.",
            EmailExtraction.Kind.Container, "BSIU2888650"),

        /* ---- what must NOT be read ---- */
        new("a ten-digit delivery note is not a job number",
            "Delivery 6317309804 completed", "",
            EmailExtraction.Kind.JobCode, null),
        new("nor is a longer run of digits carrying twelve inside it",
            "Ref 2606008007731234", "",
            EmailExtraction.Kind.JobCode, null),
        new("a four-letter word before seven digits is not a container",
            "Order LOAD1234567 dispatched", "",
            EmailExtraction.Kind.Container, null),
        new("a bare token is not a booking without its label",
            "Please quote 4367142 when replying", "",
            EmailExtraction.Kind.Booking, null),
        new("an empty email yields nothing",
            "", "", EmailExtraction.Kind.JobCode, null),
    ];

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-email")) return null;

        var failed = 0;

        Console.WriteLine();
        Console.WriteLine("What the extractor reads out of a shipping email.");
        Console.WriteLine();
        foreach (var one in Cases)
        {
            var found = EmailExtraction.Read(one.Subject, one.Body);
            var got = found.FirstOrDefault(item => item.Type == one.Type)?.Value;
            var ok = got == one.Value;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {one.Why}");
            if (!ok)
            {
                Console.WriteLine($"          subject   {one.Subject}");
                Console.WriteLine($"          want      {one.Type} = {one.Value ?? "(nothing)"}");
                Console.WriteLine($"          got       {got ?? "(nothing)"}");
            }
        }
        Console.WriteLine();

        /* ------------------------------------------------- the check digit */

        Console.WriteLine("The ISO 6346 check digit, against numbers off the register.");
        Console.WriteLine();
        foreach (var code in GoodContainers)
        {
            var ok = EmailExtraction.IsWellFormedContainer(code);
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {code} is well formed");
        }
        foreach (var code in MistypedCheckDigit)
        {
            // Not well formed, and still extracted — see the note on the field.
            var wellFormed = EmailExtraction.IsWellFormedContainer(code);
            if (wellFormed) failed++;
            Console.WriteLine($"  {(!wellFormed ? "ok  " : "FAIL")}  {code} fails the check digit, as the register has it");

            var found = EmailExtraction.Read($"Container {code} arrived", "")
                .Any(one => one.Type == EmailExtraction.Kind.Container && one.Value == code);
            if (!found) failed++;
            Console.WriteLine($"  {(found ? "ok  " : "FAIL")}  {code} is still read out of the email, not discarded");
        }

        // The other kind of typo, which cannot be read and should not be.
        var unreadable = EmailExtraction.Read($"Container {MistypedCategory} arrived", "")
            .Any(one => one.Type == EmailExtraction.Kind.Container);
        if (unreadable) failed++;
        Console.WriteLine($"  {(!unreadable ? "ok  " : "FAIL")}  {MistypedCategory} is not read at all — "
            + "ISO 6346 has no category I, and widening for it would make LOAD1234567 a container");
        Console.WriteLine();

        /* -------------------------------------------- where it was found */

        Console.WriteLine("Where a number was found, which is not the same as what it says.");
        Console.WriteLine();

        var subjectFind = EmailExtraction.Read("Container TEMU0404097", "").First();
        if (!subjectFind.InSubject) failed++;
        Console.WriteLine($"  {(subjectFind.InSubject ? "ok  " : "FAIL")}  a subject find is marked as one");

        var bodyFind = EmailExtraction.Read("Re: shipment", "Container TEMU0404097").First();
        if (bodyFind.InSubject) failed++;
        Console.WriteLine($"  {(!bodyFind.InSubject ? "ok  " : "FAIL")}  a body find is not");

        // The subject wins, because the body is the thread.
        var both = EmailExtraction.Read("Container TEMU0404097", "Container TEMU0404097 again")
            .Where(one => one.Type == EmailExtraction.Kind.Container).ToList();
        var once = both.Count == 1 && both[0].InSubject;
        if (!once) failed++;
        Console.WriteLine($"  {(once ? "ok  " : "FAIL")}  the same number in both is reported once, from the subject");

        var labelled = EmailExtraction.Read("Container No: TEMU0404097", "").First();
        if (!labelled.Labelled) failed++;
        Console.WriteLine($"  {(labelled.Labelled ? "ok  " : "FAIL")}  a named container is marked as named");

        var bare = EmailExtraction.Read("TEMU0404097 discharged", "").First();
        if (bare.Labelled) failed++;
        Console.WriteLine($"  {(!bare.Labelled ? "ok  " : "FAIL")}  a bare one is not");
        Console.WriteLine();

        /* ------------------------------------- one rule for a job number */

        Console.WriteLine("The mail extractor and LINE read a job number the same way.");
        Console.WriteLine();
        var sameRule = new[]
        {
            "260600800773", "6317309804", "2606008007731234", "8505076096",
        };
        foreach (var text in sameRule)
        {
            var viaLine = LineParser.Parse(text, DateTimeOffset.UtcNow).JobNumber;
            var viaMail = EmailExtraction.Read(text, "")
                .FirstOrDefault(one => one.Type == EmailExtraction.Kind.JobCode)?.Value;
            var agree = viaLine == viaMail;
            if (!agree) failed++;
            Console.WriteLine($"  {(agree ? "ok  " : "FAIL")}  \"{text}\" reads the same both ways ({viaMail ?? "nothing"})");
        }
        Console.WriteLine();


        /* ------------------------------------------------ matching a job */

        Console.WriteLine("Which job a message points at, and how sure.");
        Console.WriteLine();

        static EmailMatching.Evidence Ev(string kind, string value, bool inSubject, bool wellFormed, params string[] keys) =>
            new(new EmailExtraction.Found(kind, value, inSubject, false, wellFormed), keys);

        var matchCases = new (string Why, EmailMatching.Evidence[] Evidence, string Key, string Want)[]
        {
            ("a container in the subject naming one job is linked",
                [Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true, "J1")],
                "J1", EmailMatching.Decision.Link),

            ("the same container from the body alone is only offered",
                [Ev(EmailExtraction.Kind.Container, "TEMU0404097", false, true, "J1")],
                "J1", EmailMatching.Decision.Suggest),

            ("a container whose check digit fails is offered, not linked",
                [Ev(EmailExtraction.Kind.Container, "CCAU2296185", true, false, "J1")],
                "J1", EmailMatching.Decision.Suggest),

            ("our own job number naming one job is linked",
                [Ev(EmailExtraction.Kind.JobCode, "260600800773", true, true, "J1")],
                "J1", EmailMatching.Decision.Link),

            ("a booking naming one job is offered — a booking is several containers",
                [Ev(EmailExtraction.Kind.Booking, "BKKGG8920900", true, true, "J1")],
                "J1", EmailMatching.Decision.Suggest),

            ("a bill of lading alone is offered at most",
                [Ev(EmailExtraction.Kind.BillOfLading, "A15GA02185", true, true, "J1")],
                "J1", EmailMatching.Decision.Suggest),

            /* ---- the two that must not become links ---- */
            ("a booking covering three jobs is not linked to any of them",
                [Ev(EmailExtraction.Kind.Booking, "BKKGG8920900", true, true, "J1", "J2", "J3")],
                "J1", EmailMatching.Decision.Unmatched),

            // 0.96 split two ways is 0.48, under the specification's 0.70 floor.
            // So a value the register holds against two jobs offers neither —
            // the message stays in the queue with what it named on it, and a
            // person chooses. Pinned because it is a real consequence of the
            // thresholds and somebody should decide it deliberately, not
            // discover it.
            ("a container the register holds against two jobs offers neither",
                [Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true, "J1", "J2")],
                "J1", EmailMatching.Decision.Unmatched),

            /* ---- corroboration ---- */
            ("a container and a job number agreeing are linked",
                [Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true, "J1"),
                 Ev(EmailExtraction.Kind.JobCode, "260600800773", true, true, "J1")],
                "J1", EmailMatching.Decision.Link),

            // A booking and a bill of lading, both quoted in a thread, reach
            // 0.9447 together — corroboration lifts them a long way, and still
            // not over the bar. That is the intended shape: two soft signals
            // from the body do not add up to certainty.
            ("two weak signals from the body agreeing are offered, not linked",
                [Ev(EmailExtraction.Kind.Booking, "BKKGG8920900", false, true, "J1"),
                 Ev(EmailExtraction.Kind.BillOfLading, "A15GA02185", false, true, "J1")],
                "J1", EmailMatching.Decision.Suggest),
        };

        foreach (var (why, ev, key, want) in matchCases)
        {
            var got = EmailMatching.Match(ev).FirstOrDefault(one => one.JobKey == key);
            var decision = got?.Decision ?? EmailMatching.Decision.Unmatched;
            var ok = decision == want;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          want {want}, got {decision} at {got?.Confidence.ToString() ?? "no match"}");
        }
        Console.WriteLine();

        /* -------------------------------------- the arithmetic, exactly */

        Console.WriteLine("The numbers behind those decisions.");
        Console.WriteLine();

        var pairs = new (string Why, EmailMatching.Evidence[] Evidence, double Want)[]
        {
            ("one well-formed container in the subject is 0.96",
                [Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true, "J1")], 0.96),
            ("from the body it is 0.912",
                [Ev(EmailExtraction.Kind.Container, "TEMU0404097", false, true, "J1")], 0.912),
            ("split across two jobs it is 0.48 each",
                [Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true, "J1", "J2")], 0.48),
            // 1 - (0.04 x 0.04). Doubt multiplies; weights do not add.
            ("a container and a job number together leave 0.16% doubt",
                [Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true, "J1"),
                 Ev(EmailExtraction.Kind.JobCode, "260600800773", true, true, "J1")], 0.9984),
        };

        foreach (var (why, ev, want) in pairs)
        {
            var got = EmailMatching.Match(ev).First(one => one.JobKey == "J1").Confidence;
            var ok = Math.Abs(got - want) < 0.0001;
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
            if (!ok) Console.WriteLine($"          want {want}, got {got}");
        }
        Console.WriteLine();

        /* ------------------------------------------- what it refuses to do */

        Console.WriteLine("Where it refuses to choose.");
        Console.WriteLine();

        // Two jobs each named by their own strong identifier. Both clear the
        // bar, and picking the higher would be reading a difference the
        // arithmetic never meant to carry.
        var twoWinners = EmailMatching.Match([
            Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true, "J1"),
            Ev(EmailExtraction.Kind.JobCode, "260600800773", true, true, "J2"),
        ]);
        var bothLinked = twoWinners.Count(one => one.Decision == EmailMatching.Decision.Link) == 2;
        if (!bothLinked) failed++;
        Console.WriteLine($"  {(bothLinked ? "ok  " : "FAIL")}  two jobs can each be confident on their own evidence");

        var refused = EmailMatching.OnlyLink(twoWinners) is null;
        if (!refused) failed++;
        Console.WriteLine($"  {(refused ? "ok  " : "FAIL")}  but nothing is linked automatically when two qualify");

        var single = EmailMatching.OnlyLink(EmailMatching.Match(
            [Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true, "J1")]));
        var acted = single?.JobKey == "J1";
        if (!acted) failed++;
        Console.WriteLine($"  {(acted ? "ok  " : "FAIL")}  one clear winner is acted on");

        var unknown = EmailMatching.Match([Ev(EmailExtraction.Kind.Container, "TEMU0404097", true, true)]);
        if (unknown.Count != 0) failed++;
        Console.WriteLine($"  {(unknown.Count == 0 ? "ok  " : "FAIL")}  a value the register does not know yields no match at all");

        var nothing = EmailMatching.Match([]);
        if (nothing.Count != 0) failed++;
        Console.WriteLine($"  {(nothing.Count == 0 ? "ok  " : "FAIL")}  and neither does an email naming nothing");
        Console.WriteLine();

        /* ------------------------------------------ the thresholds are one */

        Console.WriteLine("The rule and the column that stores it read the same thresholds.");
        Console.WriteLine();
        var atLink = EmailMatching.DecisionFor(MailLink.AutoLink) == EmailMatching.Decision.Link;
        if (!atLink) failed++;
        Console.WriteLine($"  {(atLink ? "ok  " : "FAIL")}  exactly {MailLink.AutoLink} links");

        var justUnder = EmailMatching.DecisionFor(MailLink.AutoLink - 0.0001) == EmailMatching.Decision.Suggest;
        if (!justUnder) failed++;
        Console.WriteLine($"  {(justUnder ? "ok  " : "FAIL")}  a hair under it only suggests, unrounded");

        var atSuggest = EmailMatching.DecisionFor(MailLink.Suggest) == EmailMatching.Decision.Suggest;
        if (!atSuggest) failed++;
        Console.WriteLine($"  {(atSuggest ? "ok  " : "FAIL")}  exactly {MailLink.Suggest} suggests");

        var under = EmailMatching.DecisionFor(MailLink.Suggest - 0.0001) == EmailMatching.Decision.Unmatched;
        if (!under) failed++;
        Console.WriteLine($"  {(under ? "ok  " : "FAIL")}  and below it nothing is offered");
        Console.WriteLine();

        Console.WriteLine(failed == 0
            ? "All mail extraction checks passed."
            : $"{failed} mail extraction check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
