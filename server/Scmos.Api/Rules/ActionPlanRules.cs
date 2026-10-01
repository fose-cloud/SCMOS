namespace Scmos.Api.Rules;

/// <summary>
/// Subcontract Management's Action Plan (1 Oct 2026): development plans for the department's people and for
/// its subcontractors — not the incident action of a CAR/PAR case. The vocabulary, the numbering, and the two
/// things that are worked out rather than stored: whether a plan is overdue, and how far along it is.
/// </summary>
public static class ActionPlanRules
{
    public const string People = "people";
    public const string Subcontractor = "subcontractor";
    public static readonly string[] DevelopmentTypes = [People, Subcontractor];

    /// <summary>employee · team · department for people; subcontractor · carrier for the other side.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> TargetTypes = new Dictionary<string, string[]>
    {
        [People] = ["employee", "team", "department"],
        [Subcontractor] = ["subcontractor", "carrier"],
    };

    public static readonly string[] Periods = ["annual", "quarterly", "monthly", "custom"];
    public static readonly string[] Priorities = ["low", "medium", "high", "critical"];

    public const string Draft = "draft";
    public const string Planned = "planned";
    public const string InProgress = "in-progress";
    public const string Waiting = "waiting";
    public const string PendingReview = "pending-review";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public static readonly string[] Statuses = [Draft, Planned, InProgress, Waiting, PendingReview, Completed, Cancelled];

    /// <summary>
    /// The workflow's moves: Draft → Planned → In Progress → Pending Review → Completed, Waiting beside In
    /// Progress, Cancelled from anywhere still open. Completed is reached only by a review that approves; a review
    /// that does not sends the plan back to In Progress.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Moves = new Dictionary<string, string[]>
    {
        [Draft] = [Planned, Cancelled],
        [Planned] = [InProgress, Cancelled],
        [InProgress] = [Waiting, PendingReview, Cancelled],
        [Waiting] = [InProgress, Cancelled],
        [PendingReview] = [],
        [Completed] = [],
        [Cancelled] = [],
    };

    public static bool CanMove(string from, string to) => Moves.TryGetValue(from, out var next) && next.Contains(to);

    public const string Approved = "approved";
    public const string NeedImprovement = "need-improvement";
    public const string Reopen = "reopen";
    public static readonly string[] ReviewResults = [Approved, NeedImprovement, Reopen];

    public static readonly string[] ItemStatuses = [Planned, InProgress, Waiting, Completed, Cancelled];

    public static readonly string[] Methods =
    [
        "Formal Training", "Internal Training", "External Training", "OJT", "Coaching", "Mentoring", "Self-Learning",
        "Project Assignment", "Cross Training", "Job Rotation",
    ];

    public static readonly string[] TrainingTypes =
    [
        "Safety Training", "EHSQ Training", "Defensive Driver Training", "Customer Requirement", "DG Training",
        "ISO Tank Training", "Operation Training", "System Training", "SCMOS Training", "Procurement Training",
        "Leadership Training", "Other",
    ];

    /// <summary>The carrier development dimensions scored 1–5 (1 Poor … 5 Excellent).</summary>
    public static readonly string[] ScoreDimensions =
        ["Capacity", "OTD", "Safety", "Quality", "Documentation", "Billing", "Communication", "Cost", "Digital Readiness"];

    public static readonly IReadOnlyDictionary<string, string[]> DefaultCategories = new Dictionary<string, string[]>
    {
        [People] =
        [
            "Individual Development Plan", "Skill Development", "Training", "Coaching", "OJT", "Cross Training",
            "Leadership Development", "Knowledge Transfer", "Process Improvement", "Digital / AI Development", "Succession Development",
        ],
        [Subcontractor] =
        [
            "Carrier Performance Development", "Capacity Improvement", "Safety Improvement", "Driver Development",
            "Quality Improvement", "Documentation Improvement", "Billing Improvement", "Cost Improvement", "Digital Development",
            "Customer Requirement Improvement", "Compliance Improvement", "Supplier Development",
        ],
    };

    /// <summary>The department's Skill Matrix, by area, as it gave it (1 Oct 2026). Levels 1 Basic … 5 Expert.</summary>
    public static readonly IReadOnlyList<(string Category, string[] Skills)> DefaultSkills =
    [
        ("Operations", ["Trucking Operation", "FCL Operation", "LCL Operation", "Domestic Transportation", "Container Operation", "ISO Tank", "DG Transportation"]),
        ("Procurement / Carrier", ["Carrier Management", "Sourcing", "Procurement", "Vendor Evaluation", "Rate Analysis", "Cost Analysis", "Negotiation"]),
        ("Quality / Safety", ["Incident Management", "CAR / PAR", "Root Cause Analysis", "Customer Requirement", "EHSQ", "Safety", "Defensive Driver Requirement", "Risk Assessment"]),
        ("Finance / Documentation", ["Billing", "Additional Charge Validation", "Cargo Receipt", "POD", "Contract Rate Validation"]),
        ("Technology", ["SCMOS", "Excel", "Power BI", "Data Analysis", "AI", "Automation"]),
        ("Soft Skills", ["Communication", "Problem Solving", "Leadership", "Presentation", "Coaching", "Decision Making"]),
    ];

    public static readonly string[] SkillLevels = ["", "Basic", "Beginner", "Competent", "Advanced", "Expert"];

    /// <summary>How far a day may be before a target date and still count as due soon: a week.</summary>
    public const int DueSoonDays = 7;

    /// <summary>AP-SCM-2026-0001: the year and a four-digit running number within it.</summary>
    public static string Number(int year, int sequence) => $"AP-SCM-{year}-{sequence:0000}";

    /// <summary>
    /// Overdue is a condition, not a state: the target date has passed and the plan is neither Completed nor
    /// Cancelled. The stored status stays what the workflow left it at.
    /// </summary>
    public static bool Overdue(string status, string targetDate, int today) =>
        status is not (Completed or Cancelled) && Formats.DateNumber(targetDate) is > 0 and var due && due < today;

    /// <summary>
    /// A plan's progress: the average of its items that are not cancelled, a completed item counting 100.
    /// Null when there is nothing to average.
    /// </summary>
    public static int? Progress(IEnumerable<(string Status, int Progress)> items)
    {
        var active = items.Where(item => item.Status != Cancelled)
            .Select(item => item.Status == Completed ? 100 : Math.Clamp(item.Progress, 0, 100)).ToList();
        return active.Count == 0 ? null : (int)Math.Round(active.Average(), MidpointRounding.AwayFromZero);
    }
}
