using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// The status chase: a message to a haulier's room about a job whose truck
/// has not been reported at the site as its plan time comes and goes.
///
/// <para>
/// Asked for on 16 Sep 2026 and settled on 17 Sep: when a job in today's My
/// Job has no arrival — no status at or past the site, nothing in ARRIVAL
/// DATE / ARRIVAL TIME — half an hour after the time in DATE and PLAN
/// LOADING TIME, ask the room; then every two hours while the register
/// still shows nothing. Nothing is asked before the plan time unless a
/// margin for that is switched on (<c>Line__ChaseBeforeMinutes</c>) — the
/// department found the ask ahead of time was noise. Each stage asks once
/// per job per day; the audit trail is the ledger. Import and export alike;
/// a Domestic run the same way.
/// </para>
///
/// <para>Pure: the jobs and the clock in, the due list and the text out.</para>
/// </summary>
public static class LineChase
{
    /// <summary>How long after the plan time a job is first chased.</summary>
    public const int DefaultMinutes = 30;

    /// <summary>How long before the plan time a job is chased; off unless configured.</summary>
    public const int DefaultBeforeMinutes = 0;

    /// <summary>The plan time is coming and no truck has been reported.</summary>
    public const string Before = "before";

    /// <summary>The plan time has passed and no truck has been reported.</summary>
    public const string Overdue = "overdue";

    /// <summary>
    /// How often a job still unreported after the overdue ask is asked again,
    /// until the register says the truck arrived (asked for 16 Sep 2026:
    /// "ทุกๆ 2 ชั่วโมงจนกว่าจะมีการอัพเดท"). Each repeat is its own stage —
    /// "overdue#2", "overdue#3" — so each is sent once.
    /// </summary>
    public const int DefaultRepeatHours = 2;

    /// <summary>The most repeats in a day; a job nobody reports on is not chased through the night.</summary>
    public const int MaxRepeats = 6;

    /// <summary>
    /// Whether the truck has been reported at the site: an arrival stamp on
    /// the job, or a status at or past the rung "ถึงโรงงาน" resolves to for
    /// the job's category — DELIVERED on an import or a Domestic run,
    /// DISPATCHED on an export. A job that has left the ladder — cancelled,
    /// on hold, finished — is not chased either.
    /// </summary>
    public static bool Arrived(LineReminder.JobLine job)
    {
        if (Formats.Clean(job.ArrDate).Length > 0 && Formats.Clean(job.ArrTime).Length > 0) return true;
        var status = job.Status;
        if (string.Equals(status, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, JobStatus.Hold, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, JobStatus.Completed, StringComparison.OrdinalIgnoreCase))
            return true;
        var site = LineAuthority.Rank(job.Category, LineAuthority.ResolveSite(job.Category, LineParser.SiteArrival));
        var now = LineAuthority.Rank(job.Category, status);
        return now >= 0 && site >= 0 && now >= site;
    }

    /// <summary>The plan moment, Bangkok, or null when the job has no readable date and time.</summary>
    public static DateTimeOffset? PlanAt(LineReminder.JobLine job)
    {
        var moment = Formats.Moment(job.Date, job.PlanTime);
        return moment is null ? null : new DateTimeOffset(moment.Value, TimeSpan.FromHours(7));
    }

    /// <summary>
    /// Which stage a job is due at right now, or null: <see cref="Overdue"/>
    /// once the plan time is <paramref name="minutes"/> behind, a repeat
    /// every <paramref name="repeatHours"/> after that, and <see cref="Before"/>
    /// inside <paramref name="beforeMinutes"/> ahead of the plan time when
    /// that is on. A job with no plan time, or one whose truck has been
    /// reported, is due nothing.
    /// </summary>
    public static string? Stage(LineReminder.JobLine job, DateTimeOffset now, int minutes,
        int repeatHours = DefaultRepeatHours, int beforeMinutes = DefaultBeforeMinutes)
    {
        if (minutes <= 0 || Arrived(job)) return null;
        if (PlanAt(job) is not { } plan) return null;
        var margin = TimeSpan.FromMinutes(minutes);
        if (now >= plan + margin)
        {
            if (repeatHours <= 0) return Overdue;
            // The overdue ask, then one more every repeatHours after it while
            // the register still shows no arrival.
            var repeats = (int)Math.Floor((now - (plan + margin)).TotalHours / repeatHours);
            if (repeats <= 0) return Overdue;
            // Named by the ask, not the hours, so a changed interval keeps
            // counting from where the day's ledger left off.
            return repeats > MaxRepeats ? null : $"{Overdue}#{repeats + 1}";
        }
        if (beforeMinutes > 0 && now >= plan - TimeSpan.FromMinutes(beforeMinutes) && now < plan) return Before;
        return null;
    }

    /// <summary>Which ask this is for a job past its plan time: 1 for the overdue ask, 2 for the first repeat…</summary>
    public static int AskNumber(string stage) =>
        stage == Before ? 0
        : stage == Overdue ? 1
        : int.TryParse(stage.Replace(Overdue + "#", ""), out var ask) && ask > 1 ? ask
        : 1;

    /// <summary>One job as the chase names it — the same cells the reminder uses, with the plan time.</summary>
    private static string Line(int number, LineReminder.JobLine job, string stage, DateTimeOffset now)
    {
        static string Or(string? value) => Formats.Clean(value).Length > 0 ? Formats.Clean(value) : "—";
        var parts = new List<string>();
        switch (job.Category.ToUpperInvariant())
        {
            case "EXPORT":
                parts.Add($"Booking {Or(job.Booking)}");
                parts.Add($"Job {Or(job.JobCode)}");
                parts.Add($"ตู้ {Or(job.Container)}");
                parts.Add($"ลูกค้า {Or(job.Customer)}");
                parts.Add($"โหลดที่ {Or(job.Plant)}");
                break;
            case "DELIVERY":
                parts.Add($"Job {Or(job.JobNo)}");
                parts.Add($"ลูกค้า {Or(job.Customer)}");
                parts.Add($"ส่งที่ {Or(job.Province.Length > 0 ? job.Province : job.Destination)}");
                break;
            default:
                parts.Add($"Job {Or(job.JobCode)}");
                parts.Add($"ตู้ {Or(job.Container)}");
                parts.Add($"ลูกค้า {Or(job.Customer)}");
                parts.Add($"ส่งที่ {Or(job.Destination)}");
                break;
        }
        if (Formats.Clean(job.Licence).Length > 0) parts.Add($"รถ {Formats.Clean(job.Licence)}");

        var plan = PlanAt(job);
        var when = plan is null ? "" : stage == Before
            ? $"แผน {plan.Value:HH:mm} — อีก {Math.Max(1, (int)Math.Round((plan.Value - now).TotalMinutes))} นาที"
            : $"แผน {plan.Value:HH:mm} — เลยมา {Elapsed(now - plan.Value)} ยังไม่มีรายงานถึง"
              + (stage == Overdue ? "" : $" (ติดตามครั้งที่ {AskNumber(stage)})");
        return $"{number}) {string.Join(" · ", parts)}\n   {when}";
    }

    /// <summary>"45 นาที", "2 ชม.", "4 ชม. 5 นาที" — how long past the plan time, or how long a margin is.</summary>
    public static string Elapsed(TimeSpan gap)
    {
        var minutes = Math.Max(0, (int)Math.Round(gap.TotalMinutes));
        if (minutes < 60) return $"{minutes} นาที";
        return minutes % 60 == 0 ? $"{minutes / 60} ชม." : $"{minutes / 60} ชม. {minutes % 60} นาที";
    }

    /// <summary>
    /// The message for one room: the jobs due now, before-stage first, and
    /// how to answer so the answer lands on the job. Empty when nothing is due.
    /// </summary>
    public static string Compose(string supplier, IReadOnlyList<(LineReminder.JobLine Job, string Stage)> due, DateTimeOffset now)
    {
        if (due.Count == 0) return "";
        var ordered = due
            .OrderBy(one => one.Stage == Overdue ? 0 : 1)
            .ThenBy(one => PlanAt(one.Job) ?? DateTimeOffset.MaxValue)
            .ToList();

        var text = new StringBuilder($"ติดตามสถานะรถ {now:HH:mm} — {supplier}\nรถถึงหน้างานหรือยังครับ");
        var number = 0;
        foreach (var (job, stage) in ordered)
        {
            number++;
            text.Append("\n\n").Append(Line(number, job, stage, now));
        }
        text.Append("\n\nตอบในกลุ่มนี้: <เลขตู้ หรือ Job No.> ถึงโรงงาน HH:MM — เช่น TXGU8142057 ถึงโรงงาน 12:40\nถ้ายังไม่ถึง: <เลขตู้> คาดถึง HH:MM");
        return text.ToString();
    }
}
