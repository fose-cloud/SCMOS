using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// The status chase: a message to a haulier's room about a job whose
/// arrival is not yet written as its plan time comes and goes.
///
/// <para>
/// Asked for on 16 Sep 2026 and set, for the last time that day, at the end
/// of the 17th — after a day on which every two-hourly ask counted against
/// LINE's monthly push allowance:
/// </para>
/// <list type="bullet">
/// <item><b>Half an hour before the plan time</b>, once: is the truck on its way.</item>
/// <item><b>At 10:00 and at 14:00</b>, once each: every job whose plan time
/// has passed and whose ARRIVAL DATE or ARRIVAL TIME is still empty —
/// "หากยังไม่ได้รับการอัพเดทและข้อมูลในตารางงานยังไม่ได้ลงข้อมูล".</item>
/// </list>
/// <para>
/// Nothing else, and nothing in between: the truck's details are the
/// morning reminder's (09:00), tomorrow's jobs are the summary's (16:00).
/// Each stage asks once per job per day; the audit trail is the ledger.
/// Import and export alike; a Domestic run the same way.
/// </para>
///
/// <para>Pure: the jobs and the clock in, the due list and the text out.</para>
/// </summary>
public static class LineChase
{
    /// <summary>How long before the plan time a job is chased: half an hour.</summary>
    public const int DefaultBeforeMinutes = 30;

    /// <summary>The rounds a job still without its arrival is chased at, Bangkok.</summary>
    public const string DefaultRounds = "10:00, 14:00";

    /// <summary>The plan time is coming and no truck has been reported.</summary>
    public const string Before = "before";

    /// <summary>The stage of a round: "round:10:00", "round:14:00" — once each per job per day.</summary>
    public static string RoundStage(TimeOnly at) => $"round:{at:HH:mm}";

    /// <summary>Whether a stage is one of the day's rounds.</summary>
    public static bool IsRoundStage(string stage) => stage.StartsWith("round:", StringComparison.Ordinal);

    /// <summary>
    /// The clock times of the rounds out of a setting — "10:00, 14:00",
    /// "10.00 14.00", or "off". Blank is the default.
    /// </summary>
    public static IReadOnlyList<TimeOnly> Rounds(string? setting) =>
        LineReminder.Times(string.IsNullOrWhiteSpace(setting) ? DefaultRounds : setting);

    /// <summary>
    /// The two cells the reminder is about — LICENCE and DRIVER — in the
    /// words it asks with; empty when both are filled. Imports and exports
    /// only: a Domestic run is the company's own fleet. A chase line on a
    /// job still short of them says so, so the room need not be written to
    /// twice.
    /// </summary>
    public static IReadOnlyList<string> MissingDetails(LineReminder.JobLine job)
    {
        if (!string.Equals(job.Category, "IMPORT", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Category, "EXPORT", StringComparison.OrdinalIgnoreCase)) return [];
        var gaps = new List<string>(2);
        if (Formats.Clean(job.Licence).Length == 0) gaps.Add("ทะเบียนรถ");
        if (Formats.Clean(job.Driver).Length == 0) gaps.Add("ชื่อ-สกุลคนขับ");
        return gaps;
    }

    /// <summary>
    /// Whether the arrival is written: ARRIVAL DATE and ARRIVAL TIME both on
    /// the job. Until then the job is chased whatever its status says — see
    /// <see cref="LineAuthority.AwaitingArrival"/>. A job that has left the
    /// ladder — cancelled, on hold, finished — is not chased either.
    /// </summary>
    public static bool Arrived(LineReminder.JobLine job) =>
        !LineAuthority.AwaitingArrival(job.Category, job.Status, job.ArrDate, job.ArrTime);

    /// <summary>The plan moment, Bangkok, or null when the job has no readable date and time.</summary>
    public static DateTimeOffset? PlanAt(LineReminder.JobLine job)
    {
        var moment = Formats.Moment(job.Date, job.PlanTime);
        return moment is null ? null : new DateTimeOffset(moment.Value, TimeSpan.FromHours(7));
    }

    /// <summary>
    /// Whether the before-ask is due right now: inside
    /// <paramref name="beforeMinutes"/> ahead of the plan time, the arrival
    /// not yet written. A job with no plan time is due nothing.
    /// </summary>
    public static bool BeforeDue(LineReminder.JobLine job, DateTimeOffset now, int beforeMinutes = DefaultBeforeMinutes)
    {
        if (beforeMinutes <= 0 || Arrived(job)) return false;
        if (PlanAt(job) is not { } plan) return false;
        return now >= plan - TimeSpan.FromMinutes(beforeMinutes) && now < plan;
    }

    /// <summary>
    /// Whether a round's ask is due for a job at this moment: the round's
    /// clock has come (within its ten-minute window), the plan time is
    /// behind it, and the arrival is still not written.
    /// </summary>
    public static bool RoundDue(LineReminder.JobLine job, DateTimeOffset now, TimeOnly round)
    {
        if (Arrived(job)) return false;
        if (PlanAt(job) is not { } plan) return false;
        var clock = now.TimeOfDay;
        if (clock < round.ToTimeSpan() || clock >= round.ToTimeSpan().Add(TimeSpan.FromMinutes(10))) return false;
        var roundAt = new DateTimeOffset(now.Year, now.Month, now.Day, round.Hour, round.Minute, 0, now.Offset);
        return plan <= roundAt;
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
        // A job the register already shows at the site, or with a date and
        // no time, is asked for the time — the truck is not in question.
        var atSite = Formats.Clean(job.ArrDate).Length > 0
            || LineAuthority.Rank(job.Category, job.Status) >= LineAuthority.Rank(job.Category, LineAuthority.ResolveSite(job.Category, LineParser.SiteArrival));
        var when = plan is null ? ""
            : stage == Before
            ? $"แผน {plan.Value:HH:mm} — อีก {Math.Max(1, (int)Math.Round((plan.Value - now).TotalMinutes))} นาที รถออกแล้วหรือยังครับ"
            : atSite
            ? "ยังไม่มีเวลาถึงในระบบ — รถถึงหน้างานกี่โมงครับ"
            : $"แผน {plan.Value:HH:mm} — เลยมา {Elapsed(now - plan.Value)} ยังไม่มีรายงานถึง";
        // A job still short of its truck is asked for that on the same line,
        // so the room is not written to twice.
        if (MissingDetails(job) is { Count: > 0 } lacking)
            when += $" · ขาด: {string.Join(", ", lacking)}";
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
    /// The message for one room: the jobs due now, in plan-time order, and
    /// how to answer so the answer lands on the job. Empty when nothing is due.
    /// </summary>
    public static string Compose(string supplier, IReadOnlyList<(LineReminder.JobLine Job, string Stage)> due, DateTimeOffset now)
    {
        if (due.Count == 0) return "";
        var ordered = due.OrderBy(one => PlanAt(one.Job) ?? DateTimeOffset.MaxValue).ToList();

        var rounds = ordered.Any(one => IsRoundStage(one.Stage));
        var details = ordered.Any(one => MissingDetails(one.Job).Count > 0);
        var text = new StringBuilder($"ติดตามสถานะรถ {now:HH:mm} — {supplier}");
        text.Append(rounds ? "\nงานที่ยังไม่มีเวลาถึงในระบบ — รถถึงหน้างานหรือยังครับ" : "\nใกล้เวลาแผนแล้ว — รถออกแล้วหรือยังครับ");
        var number = 0;
        foreach (var (job, stage) in ordered)
        {
            number++;
            text.Append("\n\n").Append(Line(number, job, stage, now));
        }
        text.Append("\n\nตอบในกลุ่มนี้: <เลขตู้ หรือ Job No.> ถึงโรงงาน HH:MM — เช่น TXGU8142057 ถึงโรงงาน 12:40\nถ้ายังไม่ถึง: <เลขตู้> คาดถึง HH:MM");
        if (details) text.Append("\nทะเบียนรถและคนขับ: <เลขตู้ หรือ Job No.> ทะเบียน ชื่อ-สกุลคนขับ เบอร์ — เช่น TXGU8142057 70-1234 สมชาย ใจดี 081-2345678");
        return text.ToString();
    }
}
