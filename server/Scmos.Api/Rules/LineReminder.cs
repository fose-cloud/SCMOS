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
    /// <summary>The hour the department chose, Bangkok. Configuration may move it.</summary>
    public const string DefaultTime = "08:00";

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
        string Date = "", string ArrDate = "", string ArrTime = "");

    /// <summary>The three cells the message asks for, in the words it asks with.</summary>
    public static IReadOnlyList<string> Missing(JobLine job)
    {
        var gaps = new List<string>(3);
        if (Formats.Clean(job.Licence).Length == 0) gaps.Add("ทะเบียนรถ");
        if (Formats.Clean(job.Driver).Length == 0) gaps.Add("ชื่อ-สกุลคนขับ");
        if (Formats.Clean(job.Contact).Length == 0) gaps.Add("เบอร์ติดต่อ");
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
            + "เช่น TXGU8142057 70-1234 สมชาย ใจดี 081-2345678";

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
