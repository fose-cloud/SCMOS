using System.Text.Json;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the supervisor monitor's rules against a fixture, with
/// `--check-monitor`.
///
/// The risk list is a judgement about somebody's working day, and the way it
/// fails is not by breaking. It fails by widening — one more "just in case"
/// condition, then another, until the screen is four hundred rows and people
/// stop reading it. So the cases that must come back <b>clean</b> outnumber the
/// ones that must be flagged: a job three weeks out with no carrier, a finished
/// job that arrived late, a cancelled one. Those are the assertions that keep
/// the list short enough to act on.
/// </summary>
public static class MonitorCheck
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    private static WorkspaceTabs.JobView Job(
        string key, string date, string status = "WAITING_SUPPLIER",
        string owner = "Watsana", string ownerId = "OP-01",
        string trucker = "SANGJA", string licence = "75-7381", string driver = "นายป้อม",
        string arrDate = "", string arrTime = "") =>
        new(key, "IMPORT", ownerId, date, status,
            "", "1X20'", "", "", "HENKEL",
            "", "", "", "",
            trucker, "JOB-" + key,
            owner, "", "", licence,
            driver, "", "",
            arrDate, arrTime,
            default(JsonElement));

    private static readonly (string Why, WorkspaceTabs.JobView Job, MonitorRules.Risk? Expect)[] Cases =
    [
        /* ---- what must be flagged ---- */
        ("yesterday's job with nothing recorded as arrived is overdue",
            Job("A", "09/09/2026"), MonitorRules.Risk.Overdue),
        ("a job with no owner is one nobody is looking at",
            Job("B", "12/09/2026", owner: "", ownerId: ""), MonitorRules.Risk.Unassigned),
        ("two days out with no carrier still needs one",
            Job("C", "12/09/2026", trucker: ""), MonitorRules.Risk.NoCarrier),
        ("carrier agreed, no lorry and no driver named",
            Job("D", "12/09/2026", licence: "", driver: ""), MonitorRules.Risk.NoTruck),
        ("today counts as close", Job("E", "10/09/2026", trucker: ""), MonitorRules.Risk.NoCarrier),

        /* ---- what must stay off the list ---- */
        ("a job in hand, running in two days, wants nothing",
            Job("F", "12/09/2026"), null),
        ("three weeks out with no carrier is not a problem yet, it is next month",
            Job("G", "01/10/2026", trucker: ""), null),
        ("yesterday's job that arrived is not overdue",
            Job("H", "09/09/2026", arrDate: "09/09/2026", arrTime: "14:00"), null),
        ("a completed job is not a problem to solve",
            Job("I", "01/09/2026", status: "COMPLETED"), null),
        ("nor is a cancelled one", Job("J", "01/09/2026", status: "CANCELLED"), null),
        ("a date nobody can read is left for a person, not guessed at",
            Job("K", "soon"), null),
        ("a lorry with a plate but no driver named is still crewed enough",
            Job("L", "12/09/2026", driver: ""), null),
        // Measured on the real register: 958 of 1,081 rows were this — journeys
        // that ran and arrived, listed for a plate nobody had typed in.
        ("a job that arrived last month is not chased for a missing plate",
            Job("L2", "01/08/2026", licence: "", driver: "", arrDate: "01/08/2026", arrTime: "10:00"), null),
        ("nor for a missing carrier",
            Job("L3", "01/08/2026", trucker: "", arrDate: "01/08/2026", arrTime: "10:00"), null),
        ("nor is an arrived job with no owner",
            Job("L4", "01/08/2026", owner: "", ownerId: "", arrDate: "01/08/2026"), null),

        /* ---- one reason each, not one per fault ---- */
        ("overdue outranks everything: it has already happened",
            Job("M", "08/09/2026", trucker: "", licence: "", driver: ""), MonitorRules.Risk.Overdue),
        ("unowned outranks a missing carrier: nobody is even looking",
            Job("N", "12/09/2026", owner: "", ownerId: "", trucker: ""), MonitorRules.Risk.Unassigned),
    ];

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-monitor")) return null;

        var failed = 0;
        Console.WriteLine($"Today is {Today:dd/MM/yyyy}. Anything within {MonitorRules.SoonDays} days counts as close.");
        Console.WriteLine();

        foreach (var (why, job, expect) in Cases)
        {
            var got = MonitorRules.Judge(job, Today)?.Why;
            var ok = got == expect;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {job.Date,-12} -> {Show(got),-12} "
                + (ok ? "" : $"expected {Show(expect)}  ") + $"({why})");
        }

        // The order is the point of the list: a supervisor reads down it and
        // stops when the urgent part is dealt with.
        var order = MonitorRules.InReadingOrder(
            Cases.Select(c => MonitorRules.Judge(c.Job, Today)).Where(f => f is not null).Select(f => f!.Value));
        // Named, not "sorted by its own ordinal" — that agreed with whatever
        // the enum said and so could never have caught the enum being wrong,
        // which it was.
        var kinds = order.Select(flag => flag.Why).Distinct().ToList();
        MonitorRules.Risk[] wanted =
            [MonitorRules.Risk.Overdue, MonitorRules.Risk.Unassigned,
             MonitorRules.Risk.NoCarrier, MonitorRules.Risk.NoTruck];
        var sorted = kinds.SequenceEqual(wanted);
        if (!sorted) failed++;
        Console.WriteLine();
        Console.WriteLine($"{(sorted ? "ok  " : "FAIL")}  the list reads worst-first: "
            + string.Join(" → ", kinds)
            + (sorted ? "" : "  expected " + string.Join(" → ", wanted)));

        /* ---- the load board ---- */
        var team = new[]
        {
            Job("P1", "09/09/2026", owner: "Watsana", ownerId: "OP-01"),
            Job("P2", "12/09/2026", owner: "Watsana", ownerId: "OP-01"),
            Job("P3", "01/09/2026", owner: "Watsana", ownerId: "OP-01", status: "COMPLETED"),
            Job("P4", "12/09/2026", owner: "Uthai", ownerId: "OP-02"),
        };
        var loads = MonitorRules.Loads(team, Today);

        var watsana = loads.FirstOrDefault(load => load.OwnerId == "OP-01");
        var uthai = loads.FirstOrDefault(load => load.OwnerId == "OP-02");

        Console.WriteLine();
        failed += Say("a closed job is not something somebody is still carrying", watsana.Carrying, 2);
        failed += Say("only the job that needs somebody counts as flagged", watsana.Flagged, 1);
        failed += Say("the oldest thing waiting is a day old", watsana.OldestDaysWaiting, 1);
        failed += Say("nothing late means nothing waiting", uthai.OldestDaysWaiting, 0);
        failed += Say("the person with trouble is listed first", loads[0].OwnerId == "OP-01" ? 1 : 0, 1);

        /* ---- where the month went ---- */
        var delays = new (string Responsible, int? ImpactMinutes)[]
        {
            ("Subcontractor", 90), ("Subcontractor", 30), ("Subcontractor", null),
            ("Customer", 200),
            ("Operation", 15), ("Operation", 0),
            ("", null),
        };
        var blame = MonitorRules.Blames(delays);

        Console.WriteLine();
        failed += Say("the carrier's three cases are counted",
            blame.First(b => b.Party == "Subcontractor").Cases, 3);
        failed += Say("only the two that recorded minutes are added up",
            blame.First(b => b.Party == "Subcontractor").Minutes, 120);
        failed += Say("the one that did not is reported, not folded in as zero",
            blame.First(b => b.Party == "Subcontractor").Unmeasured, 1);
        failed += Say("a zero impact counts as unmeasured, not as measured zero",
            blame.First(b => b.Party == "Operation").Unmeasured, 1);
        failed += Say("a blank party is not lost, it becomes None",
            blame.First(b => b.Party == "None").Cases, 1);
        failed += Say("the biggest loss of time is first",
            blame[0].Party == "Customer" ? 1 : 0, 1);

        // Equal minutes, unequal cases: the one that went wrong more often is
        // the one to talk about first. Stated here because the fixture above
        // used to tie by accident and quietly test this instead.
        var tied = MonitorRules.Blames([("Port", 60), ("Customer", 30), ("Customer", 30)]);
        failed += Say("on equal minutes, more cases comes first",
            tied[0].Party == "Customer" ? 1 : 0, 1);

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"{Cases.Length} risk cases, the load board and the delay summary, all as expected."
            : $"{failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int Say(string why, int got, int want)
    {
        var ok = got == want;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {why,-56} got {got}  want {want}");
        return ok ? 0 : 1;
    }

    private static string Show(MonitorRules.Risk? risk) => risk?.ToString() ?? "(clean)";
}
