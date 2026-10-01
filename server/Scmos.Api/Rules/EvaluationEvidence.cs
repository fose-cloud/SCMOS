using Scmos.Api.Data;

namespace Scmos.Api.Rules;

/// <summary>
/// What a carrier's annual evaluation is scored on (1 Oct 2026, Annual Evaluation Phase 3): one carrier's records for
/// the period in, the figures out, each with its formula, its records and whether it could be measured at all.
///
/// <para>
/// Pure, and reading only what SCMOS already records — every rule below is somebody else's rule, reused: on time is
/// <see cref="JobRules.IsMeasurable"/> and <see cref="JobRules.IsOnTime"/> (the KPI engine's, customer grace included);
/// a delay is the carrier's when its <c>DelayRecord.AgainstCarrier</c> says so; an accident's column is
/// <see cref="ScorecardColumn.Of"/>; a certificate's state is <see cref="SupplierCompliance.StateOf"/>.
/// </para>
///
/// <para>
/// Nothing missing is a zero: a rate with no base is <see cref="AnnualEvaluationRules.NotAvailable"/>, one below the
/// campaign's minimum is <see cref="AnnualEvaluationRules.InsufficientData"/>, and what SCMOS does not record at all
/// (fatal accidents, alcohol, a market price benchmark, ISO certificates) says so rather than being invented.
/// </para>
/// </summary>
public static class EvaluationEvidence
{
    public sealed record Job(string Key, JobRecord Record);
    public sealed record Delay(string JobKey, bool AgainstCarrier);
    public sealed record Invoice(string Number, int ReviewCycle, DateTimeOffset? ApprovedAt);
    /// <param name="FirstSubmittedAt">When the carrier first submitted an invoice for the job; null while it has not.</param>
    public sealed record SlaCase(string JobKey, DateTimeOffset DeliveredAt, DateTimeOffset OpenedAt, DateOnly? DueDate, DateTimeOffset? FirstSubmittedAt);

    /// <param name="Jobs">The carrier's jobs dated inside the period, cancelled ones already left out.</param>
    /// <param name="Issues">Operational issues found inside the period and counted against this carrier.</param>
    /// <param name="CaseStages">The stage of every CAR/PAR those issues are linked to.</param>
    /// <param name="Invoices">Invoices the carrier submitted inside the period.</param>
    /// <param name="SlaCases">Billing cases for jobs delivered inside the period.</param>
    /// <param name="JobsWithPod">The job keys that have a POD filed.</param>
    /// <param name="Compliance">The state of each of the register's required documents, one per requirement.</param>
    /// <param name="Today">The day the snapshot is taken, Bangkok — what "not yet due" is read against.</param>
    public sealed record Inputs(
        IReadOnlyList<Job> Jobs, IReadOnlyList<Delay> Delays, IReadOnlyList<OperationalIssue> Issues,
        IReadOnlyDictionary<long, string> CaseStages, IReadOnlyList<Invoice> Invoices, IReadOnlyList<SlaCase> SlaCases,
        IReadOnlySet<string> JobsWithPod, IReadOnlyList<(string Code, string State)> Compliance, int MinimumBase, DateOnly Today);

    public sealed record Metric(string Code, string Status, decimal? Value, decimal? Numerator, decimal? Denominator,
        string Formula, string Note, IReadOnlyList<string> Sources, bool External);

    /// <summary>Which figure each KPI of the campaign is scored from; pricing has none — it is assessed by hand.</summary>
    public static readonly IReadOnlyDictionary<string, string> KpiMetric = new Dictionary<string, string>
    {
        ["otd"] = "carrier-otd", ["safety"] = "incident-rate", ["claim"] = "claim-rate", ["billing"] = "billing-accuracy",
        ["documents"] = "pod-compliance", ["certification"] = "compliance-documents",
    };

    /// <summary>Records kept with a figure: enough to check it by hand, never a dump of the register.</summary>
    public const int MaxSources = 300;

    /// <summary>
    /// A claim, as the department defined it (1 Oct 2026): cargo damaged or lost, or a complaint the customer made.
    /// A complaint from inside the company counts in the scorecard's complaint column but is not a customer claim.
    /// </summary>
    public static bool IsClaim(OperationalIssue issue) =>
        string.Equals(issue.Category.Trim(), ScorecardColumn.DamageCategory, StringComparison.Ordinal)
        || (string.Equals(issue.Source.Trim(), "Customer", StringComparison.OrdinalIgnoreCase)
            && ScorecardColumn.Of(issue) == ScorecardColumn.Complaint);

    /// <summary>Impact points by severity: วิกฤต Critical 5 · สูง Major 3 · ปานกลาง / ต่ำ Minor 1. Unrated counts as Minor and is said so.</summary>
    public static int ClaimPoints(string severity) => severity.Trim() switch { "วิกฤต" => 5, "สูง" => 3, _ => 1 };

    public static IReadOnlyList<Metric> Build(Inputs input)
    {
        var metrics = new List<Metric>();
        var jobs = input.Jobs;
        var completed = jobs.Where(job => JobRules.IsDone(job.Record.Status)).ToList();
        metrics.Add(Count("total-jobs", jobs.Count, "งานในช่วงประเมิน ไม่นับงานที่ยกเลิก", [], external: true));
        metrics.Add(Count("completed-jobs", completed.Count, "งานที่สถานะเสร็จสิ้น (COMPLETED)", [], external: true));

        /* ---- OTD ---- */
        var measured = jobs.Where(job => JobRules.IsMeasurable(job.Record)).ToList();
        var late = measured.Where(job => !JobRules.IsOnTime(job.Record)).ToList();
        var delaysOf = input.Delays.ToLookup(delay => delay.JobKey);
        var lateCarrier = late.Where(job => delaysOf[job.Key].Any(delay => delay.AgainstCarrier)).ToList();
        var lateOther = late.Where(job => delaysOf[job.Key].Any() && !delaysOf[job.Key].Any(delay => delay.AgainstCarrier)).ToList();
        var lateUnclassified = late.Where(job => !delaysOf[job.Key].Any()).ToList();
        metrics.Add(Count("otd-measured", measured.Count, "งานที่มีทั้งเวลาแผนและเวลาถึง", []));
        metrics.Add(Count("late", late.Count, "ถึงช้ากว่าแผนเกินเกณฑ์ (รวมข้อยกเว้นของลูกค้า)", Keys(late)));
        metrics.Add(Count("late-carrier", lateCarrier.Count, "ช้า และสาเหตุเป็นความรับผิดชอบของผู้ขนส่ง", Keys(lateCarrier)));
        metrics.Add(Count("late-other", lateOther.Count, "ช้า แต่สาเหตุไม่ใช่ของผู้ขนส่ง (ลูกค้า ท่าเรือ เอกสาร ฯลฯ)", Keys(lateOther)));
        metrics.Add(Count("late-unclassified", lateUnclassified.Count,
            "ช้า และยังไม่มีใครบันทึกสาเหตุ — ไม่นับเป็นความผิดของผู้ขนส่ง (ตกลง 1 ต.ค. 2026) แสดงแยกให้ผู้พิจารณาเห็น", Keys(lateUnclassified)));
        metrics.Add(Rate("operational-otd", measured.Count - late.Count, measured.Count, input.MinimumBase,
            "ตรงเวลา ÷ งานที่วัดได้ × 100", [], external: false));
        metrics.Add(Rate("carrier-otd", measured.Count - lateCarrier.Count, measured.Count, input.MinimumBase,
            "(งานที่วัดได้ − ช้าเพราะผู้ขนส่ง) ÷ งานที่วัดได้ × 100", Keys(lateCarrier), external: true));

        /* ---- safety ---- */
        var major = input.Issues.Where(issue => ScorecardColumn.Of(issue) == ScorecardColumn.TransportMajor).ToList();
        var minor = input.Issues.Where(issue => ScorecardColumn.Of(issue) == ScorecardColumn.TransportMinor).ToList();
        var loading = input.Issues.Where(issue => ScorecardColumn.Of(issue) == ScorecardColumn.LoadingAccident).ToList();
        var ungraded = input.Issues.Where(ScorecardColumn.IsUngradedAccident).ToList();
        var accidents = major.Concat(minor).Concat(loading).Concat(ungraded).ToList();
        metrics.Add(Count("incidents-major", major.Count, "Transport Accident (Major)", Codes(major), external: true));
        metrics.Add(Count("incidents-minor", minor.Count, "Transport Accident (Minor)", Codes(minor), external: true));
        metrics.Add(Count("incidents-loading", loading.Count, "Loading Accident", Codes(loading)));
        metrics.Add(Count("incidents-ungraded", ungraded.Count, "อุบัติเหตุที่ยังไม่ระบุชนิด", Codes(ungraded)));
        metrics.Add(PerHundred("incident-rate", accidents.Count, completed.Count, input.MinimumBase,
            "อุบัติเหตุทุกชนิด ÷ งานที่เสร็จสิ้น × 100", Codes(accidents)));
        metrics.Add(Missing("critical-safety",
            "SCMOS ไม่ได้บันทึกการเสียชีวิต การบาดเจ็บสาหัส สินค้าสูญหายรุนแรง แอลกอฮอล์/สารเสพติด — ฝ่ายบริหารพิจารณาจากเอกสารประกอบ"));

        /* ---- claims ---- */
        var claims = input.Issues.Where(IsClaim).ToList();
        var points = claims.Sum(issue => ClaimPoints(issue.Severity));
        var unrated = claims.Count(issue => issue.Severity.Trim().Length == 0);
        var withCase = claims.Where(issue => issue.CaseId is { } id && input.CaseStages.ContainsKey(id)).ToList();
        var closedCase = withCase.Count(issue => input.CaseStages[issue.CaseId!.Value] == "closed");
        metrics.Add(Count("claims", claims.Count, "สินค้าชำรุด/สูญหาย + ข้อร้องเรียนจากลูกค้า", Codes(claims), external: true));
        metrics.Add(Count("claims-critical", claims.Count(issue => ClaimPoints(issue.Severity) == 5), "ความรุนแรง วิกฤต", []));
        metrics.Add(Count("claims-major", claims.Count(issue => ClaimPoints(issue.Severity) == 3), "ความรุนแรง สูง", []));
        metrics.Add(Count("claims-minor", claims.Count(issue => ClaimPoints(issue.Severity) == 1),
            "ความรุนแรง ปานกลาง/ต่ำ" + (unrated > 0 ? $" (รวม {unrated} รายการที่ไม่ระบุความรุนแรง นับเป็น Minor)" : ""), []));
        metrics.Add(Count("claim-points", points, "Minor 1 · Major 3 · Critical 5", Codes(claims)));
        metrics.Add(PerHundred("claim-rate", points, completed.Count, input.MinimumBase, "คะแนนผลกระทบเคลม ÷ งานที่เสร็จสิ้น × 100", Codes(claims)));
        metrics.Add(Count("claims-car-par", withCase.Count, $"ผูกกับ CAR/PAR แล้ว · ปิดแล้ว {closedCase}", Codes(withCase)));

        /* ---- billing ---- */
        var submitted = input.Invoices.Where(invoice => invoice.ReviewCycle >= 1).ToList();
        // Decided: approved, or sent back at least once. One still in its first review has no answer yet.
        var decided = submitted.Where(invoice => invoice.ApprovedAt is not null || invoice.ReviewCycle > 1).ToList();
        var firstTime = decided.Where(invoice => invoice.ApprovedAt is not null && invoice.ReviewCycle == 1).ToList();
        var returned = decided.Where(invoice => invoice.ReviewCycle > 1).ToList();
        metrics.Add(Count("billing-submitted", submitted.Count, "ใบแจ้งหนี้ที่ส่งตรวจในช่วงประเมิน", Numbers(submitted)));
        metrics.Add(Count("billing-returned", returned.Count, "ถูกตีกลับอย่างน้อยหนึ่งครั้ง", Numbers(returned)));
        metrics.Add(submitted.Count == 0
            ? Missing("billing-accuracy", "ยังไม่มีใบแจ้งหนี้ในช่วงนี้ — ระบบวางบิลออนไลน์เริ่ม 30 ก.ย. 2026", external: true)
            : Rate("billing-accuracy", firstTime.Count, decided.Count, input.MinimumBase,
                "อนุมัติตั้งแต่ส่งครั้งแรก ÷ ใบแจ้งหนี้ที่ได้คำตอบแล้ว × 100", Numbers(returned), external: true));

        // SLA: a case opened long after its delivery was back-filled for an old job, and its clock could not have
        // started on time — those are left out and counted in the note.
        var backFilled = input.SlaCases.Count(c => c.OpenedAt - c.DeliveredAt > TimeSpan.FromDays(1));
        var judged = input.SlaCases.Where(c => c.OpenedAt - c.DeliveredAt <= TimeSpan.FromDays(1) && c.DueDate is not null
                && (c.FirstSubmittedAt is not null || c.DueDate < input.Today))
            .ToList();
        var met = judged.Where(c => c.FirstSubmittedAt is { } at
            && DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(7)).DateTime) <= c.DueDate).ToList();
        var missed = judged.Except(met).ToList();
        var slaNote = backFilled > 0 ? $"ไม่นับ {backFilled} งานที่เปิดเคสวางบิลย้อนหลัง" : "";
        metrics.Add(judged.Count == 0
            ? Missing("billing-sla", "ยังไม่มีงานที่ถึงกำหนดวางบิล" + (slaNote.Length > 0 ? " · " + slaNote : ""), external: true)
            : Rate("billing-sla", met.Count, judged.Count, input.MinimumBase,
                "ส่งใบแจ้งหนี้ภายในวันทำการที่กำหนด ÷ งานที่ถึงกำหนด × 100", missed.Select(c => c.JobKey).Take(MaxSources).ToList(),
                external: true, note: slaNote));

        /* ---- documents ---- */
        var withPod = completed.Where(job => input.JobsWithPod.Contains(job.Key)).ToList();
        var withoutPod = completed.Where(job => !input.JobsWithPod.Contains(job.Key)).ToList();
        metrics.Add(Rate("pod-compliance", withPod.Count, completed.Count, input.MinimumBase,
            "งานเสร็จที่มี POD ÷ งานที่เสร็จสิ้น × 100", Keys(withoutPod), external: true));

        /* ---- certification ---- */
        var compliant = input.Compliance.Where(one => one.State is SupplierCompliance.State.Valid or SupplierCompliance.State.Expiring).ToList();
        metrics.Add(input.Compliance.Count == 0
            ? Missing("compliance-documents", "ไม่มีรายการเอกสารที่ทะเบียนกำหนด")
            : new Metric("compliance-documents", AnnualEvaluationRules.Available,
                Percent(compliant.Count, input.Compliance.Count), compliant.Count, input.Compliance.Count,
                "เอกสารบังคับที่ยังไม่หมดอายุ ÷ เอกสารที่ทะเบียนกำหนด × 100",
                string.Join(" · ", input.Compliance.Select(one => $"{one.Code}: {one.State}")),
                input.Compliance.Where(one => one.State is not (SupplierCompliance.State.Valid or SupplierCompliance.State.Expiring))
                    .Select(one => one.Code).ToList(), false));
        metrics.Add(Missing("iso-certificates", "ทะเบียนผู้ขนส่งยังไม่มีช่องเก็บ ISO 9001 / 14001 / 45001 / Q-Mark"));
        metrics.Add(Missing("pricing", "ยังไม่มีราคาอ้างอิงตลาดที่เชื่อถือได้ — Subcontract Management ประเมินเอง (0–100)"));
        return metrics;
    }

    /* ------------------------------------------------------------------ helpers */

    private static Metric Count(string code, int value, string note, IReadOnlyList<string> sources, bool external = false) =>
        new(code, AnnualEvaluationRules.Available, value, value, null, "", note, sources, external);

    private static Metric Missing(string code, string note, bool external = false) =>
        new(code, AnnualEvaluationRules.NotAvailable, null, null, null, "", note, [], external);

    /// <summary>A rate: none without a base, insufficient below the minimum, measured otherwise.</summary>
    private static Metric Rate(string code, int numerator, int denominator, int minimumBase, string formula, IReadOnlyList<string> sources,
        bool external, string note = "") =>
        denominator == 0
            ? new(code, AnnualEvaluationRules.NotAvailable, null, numerator, 0, formula, Join(note, "ไม่มีฐานให้วัด"), sources, external)
            : new(code, denominator < minimumBase ? AnnualEvaluationRules.InsufficientData : AnnualEvaluationRules.Available,
                Percent(numerator, denominator), numerator, denominator, formula,
                Join(note, denominator < minimumBase ? $"ฐาน {denominator} ต่ำกว่าขั้นต่ำ {minimumBase} — ไม่ใช้ให้คะแนน" : ""), sources, external);

    /// <summary>A count per hundred completed jobs — lower is better.</summary>
    private static Metric PerHundred(string code, int count, int completed, int minimumBase, string formula, IReadOnlyList<string> sources) =>
        Rate(code, count, completed, minimumBase, formula, sources, external: false);

    private static decimal Percent(int numerator, int denominator) => Math.Round(numerator * 100m / denominator, 4);

    private static string Join(string first, string second) =>
        string.Join(" · ", new[] { first, second }.Where(text => text.Length > 0));

    private static List<string> Keys(IEnumerable<Job> jobs) => jobs.Select(job => job.Key).Take(MaxSources).ToList();
    private static List<string> Codes(IEnumerable<OperationalIssue> issues) => issues.Select(issue => issue.Code).Take(MaxSources).ToList();
    private static List<string> Numbers(IEnumerable<Invoice> invoices) => invoices.Select(invoice => invoice.Number).Take(MaxSources).ToList();
}
