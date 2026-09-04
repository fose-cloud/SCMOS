using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the supervisor's problem list against a fixture, with
/// <c>--check-problems</c>.
///
/// <para>
/// The list has one job: to be believed. It fails by saying a job is fine when
/// nobody ever recorded whether it was — a shipment with no arrival time is not
/// a shipment that arrived on time, and the difference between those two is a
/// hundred of the July plan's three hundred and seventy live jobs. So the cases
/// below spend as much effort on what must come back <b>clean</b>, and on what
/// must come back <b>unmeasured</b>, as on what must be flagged.
/// </para>
/// <para>
/// Arithmetic and strings only: no database, no register, no clock.
/// </para>
/// </summary>
public static class ProblemCheck
{
    private static JobRecord Job(
        string key, string status = "IN_TRANSIT",
        string date = "10/09/2026", string planTime = "08:00",
        string arrDate = "10/09/2026", string arrTime = "08:00",
        string reason = "", string incident = "") =>
        new()
        {
            Key = key, Status = status, Date = date, PlanTime = planTime,
            ArrDate = arrDate, ArrTime = arrTime, Reason = reason, Incident = incident,
            Customer = "HENKEL", Trucker = "SANGJA", Cat = "IMPORT",
        };

    private static readonly ProblemRules.Recorded Nothing = ProblemRules.Recorded.Nothing;

    private static readonly
        (string Why, JobRecord Job, ProblemRules.Recorded Seen, ProblemRules.Problem? Expect)[] Cases =
    [
        /* ---- what must be flagged ---- */
        ("an incident somebody typed is a problem because they typed it",
            Job("A", incident: "ตู้ REJECT ตรวจรับกลับคืน"), Nothing, ProblemRules.Problem.Incident),
        ("a delay recorded and never closed is still open",
            Job("B"), new ProblemRules.Recorded(1, "รอเอกสารจากลูกค้า", 0, ""),
            ProblemRules.Problem.DelayOpen),
        ("a stage an operator marked delayed",
            Job("C"), new ProblemRules.Recorded(0, "", 1, "รถเสีย"),
            ProblemRules.Problem.StageDelayed),
        ("two hours past the plan is late, measured",
            Job("D", arrTime: "10:00"), Nothing, ProblemRules.Problem.ArrivedLate),
        ("arriving the next day is late even at the same hour",
            Job("E", arrDate: "11/09/2026"), Nothing, ProblemRules.Problem.ArrivedLate),
        ("a reason typed in the Reason column, which is what the DELAY tab counts",
            Job("F", reason: "รถติดในท่า"), Nothing, ProblemRules.Problem.DelayNoted),
        ("a held status says so without any text at all",
            Job("G", status: "DELAYED"), Nothing, ProblemRules.Problem.DelayNoted),

        /* ---- what must stay off the list ---- */
        ("a shipment that arrived when it said it would is not a problem",
            Job("H"), Nothing, null),
        ("nor is one that arrived early",
            Job("I", arrTime: "06:30"), Nothing, null),
        // The threshold is JobRules', shared with the carrier scorecard. Twenty
        // minutes is inside it on both screens or on neither.
        ("twenty minutes is not what anybody means by late",
            Job("J", arrTime: "08:20"), Nothing, null),
        ("a finished job is history, not something to do this morning",
            Job("K", status: "COMPLETED", arrTime: "18:00", incident: "ตู้เสียหาย"), Nothing, null),
        ("nor is a cancelled one",
            Job("L", status: "CANCELLED", arrTime: "18:00", reason: "ลูกค้ายกเลิก"), Nothing, null),
        ("a delay that was closed is closed",
            Job("M"), new ProblemRules.Recorded(0, "", 0, ""), null),
        ("a job whose lorry has not arrived yet is not late, it is running",
            Job("N", arrDate: "", arrTime: ""), Nothing, null),
        ("a plan time nobody filled in is unmeasurable, not on time",
            Job("O", planTime: "", arrTime: "18:00"), Nothing, null),
        ("whitespace in the incident column is not an incident",
            Job("P", incident: "   "), Nothing, null),

        /* ---- one job, several faults ---- */
        ("the incident outranks the lateness beside it",
            Job("Q", arrTime: "17:00", incident: "การ์ดใส่ผิดกล่อง"), Nothing,
            ProblemRules.Problem.Incident),
        ("an open delay outranks a measured late arrival",
            Job("R", arrTime: "17:00"), new ProblemRules.Recorded(1, "ท่าปิด", 0, ""),
            ProblemRules.Problem.DelayOpen),
    ];

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-problems")) return null;

        var failed = 0;
        Console.WriteLine($"Late means more than {JobRules.LateMinutes} minutes past the plan — "
            + "JobRules', the same figure the carrier scorecard scores on.");
        Console.WriteLine();

        foreach (var (why, job, seen, expect) in Cases)
        {
            var got = ProblemRules.Judge(job, seen)?.Worst;
            var ok = got == expect;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {job.Key,-3} -> {Show(got),-13}"
                + (ok ? "" : $"expected {Show(expect)}  ") + $"({why})");
        }

        /* ---- a job with several faults keeps all of them ---- */
        var many = ProblemRules.Judge(
            Job("Z", arrTime: "17:00", reason: "รถติด", incident: "ตู้ REJECT"),
            new ProblemRules.Recorded(2, "รอด่าน", 1, "รถเสีย"));

        Console.WriteLine();
        failed += Say("every fault is kept, not just the worst", many?.Problems.Count ?? 0, 4);
        // DelayNoted is the one that does not appear: an open record and a
        // marked stage both say more than "there is text in a column", and
        // saying it a third time would make one job look like three.
        failed += Say("the weakest reading stands down when a stronger one spoke",
            many?.Problems.Contains(ProblemRules.Problem.DelayNoted) == true ? 1 : 0, 0);
        failed += Say("the minutes are measured, not guessed", many?.MinutesLate ?? 0, 540);
        failed += Say("the incident's words are the ones shown",
            many?.Note == "ตู้ REJECT" ? 1 : 0, 1);
        failed += Say("and the screen can say where they came from",
            many?.NoteFrom == ProblemRules.Source.Incident ? 1 : 0, 1);

        /* ---- measurable is not the same as on time ---- */
        var blind = ProblemRules.Judge(Job("Y", arrTime: "", incident: "ตู้เสียหาย"), Nothing);
        Console.WriteLine();
        failed += Say("with no arrival time, lateness is unmeasured",
            blind?.Measurable == false ? 1 : 0, 1);
        failed += Say("and reported as zero minutes, never as zero lateness",
            blind?.MinutesLate ?? -1, 0);

        var measured = ProblemRules.Judge(Job("X", arrTime: "17:00"), Nothing);
        failed += Say("a shipment with both times is measurable",
            measured?.Measurable == true ? 1 : 0, 1);

        /* ---- the order the list is read in ---- */
        var order = ProblemRules
            .InReadingOrder(Cases
                .Select(one => ProblemRules.Judge(one.Job, one.Seen))
                .Where(row => row is not null)
                .Select(row => row!.Value))
            .Select(row => row.Worst)
            .Distinct()
            .ToList();
        ProblemRules.Problem[] wanted =
        [
            ProblemRules.Problem.Incident, ProblemRules.Problem.DelayOpen,
            ProblemRules.Problem.StageDelayed, ProblemRules.Problem.ArrivedLate,
            ProblemRules.Problem.DelayNoted,
        ];
        var sorted = order.SequenceEqual(wanted);
        if (!sorted) failed++;
        Console.WriteLine();
        Console.WriteLine($"{(sorted ? "ok  " : "FAIL")}  the list reads worst-first: "
            + string.Join(" → ", order)
            + (sorted ? "" : "  expected " + string.Join(" → ", wanted)));

        // Within one kind, the shipment that lost the most time is first — a
        // supervisor reading down stops when the serious part is dealt with.
        var byMinutes = ProblemRules.InReadingOrder([
            ProblemRules.Judge(Job("S1", arrTime: "09:00"), Nothing)!.Value,
            ProblemRules.Judge(Job("S2", arrTime: "16:00"), Nothing)!.Value,
            ProblemRules.Judge(Job("S3", arrTime: "10:00"), Nothing)!.Value,
        ]).Select(row => row.Key).ToList();
        failed += Say("the worst delay in a kind is read first",
            byMinutes.SequenceEqual(new[] { "S2", "S3", "S1" }) ? 1 : 0, 1);

        /* ---- the headline ---- */
        JobRecord[] register =
        [
            Job("T1", arrTime: "17:00"),                      // live, late, measurable
            Job("T2"),                                        // live, on time
            Job("T3", arrTime: ""),                           // live, unmeasurable
            Job("T4", planTime: "", arrTime: ""),             // live, unmeasurable
            Job("T5", status: "COMPLETED", arrTime: "17:00"), // done — counted nowhere
            Job("T6", status: "CANCELLED"),                   // cancelled — likewise
        ];
        var found = register
            .Select(job => ProblemRules.Judge(job, Nothing))
            .Where(row => row is not null)
            .Select(row => row!.Value)
            .ToList();
        var tally = ProblemRules.Count(register, found);

        Console.WriteLine();
        failed += Say("finished and cancelled work is not work in flight", tally.Live, 4);
        failed += Say("one of the four has something wrong with it", tally.WithProblem, 1);
        failed += Say("two of them cannot be judged on time at all", tally.Unmeasurable, 2);
        failed += Say("and one measured late arrival is one", tally.ArrivedLate, 1);

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"{Cases.Length} cases, the ordering and the headline, all as expected."
            : $"{failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int Say(string why, int got, int want)
    {
        var ok = got == want;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {why,-58} got {got}  want {want}");
        return ok ? 0 : 1;
    }

    private static string Show(ProblemRules.Problem? problem) => problem?.ToString() ?? "(clean)";
}
