using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>One line of a carrier's scorecard.</summary>
/// <param name="Id">Stable key, so the screen and a spreadsheet agree.</param>
/// <param name="Weight">Its share of the hundred, as agreed with the customer.</param>
/// <param name="Percent">The score out of a hundred, or null when it cannot be measured.</param>
/// <param name="Count">What went wrong — accidents, late reports, complaints.</param>
/// <param name="Base">What it was measured against — shipments, or reports due.</param>
public record ScoreLine(
    string Id, string English, string Thai, double Weight,
    double? Percent, int Count, int Base, double Target, string Note);

/// <param name="Weighted">The weighted total, out of the weight actually available.</param>
/// <param name="WeightAvailable">
/// The weight of the criteria that could be measured. Below 100 when something
/// the scorecard asks for is not recorded anywhere, and the total is scaled to
/// it rather than the missing criterion being scored as zero or as full marks.
/// </param>
/// <summary>
/// The counts the customer's own monthly report is laid out in.
///
/// Their form is a tally per carrier — how many of each thing happened — and
/// the weighted score is worked out from it. Both are sent: the tallies are
/// what somebody checks against their own sheet, the score is what the contract
/// is judged on, and showing one without the other means a figure nobody can
/// reconcile.
/// </summary>
public record CarrierTally(
    int TransportAccidentMajor,
    int TransportAccidentMinor,
    int LoadingAccident,
    int Complaints,
    int BreakdownNoComplaint);

public record CarrierScore(
    string Carrier, int Shipments,
    IReadOnlyList<ScoreLine> Lines,
    double? Weighted, double WeightAvailable,
    int UngradedAccidents,
    CarrierTally Tally);

/// <summary>
/// The carrier scorecard the customer's contract is judged on.
///
/// Five criteria and their weights come from the agreement, not from here:
/// accidents at 15% minor and 35% major, damage reporting at 20%, vehicle
/// readiness at 10%, on-time delivery at 10%, customer satisfaction at 10%.
///
/// Four of the five are stated in the agreement as a rate of things going
/// wrong — accidents over shipments, complaints over shipments — against a
/// target of a hundred percent. A rate of failures cannot be compared to a
/// target of perfection, so each is scored as a hundred less that rate. The
/// fifth, damage reporting, is stated the other way round already: reports made
/// inside the deadline over reports due, which is a score as it stands.
///
/// Everything is attributed through the job. An operational issue names a job,
/// the job names the carrier, and that is the only chain that exists — an issue
/// whose written reference never matched a job cannot be laid at anybody's
/// door, so it is counted for the company and reported separately rather than
/// distributed among carriers who may have had nothing to do with it.
/// </summary>
public static class CarrierScorecard
{
    /// <summary>The category an accident is logged under in the issue register.</summary>
    private const string AccidentCategory = "ความปลอดภัย/อุบัติเหตุ";

    /// <summary>The category a damage or discrepancy report is logged under.</summary>
    private const string DamageCategory = "สินค้าชำรุด/สูญหาย";

    /// <summary>The category a lorry that would not run is logged under.</summary>
    private const string BreakdownCategory = "รถ/อุปกรณ์ไม่พร้อม";

    /// <summary>
    /// Where a complaint comes from, inside the company and outside it.
    ///
    /// The report column reads "Complaint (Internal &amp; external)", so both
    /// halves count: the customer complaining is external, and CS, shipping,
    /// billing or the warehouse raising it is internal. Customs, the depot and
    /// the forwarder are none of those — a customs hold is a fact about the
    /// shipment, not somebody complaining about the haulier — so they are left
    /// out.
    /// </summary>
    private static readonly HashSet<string> ComplaintSources =
        new(StringComparer.OrdinalIgnoreCase)
        { "Customer", "CS", "CS/Shipping", "Shipping", "Billing", "Warehouse" };

    /// <summary>Minutes allowed to report: five for an accident, thirty otherwise.</summary>
    private const int AccidentReportMinutes = 5;
    private const int ReportMinutes = 30;

    /// <summary>Late by more than this and the shipment is not on time.</summary>
    private const int LateMinutes = 30;

    public static IReadOnlyList<CarrierScore> Build(
        IReadOnlyList<(string Key, string Carrier, JobRecord Record)> jobs,
        IReadOnlyList<OperationalIssue> issues,
        IReadOnlyList<PreRunCheck> preRuns)
    {
        var carrierOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var job in jobs) carrierOf[job.Key] = job.Carrier;

        // Only issues that reach a job in this period. The rest are real and are
        // reported elsewhere; they are simply not anybody's score.
        var attributed = issues
            .Where(issue => issue.JobKey.Length > 0 && carrierOf.ContainsKey(issue.JobKey))
            .ToList();

        var byCarrier = jobs.GroupBy(job => job.Carrier)
            .Where(group => group.Key.Length > 0)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var scores = new List<CarrierScore>();
        foreach (var group in byCarrier)
        {
            var shipments = group.Count();
            var keys = group.Select(job => job.Key).ToHashSet(StringComparer.Ordinal);
            var mine = attributed.Where(issue => keys.Contains(issue.JobKey)).ToList();

            var accidents = mine.Where(IsAccident).ToList();
            var minor = accidents.Count(issue => Graded(issue) == "Transport (Minor)");
            var major = accidents.Count(issue => Graded(issue) == "Transport (Major)");
            var loading = accidents.Count(issue => Graded(issue) == "Loading");
            var ungraded = accidents.Count(issue => Graded(issue).Length == 0);

            // A lorry that would not run, where the customer never complained
            // about it. The pairing is the point: a breakdown the customer felt
            // is already counted as a complaint, and counting it twice would
            // punish one event under two headings.
            var breakdownNoComplaint = mine.Count(issue =>
                IsBreakdown(issue)
                && !mine.Any(other => other.JobKey == issue.JobKey && IsComplaint(other)));

            var complaints = mine.Count(IsComplaint);

            var reports = mine.Where(issue => IsAccident(issue) || IsDamage(issue)).ToList();
            var onTimeReports = reports.Count(ReportedInTime);

            var lateWithComplaint = group.Count(job =>
                LateBeyond(job.Record, LateMinutes)
                && mine.Any(issue => issue.JobKey == job.Key && IsComplaint(issue)));

            var lines = new List<ScoreLine>
            {
                Rate("accident-minor", "Transport Accident (Minor)", "อุบัติเหตุระหว่างขนส่ง (เล็กน้อย)",
                    15, minor, shipments, 100,
                    ungraded > 0 ? $"ยังไม่ระบุระดับ {ungraded} เคส — ไม่ถูกนับในคะแนน" : ""),

                Rate("accident-major", "Transport Accident (Major)", "อุบัติเหตุระหว่างขนส่ง (ใหญ่)",
                    35, major, shipments, 100,
                    ungraded > 0 ? $"ยังไม่ระบุระดับ {ungraded} เคส — ไม่ถูกนับในคะแนน" : ""),

                // Already a score rather than a failure rate: reports made
                // inside the deadline, over reports that were due.
                new("damage-reporting", "Cargo damage & Discrepancy Reporting",
                    "รายงานความเสียหายภายในเวลา", 20,
                    reports.Count == 0 ? null : Round(onTimeReports * 100.0 / reports.Count),
                    onTimeReports, reports.Count, 100,
                    reports.Count == 0
                        ? "ไม่มีรายงานความเสียหายในเดือนนี้"
                        : $"ในกำหนด {onTimeReports} จาก {reports.Count} รายงาน · อุบัติเหตุ {AccidentReportMinutes} นาที · อื่นๆ {ReportMinutes} นาที"),

                // Not measured, and not substituted. The pre-run check on file
                // is the carrier confirming which lorry will turn up, which is a
                // different question from whether that lorry passed a safety
                // inspection. Scoring one as the other would put ten percent of
                // a carrier's mark on a measurement nobody took.
                new("vehicle-readiness", "Safety readiness of transport vehicles",
                    "ความพร้อมด้านความปลอดภัยของรถ", 10,
                    null, 0, preRuns.Count(check => Same(check.Carrier, group.Key)), 100,
                    "ยังไม่มีบันทึกผลตรวจความพร้อมรถ (ผ่าน/ไม่ผ่าน) ในระบบ — เกณฑ์นี้ยังคิดคะแนนไม่ได้"),

                Rate("on-time", "On time delivery", "ส่งมอบตรงเวลา",
                    10, lateWithComplaint, shipments, 95,
                    $"ช้ากว่านัดเกิน {LateMinutes} นาที และมีข้อร้องเรียน"),

                Rate("satisfaction", "Complaint (Internal & external)", "ข้อร้องเรียน (ภายใน/ภายนอก)",
                    10, complaints, shipments, 95,
                    "ข้อร้องเรียนจากลูกค้า และจากภายใน (CS · Shipping · Billing · คลัง)"),
            };

            var measured = lines.Where(line => line.Percent is not null).ToList();
            var available = measured.Sum(line => line.Weight);
            double? weighted = available <= 0
                ? null
                : Round(measured.Sum(line => line.Percent!.Value * line.Weight) / available);

            scores.Add(new CarrierScore(group.Key, shipments, lines, weighted, available, ungraded,
                new CarrierTally(major, minor, loading, complaints, breakdownNoComplaint)));
        }

        return scores;
    }

    /// <summary>
    /// A criterion stated as a rate of failures, turned into a score.
    ///
    /// The agreement writes these as "(count / shipments) × 100" against a
    /// target of 100%. Read literally that asks a carrier to have accidents on
    /// every shipment, which is plainly not what it means: the target is
    /// perfection, so the score is a hundred less the failure rate. Floored at
    /// zero, because a carrier cannot be worse than no marks.
    /// </summary>
    private static ScoreLine Rate(string id, string english, string thai, double weight,
        int count, int shipments, double target, string note)
    {
        double? percent = shipments == 0 ? null : Round(Math.Max(0, 100 - (count * 100.0 / shipments)));
        return new ScoreLine(id, english, thai, weight, percent, count, shipments, target,
            shipments == 0 ? "ไม่มี shipment ในเดือนนี้" : note);
    }

    private static bool IsAccident(OperationalIssue issue) =>
        issue.Category.Trim().Equals(AccidentCategory, StringComparison.Ordinal);

    private static bool IsDamage(OperationalIssue issue) =>
        issue.Category.Trim().Equals(DamageCategory, StringComparison.Ordinal);

    private static bool IsBreakdown(OperationalIssue issue) =>
        issue.Category.Trim().Equals(BreakdownCategory, StringComparison.Ordinal);

    /// <summary>
    /// An issue that is somebody complaining, rather than one that merely came
    /// in through them.
    ///
    /// The source says who reported it and the category says what it was, and
    /// reading the source alone conflates the two: a loading accident reported
    /// by the warehouse is the warehouse doing its job, not the warehouse
    /// complaining. Counted as both, one event lands in the accident column and
    /// the complaint column and the haulier is marked down twice for it.
    ///
    /// So an accident or a breakdown is never a complaint — each already has a
    /// column of its own. Everything else raised by the customer or by CS,
    /// shipping, billing or the warehouse is.
    /// </summary>
    private static bool IsComplaint(OperationalIssue issue) =>
        !IsAccident(issue) && !IsBreakdown(issue)
        && ComplaintSources.Contains(issue.Source.Trim());

    /// <summary>
    /// Which of the three kinds of accident this was, or empty when nobody has
    /// said.
    ///
    /// The customer's report separates a transport accident from one that
    /// happened while loading, and the transport one by how serious it was. All
    /// three are one field on the issue with three values, because they are one
    /// question — what kind of accident was it — and splitting them across two
    /// fields would allow a loading accident marked Major, which their form has
    /// no column for.
    ///
    /// The two older spellings are still read. Anything graded before this
    /// change says "Minor" or "Major", and re-reading those as ungraded would
    /// quietly drop accidents out of a score somebody has already seen.
    /// </summary>
    private static string Graded(OperationalIssue issue)
    {
        var grade = issue.AccidentGrade.Trim();
        if (grade.Equals("loading", StringComparison.OrdinalIgnoreCase)) return "Loading";
        if (grade.Contains("minor", StringComparison.OrdinalIgnoreCase)) return "Transport (Minor)";
        if (grade.Contains("major", StringComparison.OrdinalIgnoreCase)) return "Transport (Major)";
        return "";
    }

    /// <summary>
    /// Whether the report went in inside the time the agreement allows.
    ///
    /// Measured from when the problem was found to when it was recorded here.
    /// An issue with no time of discovery cannot be measured and is counted as
    /// late — not to be harsh, but because the alternative is to treat a missing
    /// timestamp as compliance, and the missing timestamps would then be the
    /// cheapest way to a perfect score.
    /// </summary>
    private static bool ReportedInTime(OperationalIssue issue)
    {
        var found = Formats.Moment(issue.FoundOn, issue.FoundAt);
        if (found is null || issue.CreatedAt == default) return false;

        var allowed = IsAccident(issue) ? AccidentReportMinutes : ReportMinutes;
        var minutes = (issue.CreatedAt - found.Value).TotalMinutes;

        // A report logged before it was found is a clock disagreement, not a
        // fast report; it counts as inside the window rather than being read as
        // a negative delay somebody could game.
        return minutes <= allowed;
    }

    /// <summary>
    /// Whether the shipment arrived more than the allowed minutes after its
    /// plan, using the register's own reading of lateness rather than a second
    /// one written here.
    /// </summary>
    private static bool LateBeyond(JobRecord record, int minutes)
    {
        var late = JobRules.MinutesLate(record);
        return late is not null && late > minutes;
    }

    private static bool Same(string a, string b) =>
        a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
