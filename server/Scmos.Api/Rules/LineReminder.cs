using System.Text;

namespace Scmos.Api.Rules;

/// <summary>
/// The morning message to a haulier's group: today's jobs that still have no
/// truck on them.
///
/// <para>
/// Asked for on 16 Sep 2026: "ให้ LINE ดึงข้อมูลงานที่ยังไม่มีข้อมูล ทะเบียนรถ
/// ชื่อ-สกุลคนขับรถ เบอร์ติดต่อ สำหรับงาน Today ส่งแจ้งเตือนไปยัง Line Group" — at
/// 08:00. What each job is named by is the department's: an import by its
/// job number, container, customer and delivery place; an export by its
/// booking, job number, container, customer, loading plant and the port
/// yard the empty goes back to. A Domestic run, which the request did not
/// name, is written the way the grid names it.
/// </para>
///
/// <para>
/// Pure: a list of jobs in, a list of messages out. <c>--check-line</c>
/// proves the wording and the split, and nothing here knows about LINE.
/// </para>
/// </summary>
public static class LineReminder
{
    /// <summary>
    /// The hour the department chose, Bangkok: one ask for the trucks'
    /// details at nine — "การติดตามรายชื่อพนักงานขับรถ กำหนดให้เป็นรอบ 09.00 น.
    /// เท่านั้น", 17 Sep 2026, after a day on which the morning's ask, the
    /// noon's and a two-hourly chase all counted against LINE's monthly
    /// allowance. Configuration may move it — a comma-separated list, or "off".
    /// </summary>
    public const string DefaultTime = "09:00";

    /// <summary>
    /// The clock times out of a setting: "08:00, 12:00", "8.00 12.00", or
    /// "off" — empty when off, the default when the setting is absent, blank,
    /// or not a time. Blank is the default and not "off": on 16 Sep 2026 the
    /// setting was added to the Portal with no value, which switched the
    /// morning off until this said otherwise.
    /// </summary>
    public static IReadOnlyList<TimeOnly> Times(string? setting)
    {
        var text = (setting ?? "").Trim();
        if (text.Length == 0) text = DefaultTime;
        if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) return [];
        var times = new List<TimeOnly>();
        foreach (var part in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TimeOnly.TryParseExact(part.Replace('.', ':'), "H:mm", null, System.Globalization.DateTimeStyles.None, out var at)
                && !times.Contains(at))
                times.Add(at);
        }
        if (times.Count == 0 && setting is not null) return Times(null);
        times.Sort();
        return times;
    }

    /// <summary>LINE refuses a text over 5,000 characters; well under it, per message.</summary>
    public const int MaxChars = 4200;

    /// <summary>One job as the message needs it — the cells only, no whole record.</summary>
    public record JobLine(
        string Key, string Category, string Status,
        string Customer, string JobCode, string Booking, string Container,
        string Destination, string Plant, string ReturnLoc, string PlanTime,
        string Licence, string Driver, string Contact,
        /// <summary>The Domestic grid's own references, for a run that has no booking.</summary>
        string JobNo = "", string Warehouse = "", string Province = "",
        /// <summary>The plan date and the arrival stamp, for the status chase — see <see cref="LineChase"/>.</summary>
        string Date = "", string ArrDate = "", string ArrTime = "",
        /// <summary>The SEAL NO. cell — asked for on an export once the box is loaded (17 Sep 2026).</summary>
        string Seal = "");

    /// <summary>
    /// The cells the message asks for, in the words it asks with: the truck's
    /// three, and on an export the box and the seal as well — an export's
    /// CONTAINER NO. and SEAL NO. are known only once the empty is picked up
    /// and the box loaded, and the haulier is who knows them (17 Sep 2026).
    /// </summary>
    public static IReadOnlyList<string> Missing(JobLine job)
    {
        var gaps = new List<string>(5);
        if (Formats.Clean(job.Licence).Length == 0) gaps.Add("ทะเบียนรถ");
        if (Formats.Clean(job.Driver).Length == 0) gaps.Add("ชื่อ-สกุลคนขับ");
        if (Formats.Clean(job.Contact).Length == 0) gaps.Add("เบอร์ติดต่อ");
        if (string.Equals(job.Category, "EXPORT", StringComparison.OrdinalIgnoreCase))
        {
            if (Formats.Clean(job.Container).Length == 0) gaps.Add("เลขตู้");
            if (Formats.Clean(job.Seal).Length == 0) gaps.Add("เลขซีล");
        }
        return gaps;
    }

    /// <summary>
    /// Whether a job belongs in the message at all: not finished, not
    /// cancelled, not parked — and short of something.
    /// </summary>
    public static bool Wanted(JobLine job) =>
        !string.Equals(job.Status, JobStatus.Completed, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(job.Status, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(job.Status, JobStatus.Hold, StringComparison.OrdinalIgnoreCase)
        && Missing(job).Count > 0;

    /// <summary>One job, on one or two lines, as the haulier reads it.</summary>
    public static string Line(int number, JobLine job)
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
                parts.Add($"คืนตู้ที่ {Or(job.ReturnLoc)}");
                break;
            case "DELIVERY":
                parts.Add($"Job {Or(job.JobNo)}");
                parts.Add($"คลัง {Or(job.Warehouse)}");
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
        if (Formats.Clean(job.PlanTime).Length > 0) parts.Add($"เวลา {Formats.Clean(job.PlanTime)}");
        return $"{number}) {string.Join(" · ", parts)}\n   ขาด: {string.Join(", ", Missing(job))}";
    }

    /// <summary>
    /// The message, or messages: a heading, the jobs by category in plan-time
    /// order, and how to answer so the answer can be read back. Split when a
    /// day's list would not fit one LINE text; each part is whole on its own.
    /// Empty when nothing is missing — then nothing is sent.
    /// </summary>
    public static IReadOnlyList<string> Compose(string supplier, DateOnly day, IReadOnlyList<JobLine> jobs)
    {
        var wanted = jobs.Where(Wanted)
            .OrderBy(job => CategoryOrder(job.Category))
            .ThenBy(job => Formats.TimeMinutes(job.PlanTime) ?? int.MaxValue)
            .ThenBy(job => job.Customer, StringComparer.Ordinal)
            .ToList();
        if (wanted.Count == 0) return [];

        var heading = $"งานวันนี้ {Formats.PlanDate(day)} — {supplier}\n"
            + "ขอทะเบียนรถ ชื่อ-สกุลคนขับ และเบอร์ติดต่อ สำหรับงานต่อไปนี้ครับ";
        // The first real message, 16 Sep 2026, listed five boxes on one job
        // number — a booking is several containers — so the container is what
        // an answer should lead with; the job number alone would leave the
        // operator choosing between five rows.
        var footer = "ตอบในกลุ่มนี้ทีละตู้: <เลขตู้ หรือ Job No. / Booking> ทะเบียน ชื่อ-สกุลคนขับ เบอร์\n"
            + "เช่น TXGU8142057 70-1234 สมชาย ใจดี 081-2345678\n"
            + "งาน Export ที่ยังไม่มีเลขตู้/ซีล: <Booking> ตู้ XXXU1234567 ซีล 123456";

        var messages = new List<string>();
        var text = new StringBuilder(heading);
        var category = "";
        var number = 0;
        foreach (var job in wanted)
        {
            number++;
            var block = new StringBuilder();
            var thisCategory = job.Category.ToUpperInvariant();
            if (thisCategory != category)
            {
                block.Append("\n\n").Append(CategoryName(thisCategory));
                category = thisCategory;
            }
            block.Append('\n').Append(Line(number, job));

            if (text.Length + block.Length + footer.Length + 2 > MaxChars)
            {
                messages.Add(text.ToString());
                text = new StringBuilder($"งานวันนี้ {Formats.PlanDate(day)} — {supplier} (ต่อ)");
                // The heading of the category is repeated on a new part.
                block.Clear().Append("\n\n").Append(CategoryName(thisCategory)).Append('\n').Append(Line(number, job));
            }
            text.Append(block);
        }
        text.Append("\n\n").Append(footer);
        messages.Add(text.ToString());
        return messages;
    }

    /// <summary>
    /// The days the summary sent on <paramref name="today"/> is about.
    /// Tomorrow, on most days. On a Friday the weekend and the Monday in one
    /// message — "ทุกๆ วันศุกร์ จะต้องส่งสรุปงานของวันเสาร์ วันอาทิตย์ และวันจันทร์
    /// ในเวลา 16.00 น." (20 Sep 2026). A Saturday and a Sunday still name
    /// tomorrow, and the ledger is what keeps them quiet: a room Friday's
    /// message already covered is not sent the same day again, while a room
    /// Friday had nothing for — or a weekend before this rule — still hears
    /// about a job that has since appeared.
    /// </summary>
    public static IReadOnlyList<DateOnly> SummaryDays(DateOnly today) => today.DayOfWeek switch
    {
        DayOfWeek.Friday => [today.AddDays(1), today.AddDays(2), today.AddDays(3)],
        _ => [today.AddDays(1)],
    };

    /// <summary>The day of the week as the room reads it: "เสาร์", "จันทร์".</summary>
    public static string ThaiDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "อาทิตย์",
        DayOfWeek.Monday => "จันทร์",
        DayOfWeek.Tuesday => "อังคาร",
        DayOfWeek.Wednesday => "พุธ",
        DayOfWeek.Thursday => "พฤหัสบดี",
        DayOfWeek.Friday => "ศุกร์",
        _ => "เสาร์",
    };

    /// <summary>
    /// The span a summary is about, for its heading and the screen: one
    /// day's date, or "26/09/2026 – 28/09/2026 (เสาร์–จันทร์)".
    /// </summary>
    public static string SpanLabel(IReadOnlyList<DateOnly> days)
    {
        if (days.Count == 0) return "";
        if (days.Count == 1) return Formats.PlanDate(days[0]);
        var first = days[0];
        var last = days[^1];
        return $"{Formats.PlanDate(first)} – {Formats.PlanDate(last)} ({ThaiDay(first.DayOfWeek)}–{ThaiDay(last.DayOfWeek)})";
    }

    /// <summary>
    /// The day-before summary: every job of the haulier's planned for
    /// <paramref name="day"/> — not only the ones short of something — sent
    /// the afternoon before, so the room can line up its trucks. Asked for on
    /// 17 Sep 2026: "สรุปงานวันที่ 18/09/2026, ส่ง 17/09/2026 เวลา 16:00". A job
    /// still short of its truck says so on its line, so the answer can come
    /// tonight. Cancelled and finished jobs are left out; a day with none
    /// sends nothing. Split like the reminder when it would not fit.
    /// </summary>
    public static IReadOnlyList<string> ComposeSummary(string supplier, DateOnly day, IReadOnlyList<JobLine> jobs) =>
        ComposeSummary(supplier, [(day, jobs)]);

    /// <summary>
    /// The summary over several days in one message — Friday's, for the
    /// weekend and the Monday. Each day is its own section, headed with the
    /// day and its count, the jobs numbered straight through so a reply by
    /// number is not ambiguous; a day with nothing on it says so, since
    /// "no work on Sunday" is what a haulier plans by. Nothing at all when
    /// no day has an open job. A single day reads exactly as before.
    /// </summary>
    public static IReadOnlyList<string> ComposeSummary(string supplier, IReadOnlyList<(DateOnly Day, IReadOnlyList<JobLine> Jobs)> days)
    {
        var perDay = days.Select(one => (one.Day, Jobs: (IReadOnlyList<JobLine>)one.Jobs
            .Where(job => !string.Equals(job.Status, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(job.Status, JobStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(job => CategoryOrder(job.Category))
            .ThenBy(job => Formats.TimeMinutes(job.PlanTime) ?? int.MaxValue)
            .ThenBy(job => job.Customer, StringComparer.Ordinal)
            .ToList())).ToList();
        var total = perDay.Sum(one => one.Jobs.Count);
        if (total == 0) return [];

        var span = SpanLabel(perDay.Select(one => one.Day).ToList());
        var heading = $"สรุปงานวันที่ {span} — {supplier} · {total} งาน";
        var footer = "ถ้างานไหนรับไม่ได้ หรือข้อมูลไม่ตรง แจ้งในกลุ่มนี้ครับ\n"
            + "ทะเบียนรถและคนขับส่งได้เลย: <เลขตู้ หรือ Booking> ทะเบียน ชื่อ-สกุลคนขับ เบอร์ — เช่น TXGU8142057 70-1234 สมชาย ใจดี 081-2345678";

        var messages = new List<string>();
        var text = new StringBuilder(heading);
        var number = 0;
        foreach (var (day, listed) in perDay)
        {
            // A day's own heading only when there is more than one day.
            var dayHead = perDay.Count > 1
                ? $"\n\n■ {ThaiDay(day.DayOfWeek)} {Formats.PlanDate(day)} · {(listed.Count > 0 ? $"{listed.Count} งาน" : "ไม่มีงาน")}"
                : "";
            if (dayHead.Length > 0)
            {
                if (text.Length + dayHead.Length + footer.Length + 2 > MaxChars)
                {
                    messages.Add(text.ToString());
                    text = new StringBuilder($"สรุปงานวันที่ {span} — {supplier} (ต่อ)");
                }
                text.Append(dayHead);
            }
            var category = "";
            foreach (var job in listed)
            {
                number++;
                var block = new StringBuilder();
                var thisCategory = job.Category.ToUpperInvariant();
                if (thisCategory != category)
                {
                    block.Append("\n\n").Append(CategoryName(thisCategory));
                    category = thisCategory;
                }
                block.Append('\n').Append(SummaryLine(number, job));

                if (text.Length + block.Length + footer.Length + 2 > MaxChars)
                {
                    messages.Add(text.ToString());
                    text = new StringBuilder($"สรุปงานวันที่ {span} — {supplier} (ต่อ)");
                    if (dayHead.Length > 0) text.Append(dayHead.Replace(" · ", " (ต่อ) · "));
                    block.Clear().Append("\n\n").Append(CategoryName(thisCategory)).Append('\n').Append(SummaryLine(number, job));
                }
                text.Append(block);
            }
        }
        text.Append("\n\n").Append(footer);
        messages.Add(text.ToString());
        return messages;
    }

    /// <summary>One job in the summary: the reminder's line, and what is still missing only when something is.</summary>
    private static string SummaryLine(int number, JobLine job)
    {
        var line = Line(number, job);
        var cut = line.LastIndexOf("\n   ขาด: ", StringComparison.Ordinal);
        var head = cut < 0 ? line : line[..cut];
        var missing = Missing(job);
        return missing.Count == 0 ? head : $"{head}\n   ยังขาด: {string.Join(", ", missing)}";
    }

    private static int CategoryOrder(string category) => category.ToUpperInvariant() switch
    {
        "IMPORT" => 0,
        "EXPORT" => 1,
        _ => 2,
    };

    private static string CategoryName(string category) => category switch
    {
        "IMPORT" => "IMPORT",
        "EXPORT" => "EXPORT",
        "DELIVERY" => "DOMESTIC",
        _ => category,
    };
}
