using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the dashboard briefing against fixtures, with <c>--check-briefing</c>.
///
/// <para>
/// A briefing fails by being agreeable. Padded to a comfortable length it stops
/// being read; ranked by what is easiest to count rather than what is worst, it
/// sends somebody to the wrong screen. So most of what is checked here is what
/// it must <b>not</b> say: nothing on a quiet register, nothing about a
/// colleague's workload to a reader who may not see the team, and never a
/// figure that was not counted.
/// </para>
/// </summary>
public static class BriefingCheck
{
    private static Briefing.Facts Facts(
        int live = 1000, int overdue = 0, int missingBeforeRun = 0, int withProblem = 0,
        int arrivedLate = 0, int incidents = 0, int openDelays = 0, int unmeasurable = 0,
        string busiestOwner = "Uthai", int busiestOwnerFlagged = 0,
        string topDelayParty = "", int topDelayCases = 0, bool showTeam = true) =>
        new(live, overdue, missingBeforeRun, withProblem, arrivedLate, JobRules.LateMinutes,
            incidents, openDelays, unmeasurable,
            busiestOwner, busiestOwnerFlagged, topDelayParty, topDelayCases, showTeam);

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-briefing")) return null;

        var failed = 0;
        Console.WriteLine("The dashboard briefing: what it says, and what it refuses to.");
        Console.WriteLine();

        /* ---- a quiet register says nothing ---- */
        var quiet = Briefing.Read(Facts());
        failed += Say("nothing wrong means nothing said", quiet.Count, 0);
        // The alternative is a briefing that finds five things every morning,
        // which is a briefing nobody reads by the second week.
        failed += Say("and the quiet line does not claim more than it knows",
            Briefing.Quiet(Facts()).Contains("ยังไม่พบปัญหา"), true);
        failed += Say("an empty register says so instead",
            Briefing.Quiet(Facts(live: 0)).Contains("ไม่มีงานที่ยังไม่จบ"), true);
        // "Nothing is wrong" and "nothing can be seen to be wrong" are not the
        // same claim, and only one of them is available on this register.
        failed += Say("a mostly unmeasured register admits that in the quiet line",
            Briefing.Quiet(Facts(live: 100, unmeasurable: 80)).Contains("ยังวัดไม่ได้"), true);

        /* ---- order ---- */
        Console.WriteLine();
        var busy = Briefing.Read(Facts(
            live: 2093, overdue: 40, missingBeforeRun: 83, arrivedLate: 228,
            incidents: 7, openDelays: 3, unmeasurable: 1463,
            busiestOwner: "Uthai", busiestOwnerFlagged: 82,
            topDelayParty: "ผู้ขนส่ง", topDelayCases: 12));

        var order = busy.Select(one => one.Kind).ToList();
        string[] wanted = ["incident", "openDelay", "overdue", "today", "late", "blame", "unmeasurable"];
        var sorted = order.SequenceEqual(wanted);
        if (!sorted) failed++;
        Console.WriteLine($"{(sorted ? "ok  " : "FAIL")}  worst first: {string.Join(" → ", order)}"
            + (sorted ? "" : $"\n      expected {string.Join(" → ", wanted)}"));

        failed += Say("the records finding is last, because it qualifies the rest",
            busy[^1].Urgency, Briefing.Urgency.Records);
        failed += Say("and everything above it is about the work",
            busy.Take(busy.Count - 1).All(one => one.Urgency != Briefing.Urgency.Records), true);

        /* ---- every finding carries its number and a way to act ---- */
        Console.WriteLine();
        failed += Say("no finding appears without a count behind it",
            busy.All(one => one.Count > 0), true);
        failed += Say("nor without a screen that answers it",
            busy.All(one => one.Screen.Length > 0), true);
        failed += Say("nor without words of its own",
            busy.All(one => one.Headline.Length > 0 && one.Detail.Length > 0), true);
        failed += Say("the counted figure is the one in the sentence",
            busy.First(one => one.Kind == "late").Count, 228);
        // The threshold is JobRules', the same figure the scorecard and the
        // monitor use, and the sentence quotes it rather than repeating "30".
        failed += Say("late says what late means, from the shared threshold",
            busy.First(one => one.Kind == "late").Detail.Contains($"{JobRules.LateMinutes} นาที"), true);

        /* ---- who is carrying it is team information ---- */
        Console.WriteLine();
        var team = Briefing.Read(Facts(missingBeforeRun: 123, busiestOwner: "Uthai", busiestOwnerFlagged: 82));
        var alone = Briefing.Read(Facts(missingBeforeRun: 123, busiestOwner: "Uthai", busiestOwnerFlagged: 82,
            showTeam: false));
        failed += Say("a supervisor is told who is carrying most of it",
            team.First(one => one.Kind == "today").Detail.Contains("Uthai"), true);
        // A briefing is still a view of the register, and a reader who may not
        // see the team may not learn whose backlog is worst from here either.
        failed += Say("somebody who may not see the team is not told a name",
            alone.First(one => one.Kind == "today").Detail.Contains("Uthai"), false);
        failed += Say("but still learns there is work to do",
            alone.Any(one => one.Kind == "today"), true);

        /* ---- the records finding only when it changes how the rest reads ---- */
        Console.WriteLine();
        failed += Say("a tidy register is not lectured about its records",
            Briefing.Read(Facts(live: 1000, unmeasurable: 50)).Any(one => one.Kind == "unmeasurable"), false);
        failed += Say("a fifth unmeasured is where it starts being the finding",
            Briefing.Read(Facts(live: 1000, unmeasurable: 200)).Any(one => one.Kind == "unmeasurable"), true);
        failed += Say("two thirds unmeasured certainly is",
            Briefing.Read(Facts(live: 2093, unmeasurable: 1463)).Any(one => one.Kind == "unmeasurable"), true);
        // Zero live jobs must not divide by anything or find a records problem.
        failed += Say("an empty register raises nothing at all",
            Briefing.Read(Facts(live: 0, unmeasurable: 0)).Count, 0);

        /* ---- one thing wrong is one finding ---- */
        Console.WriteLine();
        var single = Briefing.Read(Facts(live: 500, incidents: 1));
        failed += Say("a single incident is the whole briefing", single.Count, 1);
        failed += Say("and it is not padded out with reassurance",
            single.All(one => one.Count > 0), true);

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "The briefing says what was counted, in the order it matters, and nothing else."
            : $"{failed} problem(s).");
        return failed == 0 ? 0 : 1;
    }

    private static int Say<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {why,-58} {(ok ? "" : $"got {got}  want {want}")}");
        return ok ? 0 : 1;
    }
}
