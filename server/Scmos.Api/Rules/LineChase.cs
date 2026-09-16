using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// The status chase: a message to a haulier's room about a job whose truck
/// has not been reported at the site as its plan time comes and goes.
///
/// <para>
/// Asked for on 16 Sep 2026: when a job in today's My Job has no arrival —
/// no status at or past the site, nothing in ARRIVAL DATE / ARRIVAL TIME —
/// before the time in DATE and PLAN LOADING TIME, ask the room. Two stages:
/// a little before the plan time, and again once it has passed by the same
/// margin. Each stage asks once per job per day; the audit trail is the
/// ledger. Import and export alike; a Domestic run the same way.
/// </para>
///
/// <para>Pure: the jobs and the clock in, the due list and the text out.</para>
/// </summary>
public static class LineChase
{
    /// <summary>How long before, and how long after, the plan time a job is chased.</summary>
    public const int DefaultMinutes = 30;

    /// <summary>The plan time is coming and no truck has been reported.</summary>
    public const string Before = "before";

    /// <summary>The plan time has passed and no truck has been reported.</summary>
    public const string Overdue = "overdue";

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
    /// Which stage a job is due at right now, or null: <see cref="Before"/>
    /// inside the margin ahead of the plan time, <see cref="Overdue"/> once
    /// the plan time is the margin behind. A job with no plan time, or one
    /// whose truck has been reported, is due nothing.
    /// </summary>
    public static string? Stage(LineReminder.JobLine job, DateTimeOffset now, int minutes)
    {
        if (minutes <= 0 || Arrived(job)) return null;
        if (PlanAt(job) is not { } plan) return null;
        var margin = TimeSpan.FromMinutes(minutes);
        if (now >= plan + margin) return Overdue;
        if (now >= plan - margin && now < plan) return Before;
        return null;
    }

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
            : $"แผน {plan.Value:HH:mm} — เลยมา {(int)Math.Round((now - plan.Value).TotalMinutes)} นาที ยังไม่มีรายงานถึง";
        return $"{number}) {string.Join(" · ", parts)}\n   {when}";
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
