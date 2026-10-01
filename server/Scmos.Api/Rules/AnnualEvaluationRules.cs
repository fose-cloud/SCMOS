using Scmos.Api.Data;

namespace Scmos.Api.Rules;

/// <summary>
/// Annual Carrier Evaluation's rules (1 Oct 2026): the campaign's states, what may follow what, when its rules are
/// held still, the defaults a new campaign starts from, and what has to be true of its configuration before it opens.
///
/// <para>
/// The defaults are the department's stated baseline — 70 measured / 30 departments, the seven KPI weights, the six
/// questions at five points each, the OTD bands 100 · 96 · 91 · 85. Where the department gave no figure (the bands for
/// safety, claims, billing, documents and certification; the final score bands and the pass mark) nothing is assumed:
/// those start empty and the campaign will not open until somebody sets them.
/// </para>
/// </summary>
public static class AnnualEvaluationRules
{
    public const decimal DefaultSystemWeight = 70m;
    public const decimal DefaultHumanWeight = 30m;
    public const int DefaultMinimumJobs = 5;
    public const decimal DefaultMinimumSystemCoverage = 50m;
    public const int DefaultCommentRequiredAtOrBelow = 2;

    /* ---- campaign states ---- */
    public const string Draft = "draft";
    public const string DataPreparation = "data-preparation";
    public const string Ready = "ready";
    public const string Open = "open";
    public const string Closed = "closed";
    public const string UnderReview = "under-review";
    public const string Approved = "approved";
    public const string Finalized = "finalized";
    public const string Archived = "archived";

    public static readonly string[] Statuses = [Draft, DataPreparation, Ready, Open, Closed, UnderReview, Approved, Finalized, Archived];

    /// <summary>What a campaign may move to from each state. Approving and finalizing are a decision-maker's.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Moves = new Dictionary<string, string[]>
    {
        [Draft] = [DataPreparation],
        [DataPreparation] = [Ready, Draft],
        [Ready] = [Open, DataPreparation],
        [Open] = [Closed],
        [Closed] = [UnderReview, Open],
        [UnderReview] = [Approved, Closed],
        [Approved] = [Finalized, UnderReview],
        [Finalized] = [Archived],
        [Archived] = [],
    };

    public static bool CanMove(string from, string to) => Moves.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>Moves only somebody who decides may make — the rest are the campaign manager's.</summary>
    public static bool IsDecision(string to) => to is Approved or Finalized or Archived;

    /// <summary>
    /// Whether the campaign's rules are held still. From the moment the links work, the period, KPIs, weights, bands,
    /// questions and carriers are what evaluators were shown and what they are scored under; a correction after that
    /// is a recalculation with a reason, never an edit.
    /// </summary>
    public static bool Locked(string status) => status is Open or Closed or UnderReview or Approved or Finalized or Archived;

    /* ---- KPIs ---- */
    public const string Band = "band";
    public const string Manual = "manual";
    public const string Higher = "higher";
    public const string Lower = "lower";

    public sealed record KpiDefault(string Code, string Name, string NameTh, decimal Weight, string Method, string Direction, string Measure,
        (decimal Threshold, decimal Score)[] Bands);

    /// <summary>The seven measured KPIs, in the department's order. Only these have a source the snapshot can read.</summary>
    public static readonly IReadOnlyList<KpiDefault> Kpis =
    [
        new("otd", "OTD / Delivery Performance", "ส่งตรงเวลา", 20m, Band, Higher,
            "Carrier-responsible on-time %: completed jobs not late for a reason counted against the carrier ÷ completed jobs with an arrival × 100",
            [(100m, 100m), (96m, 75m), (91m, 50m), (85m, 25m)]),
        new("safety", "Safety & Incident", "ความปลอดภัยและอุบัติเหตุ", 15m, Band, Lower,
            "Carrier-related incidents per 100 completed jobs", []),
        new("claim", "Customer Claim", "เคลมจากลูกค้า", 10m, Band, Lower,
            "Claim impact points (Minor 1 · Major 3 · Critical 5) per 100 completed jobs", []),
        new("billing", "Billing Performance", "การวางบิล", 10m, Band, Higher,
            "Billing accuracy: invoices approved at the first submission ÷ invoices submitted × 100", []),
        new("pricing", "Pricing Competitiveness", "ความสามารถด้านราคา", 5m, Manual, Higher,
            "Subcontract Management's assessment, 0–100", []),
        new("documents", "Documentation Compliance", "ความครบถ้วนของเอกสาร", 5m, Band, Higher,
            "Required documents complete ÷ required document instances × 100", []),
        new("certification", "Certification / Compliance", "ใบรับรองและเอกสารกำกับ", 5m, Band, Higher,
            "Required compliance documents valid ÷ required × 100", []),
    ];

    public static bool IsKpi(string code) => Kpis.Any(kpi => kpi.Code == code);

    /* ---- departments and questions ---- */
    public sealed record DepartmentDefault(string Code, string Name);

    public static readonly IReadOnlyList<DepartmentDefault> Departments =
    [
        new("ops", "Operation"), new("cs", "Customer Service"), new("billing", "Billing"), new("finance", "Finance"),
        new("ehsq", "EHSQ"), new("sales", "Sales / KAM"), new("subcontract", "Subcontract Management"),
    ];

    /// <param name="AskedOf">The departments asked; empty means every department.</param>
    public sealed record QuestionDefault(string Code, string Text, string TextTh, decimal Weight, string[] AskedOf);

    public static readonly IReadOnlyList<QuestionDefault> Questions =
    [
        new("capacity", "Truck Availability / Capacity", "ความพร้อมของรถ / กำลังรถ", 5m, []),
        new("communication", "Communication", "การสื่อสาร", 5m, []),
        new("problem-solving", "Problem Solving", "การแก้ไขปัญหา", 5m, []),
        new("cooperation", "Operational Cooperation", "ความร่วมมือในการปฏิบัติงาน", 5m, []),
        // The department named who sees the drivers: Operation, CS and EHSQ.
        new("service", "Driver / Service Quality", "คุณภาพคนขับ / การบริการ", 5m, ["ops", "cs", "ehsq"]),
        new("satisfaction", "Overall Satisfaction", "ความพึงพอใจโดยรวม", 5m, []),
    ];

    /* ---- carriers ---- */
    public const string Full = "full";
    public const string LimitedData = "limited-data";
    public const string NoActivity = "no-activity";

    /// <summary>Full at the minimum or above, limited below it, no activity at none. Missing data is never a zero score.</summary>
    public static string Eligibility(int completedJobs, int minimumJobs) =>
        completedJobs <= 0 ? NoActivity : completedJobs >= minimumJobs ? Full : LimitedData;

    /// <summary>The decisions management may record. A score suggests none of them.</summary>
    public static readonly string[] Decisions =
    [
        "continue", "continue-with-improvement-plan", "corrective-action-required", "management-review-required",
        "suspend-new-allocation", "inactive",
    ];

    /* ---- snapshot figures ---- */
    public const string Available = "available";
    public const string NotAvailable = "not-available";
    public const string InsufficientData = "insufficient-data";

    /* ---- invitations ---- */
    public const string InvitationPending = "pending";
    public const string InvitationSent = "sent";
    public const string InvitationOpened = "opened";
    public const string InvitationSubmitted = "submitted";
    public const string InvitationRevoked = "revoked";

    /* ---- configuration ---- */

    /// <summary>A campaign's rules as one value, for <see cref="Problems"/>.</summary>
    public sealed record Configuration(
        EvaluationCampaign Campaign,
        IReadOnlyList<EvaluationKpi> Kpis,
        IReadOnlyList<EvaluationKpiBand> Bands,
        IReadOnlyList<EvaluationQuestion> Questions,
        IReadOnlyList<EvaluationQuestionDepartment> QuestionDepartments,
        IReadOnlyList<EvaluationCampaignDepartment> Departments,
        IReadOnlyList<EvaluationScoreBand> ScoreBands,
        int IncludedCarriers,
        /// <summary>Included carriers with no current snapshot — evidence an evaluator would be shown nothing of.</summary>
        int MissingSnapshots = 0);

    /// <summary>
    /// Everything that stops the campaign from being ready, in words the screen shows. Empty means it may open.
    /// Weights are compared exactly — they are decimals, and 69.99 is not 70.
    /// </summary>
    public static IReadOnlyList<string> Problems(Configuration config)
    {
        var problems = new List<string>();
        var campaign = config.Campaign;

        if (campaign.PeriodEnd < campaign.PeriodStart) problems.Add("ช่วงประเมิน: วันสิ้นสุดก่อนวันเริ่ม");
        if (campaign.OpenOn is null || campaign.DueOn is null) problems.Add("ยังไม่ได้กำหนดวันเปิดและวันปิดรับการประเมิน");
        else if (campaign.DueOn < campaign.OpenOn) problems.Add("วันปิดรับการประเมินอยู่ก่อนวันเปิด");
        if (campaign.SystemWeight < 0 || campaign.HumanWeight < 0 || campaign.SystemWeight + campaign.HumanWeight != 100m)
            problems.Add($"น้ำหนัก System + Department ต้องรวมเป็น 100 (ตอนนี้ {campaign.SystemWeight + campaign.HumanWeight:0.##})");
        if (campaign.MinimumJobs < 0) problems.Add("จำนวนงานขั้นต่ำต้องไม่ติดลบ");
        if (campaign.MinimumSystemCoverage is < 0 or > 100) problems.Add("สัดส่วนน้ำหนัก KPI ที่วัดได้ขั้นต่ำต้องอยู่ระหว่าง 0–100%");
        if (campaign.CommentRequiredAtOrBelow is < 0 or > 5) problems.Add("เกณฑ์บังคับความเห็นต้องอยู่ระหว่าง 0–5");

        var kpis = config.Kpis.Where(kpi => kpi.Enabled).ToList();
        var kpiWeight = kpis.Sum(kpi => kpi.Weight);
        if (kpiWeight != campaign.SystemWeight)
            problems.Add($"น้ำหนัก KPI ที่เปิดใช้รวม {kpiWeight:0.##} ไม่เท่ากับน้ำหนัก System {campaign.SystemWeight:0.##}");
        foreach (var kpi in kpis)
        {
            var label = kpi.Name.Length > 0 ? kpi.Name : kpi.Code;
            if (kpi.Weight <= 0) problems.Add($"KPI {label}: น้ำหนักต้องมากกว่า 0");
            if (kpi.Method is not (Band or Manual)) problems.Add($"KPI {label}: วิธีให้คะแนนไม่ถูกต้อง");
            if (kpi.Direction is not (Higher or Lower)) problems.Add($"KPI {label}: ทิศทางไม่ถูกต้อง");
            if (kpi.FallbackScore is < 0 or > 100) problems.Add($"KPI {label}: คะแนนเมื่อไม่เข้าช่วงใดต้องอยู่ระหว่าง 0–100");
            if (kpi.Method != Band) continue;
            var bands = config.Bands.Where(band => band.KpiId == kpi.Id).ToList();
            if (bands.Count == 0) problems.Add($"KPI {label}: ยังไม่ได้ตั้งช่วงคะแนน");
            if (bands.Any(band => band.Score is < 0 or > 100)) problems.Add($"KPI {label}: คะแนนในช่วงต้องอยู่ระหว่าง 0–100");
            if (bands.GroupBy(band => band.Threshold).Any(group => group.Count() > 1)) problems.Add($"KPI {label}: มีเกณฑ์ซ้ำกัน");
        }

        var departments = config.Departments.Where(row => row.Enabled && row.Weight > 0).Select(row => row.DepartmentId).ToHashSet();
        if (config.Departments.Any(row => row.Weight < 0)) problems.Add("น้ำหนักแผนกต้องไม่ติดลบ");
        var questions = config.Questions.Where(question => question.Enabled).ToList();
        var questionWeight = questions.Sum(question => question.Weight);
        if (questionWeight != campaign.HumanWeight)
            problems.Add($"น้ำหนักคำถามที่เปิดใช้รวม {questionWeight:0.##} ไม่เท่ากับน้ำหนัก Department {campaign.HumanWeight:0.##}");
        if (campaign.HumanWeight > 0 && departments.Count == 0) problems.Add("ยังไม่มีแผนกที่เปิดให้ประเมิน");
        foreach (var question in questions)
        {
            var label = question.Text.Length > 0 ? question.Text : question.Code;
            if (question.Weight <= 0) problems.Add($"คำถาม {label}: น้ำหนักต้องมากกว่า 0");
            if (!config.QuestionDepartments.Any(row => row.QuestionId == question.Id && row.Enabled && departments.Contains(row.DepartmentId)))
                problems.Add($"คำถาม {label}: ไม่มีแผนกใดถูกถาม");
        }

        if (config.ScoreBands.Count == 0) problems.Add("ยังไม่ได้ตั้งช่วงคะแนนรวม (Score band)");
        else
        {
            if (config.ScoreBands.Any(band => band.MinScore is < 0 or > 100)) problems.Add("ช่วงคะแนนรวมต้องอยู่ระหว่าง 0–100");
            if (config.ScoreBands.Min(band => band.MinScore) != 0m) problems.Add("ช่วงคะแนนรวมต้องมีช่วงที่เริ่มจาก 0 เพื่อให้ทุกคะแนนมีช่วง");
            if (config.ScoreBands.GroupBy(band => band.MinScore).Any(group => group.Count() > 1)) problems.Add("ช่วงคะแนนรวมมีคะแนนเริ่มซ้ำกัน");
            if (config.ScoreBands.GroupBy(band => band.Code).Any(group => group.Count() > 1)) problems.Add("ช่วงคะแนนรวมมีรหัสซ้ำกัน");
        }

        if (config.IncludedCarriers == 0) problems.Add("ยังไม่ได้เลือกผู้ขนส่งที่จะประเมิน");
        if (config.MissingSnapshots > 0) problems.Add($"ยังไม่ได้สร้าง snapshot หลักฐาน {config.MissingSnapshots} ราย");
        return problems;
    }

    /// <summary>A code typed by a person — letters, digits and dashes, lower case — or empty when nothing usable is left.</summary>
    public static string Slug(string? text)
    {
        var chars = (text ?? "").Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }
}
