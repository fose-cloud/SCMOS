using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// The configurable list of Action Plan types (1 Oct 2026) — people development and subcontractor development
/// — filled with the department's defaults the first time it is read; an Administrator adds or retires one.
/// </summary>
public class ActionPlanType
{
    public int Id { get; set; }

    /// <summary>people · subcontractor</summary>
    public string DevelopmentType { get; set; } = ActionPlanRules.People;

    public string Name { get; set; } = "";
    public int Position { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// One development plan (1 Oct 2026): for a person, a team or the department, or for a subcontractor. The
/// staff register and the supplier register are referenced, never copied — what is kept here of a person is
/// what the plan was made against: their position, team and supervisor on the day, which the staff register
/// does not hold.
/// </summary>
public class ActionPlan
{
    public long Id { get; set; }

    /// <summary>AP-SCM-YYYY-XXXX. Unique.</summary>
    public string Number { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>people · subcontractor</summary>
    public string DevelopmentType { get; set; } = ActionPlanRules.People;

    /// <summary>The plan type, from <see cref="ActionPlanType"/>.</summary>
    public string Category { get; set; } = "";

    /// <summary>annual · quarterly · monthly · custom</summary>
    public string Period { get; set; } = "annual";
    public int Year { get; set; }
    public int? Quarter { get; set; }
    public int? Month { get; set; }
    public string Department { get; set; } = "Subcontract Management";

    /// <summary>low · medium · high · critical</summary>
    public string Priority { get; set; } = "medium";

    /// <summary><see cref="ActionPlanRules.Statuses"/>. Overdue is worked out, never stored.</summary>
    public string Status { get; set; } = ActionPlanRules.Draft;

    public string Description { get; set; } = "";
    public string Objective { get; set; } = "";
    public string ExpectedOutcome { get; set; } = "";

    /* ---- dates, DD/MM/YYYY ---- */
    public string StartDate { get; set; } = "";
    public string TargetDate { get; set; } = "";
    public string ActualCompletionDate { get; set; } = "";

    /* ---- who owns it ---- */
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";

    /* ---- who or what is developed ---- */
    /// <summary>employee · team · department · subcontractor · carrier</summary>
    public string TargetType { get; set; } = "employee";
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string Position { get; set; } = "";
    public string Team { get; set; } = "";
    public string Supervisor { get; set; } = "";
    public int? SupplierId { get; set; }

    /// <summary>The team's or department's name, or the subcontractor's as the register names it.</summary>
    public string TargetName { get; set; } = "";

    /* ---- the development itself ---- */
    public string DevelopmentArea { get; set; } = "";
    /// <summary>People: the skill level now and wanted (1 Basic … 5 Expert). Subcontractor: the performance now and wanted.</summary>
    public string CurrentLevel { get; set; } = "";
    public string TargetLevel { get; set; } = "";
    public string Gap { get; set; } = "";
    public string RootCause { get; set; } = "";
    /// <summary>Formal Training, OJT, Coaching … (<see cref="ActionPlanRules.Methods"/>).</summary>
    public string Method { get; set; } = "";
    public string Coach { get; set; } = "";
    public string CarrierContact { get; set; } = "";
    public string EvaluationMethod { get; set; } = "";
    public string ReviewDate { get; set; } = "";
    public string Result { get; set; } = "";

    /* ---- a measurable target, for the KPI link and later analysis ---- */
    public string Metric { get; set; } = "";
    public decimal? Baseline { get; set; }
    public decimal? TargetValue { get; set; }
    public decimal? ActualValue { get; set; }

    public string CancelReason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One step of a plan, with its owner, dates, progress and — when it is a training — the training.</summary>
public class ActionPlanItem
{
    public long Id { get; set; }
    public long PlanId { get; set; }
    public int Sequence { get; set; }
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string SupportingPerson { get; set; } = "";
    public string SupportingDepartment { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string TargetDate { get; set; } = "";
    public string ActualCompletionDate { get; set; } = "";
    public string Priority { get; set; } = "medium";

    /// <summary>planned · in-progress · waiting · completed · cancelled</summary>
    public string Status { get; set; } = ActionPlanRules.Planned;

    /// <summary>0–100; 100 when completed.</summary>
    public int Progress { get; set; }

    public string ExpectedResult { get; set; } = "";
    public string ActualResult { get; set; } = "";
    public string Remark { get; set; } = "";

    /* ---- a training, when the step is one ---- */
    public string TrainingTitle { get; set; } = "";
    public string TrainingType { get; set; } = "";
    public string Trainer { get; set; } = "";
    public string TrainingProvider { get; set; } = "";
    public string TrainingDate { get; set; } = "";
    public string Participants { get; set; } = "";
    public string CertificateExpiry { get; set; } = "";

    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The plan's progress history: a comment, and the progress or status it moved, in the order written.</summary>
public class ActionPlanUpdate
{
    public long Id { get; set; }
    public long PlanId { get; set; }
    public long? ItemId { get; set; }
    public string Comment { get; set; } = "";
    public int? ProgressBefore { get; set; }
    public int? ProgressAfter { get; set; }
    public string StatusBefore { get; set; } = "";
    public string StatusAfter { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A submission for review and its answer: approved, needs improvement, or reopened.</summary>
public class ActionPlanReview
{
    public long Id { get; set; }
    public long PlanId { get; set; }
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    public string Reviewer { get; set; } = "";
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>empty while waiting · approved · need-improvement · reopen</summary>
    public string Result { get; set; } = "";
    public string Comment { get; set; } = "";
}

/// <summary>
/// A subcontractor development score (1 Poor … 5 Excellent) on one dimension, kept as a row per assessment so
/// last quarter's stays beside this one.
/// </summary>
public class ActionPlanScore
{
    public long Id { get; set; }
    public long PlanId { get; set; }
    public int? SupplierId { get; set; }
    public string Dimension { get; set; } = "";
    public int? PreviousScore { get; set; }
    public int? CurrentScore { get; set; }
    public int? TargetScore { get; set; }
    public string AssessedBy { get; set; } = "";
    public DateTimeOffset AssessedAt { get; set; }
}

/// <summary>
/// Another SCMOS record the plan is about — an evaluation, a KPI, an incident, a CAR/PAR, an audit, a training —
/// referenced by kind and id rather than copied.
/// </summary>
public class ActionPlanReference
{
    public long Id { get; set; }
    public long PlanId { get; set; }

    /// <summary>evaluation · kpi · incident · carpar · audit · customer · training · risk · project · plan</summary>
    public string Kind { get; set; } = "";
    public string RefId { get; set; } = "";
    public string Label { get; set; } = "";
}
