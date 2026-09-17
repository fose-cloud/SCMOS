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
    /// The job has no LICENCE or no DRIVER yet. Asked for on the same
    /// two-hour spacing as the arrival, from the last word to the room —
    /// "หาก LICENCE หรือ DRIVER ยังไม่มีการลงข้อมูล ให้ส่งข้อความ … ทุกๆ 2 ชั่วโมง",
    /// 17 Sep 2026, imports and exports alike. Repeats are "details#2",
    /// "details#3"…
    /// </summary>
    public const string Details = "details";

    /// <summary>Whether a stage is the truck-details ask.</summary>
    public static bool IsDetailsStage(string stage) => stage == Details || stage.StartsWith(Details + "#", StringComparison.Ordinal);

    /// <summary>
    /// The two cells the details ask is about — LICENCE and DRIVER — in the
    /// words it asks with; empty when both are filled. Imports and exports
    /// only: a Domestic run is the company's own fleet.
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
    /// Which details ask a job is due right now, or null: the first once the
    /// day's asking hour has come (<paramref name="earliest"/>, the morning
    /// reminder's), then one every <paramref name="repeatHours"/> after the
    /// latest word to the room about the job — up to the day's limit — while
    /// LICENCE or DRIVER is still empty and the job is open.
    /// </summary>
    /// <param name="asked">How many details asks this job has had today.</param>
    /// <param name="lastAsked">When the room was last told about this job today — a reminder or any ask — or null.</param>
    public static string? DetailsStage(LineReminder.JobLine job, DateTimeOffset now, int repeatHours,
        int asked, DateTimeOffset? lastAsked, TimeOnly earliest)
    {
        if (repeatHours <= 0 || MissingDetails(job).Count == 0) return null;
        if (string.Equals(job.Status, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)
            || string.Equals(job.Status, JobStatus.Hold, StringComparison.OrdinalIgnoreCase)
            || string.Equals(job.Status, JobStatus.Completed, StringComparison.OrdinalIgnoreCase)) return null;
        if (TimeOnly.FromDateTime(now.DateTime) < earliest) return null;
        if (asked > MaxRepeats) return null;
        var spaced = lastAsked is null || now >= lastAsked.Value + TimeSpan.FromHours(repeatHours);
        if (!spaced) return null;
        return asked <= 0 ? Details : $"{Details}#{asked + 1}";
    }

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
    /// The stage for a haulier's own estimate: "ประมาณ 10.00 รถถึงโรงงาน" is
    /// asked about at 10:00 — did it? — once, whatever the plan-time stages
    /// are doing (asked for 17 Sep 2026). Named by the clock, so a later
    /// estimate is a new ask.
    /// </summary>
    public static string EtaStage(DateTimeOffset eta) => $"eta:{eta:HH:mm}";

    /// <summary>Whether a stage is an estimate's ask.</summary>
    public static bool IsEtaStage(string stage) => stage.StartsWith("eta:", StringComparison.Ordinal);

    /// <summary>
    /// Whether an estimate is due its ask: the clock has come, the estimate
    /// was ahead of the message that gave it (one already past when sent is
    /// nothing to wait for), and the truck is still unreported.
    /// </summary>
    public static bool EtaDue(LineReminder.JobLine job, DateTimeOffset eta, DateTimeOffset saidAt, DateTimeOffset now) =>
        now >= eta && eta > saidAt && !Arrived(job);

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
    /// Which stage a job is due at right now, or null.
    ///
    /// <para>
    /// <see cref="Overdue"/> once the plan time is <paramref name="minutes"/>
    /// behind and nothing has been asked yet; then a repeat, "overdue#2",
    /// "overdue#3"…, each <paramref name="repeatHours"/> after the latest
    /// word to the room about this job — the morning reminder or the last
    /// ask, whichever was later (<paramref name="lastAsked"/>), which is how
    /// the department set it on 17 Sep 2026: "ทุกๆ 2 ชั่วโมง นับจากการแจ้งเตือน
    /// และติดตามล่าสุด". Never inside the two hours after a reminder, and never
    /// before the plan time is the margin behind. <see cref="Before"/> inside
    /// <paramref name="beforeMinutes"/> ahead of the plan time when that is on.
    /// A job with no plan time, or one whose arrival is written, is due nothing.
    /// </para>
    /// </summary>
    /// <param name="asked">How many asks after the plan time this job has had today.</param>
    /// <param name="lastAsked">When the room was last told about this job today — a reminder or an ask — or null.</param>
    public static string? Stage(LineReminder.JobLine job, DateTimeOffset now, int minutes,
        int repeatHours = DefaultRepeatHours, int beforeMinutes = DefaultBeforeMinutes,
        int asked = 0, DateTimeOffset? lastAsked = null)
    {
        if (minutes <= 0 || Arrived(job)) return null;
        if (PlanAt(job) is not { } plan) return null;
        var margin = TimeSpan.FromMinutes(minutes);
        if (now >= plan + margin)
        {
            var spaced = lastAsked is null || now >= lastAsked.Value + TimeSpan.FromHours(Math.Max(repeatHours, 0));
            if (asked <= 0) return spaced ? Overdue : null;
            if (repeatHours <= 0 || asked > MaxRepeats) return null;
            return spaced ? $"{Overdue}#{asked + 1}" : null;
        }
        if (beforeMinutes > 0 && now >= plan - TimeSpan.FromMinutes(beforeMinutes) && now < plan) return Before;
        return null;
    }

    /// <summary>Which ask this is: 1 for the first overdue or details ask, 2 for the first repeat…</summary>
    public static int AskNumber(string stage) =>
        stage == Before ? 0
        : stage == Overdue || stage == Details ? 1
        : int.TryParse(stage.Replace(Overdue + "#", "").Replace(Details + "#", ""), out var ask) && ask > 1 ? ask
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
        // A job the register already shows at the site, or with a date and
        // no time, is asked for the time — the truck is not in question.
        var atSite = Formats.Clean(job.ArrDate).Length > 0
            || LineAuthority.Rank(job.Category, job.Status) >= LineAuthority.Rank(job.Category, LineAuthority.ResolveSite(job.Category, LineParser.SiteArrival));
        var when = IsDetailsStage(stage)
            ? $"ยังไม่มี{string.Join("และ", MissingDetails(job))}ในระบบ — ขอ{string.Join("และ", MissingDetails(job))}ครับ"
              + (stage == Details ? "" : $" (ติดตามครั้งที่ {AskNumber(stage)})")
            : IsEtaStage(stage)
            ? $"แจ้งไว้ว่าคาดถึง {stage[4..]} — ถึงโรงงานแล้วหรือยังครับ"
            : atSite && stage != Before
            ? $"ยังไม่มีเวลาถึงในระบบ — รถถึงหน้างานกี่โมงครับ" + (stage == Overdue ? "" : $" (ติดตามครั้งที่ {AskNumber(stage)})")
            : plan is null ? "" : stage == Before
            ? $"แผน {plan.Value:HH:mm} — อีก {Math.Max(1, (int)Math.Round((plan.Value - now).TotalMinutes))} นาที"
            : $"แผน {plan.Value:HH:mm} — เลยมา {Elapsed(now - plan.Value)} ยังไม่มีรายงานถึง"
              + (stage == Overdue ? "" : $" (ติดตามครั้งที่ {AskNumber(stage)})");
        // An arrival ask on a job still short of its truck asks for that too,
        // so one line carries both and the room is not written to twice.
        if (!IsDetailsStage(stage) && MissingDetails(job) is { Count: > 0 } lacking)
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

        var arrivals = ordered.Any(one => !IsDetailsStage(one.Stage));
        var details = ordered.Any(one => IsDetailsStage(one.Stage) || MissingDetails(one.Job).Count > 0);
        var text = new StringBuilder($"ติดตามสถานะรถ {now:HH:mm} — {supplier}");
        if (arrivals) text.Append("\nรถถึงหน้างานหรือยังครับ");
        var number = 0;
        foreach (var (job, stage) in ordered)
        {
            number++;
            text.Append("\n\n").Append(Line(number, job, stage, now));
        }
        text.Append("\n\nตอบในกลุ่มนี้:");
        if (arrivals) text.Append(" <เลขตู้ หรือ Job No.> ถึงโรงงาน HH:MM — เช่น TXGU8142057 ถึงโรงงาน 12:40\nถ้ายังไม่ถึง: <เลขตู้> คาดถึง HH:MM");
        if (details) text.Append(arrivals ? "\nทะเบียนรถและคนขับ: " : " ").Append("<เลขตู้ หรือ Job No.> ทะเบียน ชื่อ-สกุลคนขับ เบอร์ — เช่น TXGU8142057 70-1234 สมชาย ใจดี 081-2345678");
        return text.ToString();
    }
}
