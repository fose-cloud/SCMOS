using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/*
 * Annual Carrier Evaluation (1 Oct 2026). A campaign is one year's evaluation, set up once and then held still:
 * its KPI weights and bands, its questions and department weights are rows of the campaign, copied from the
 * defaults (or the previous campaign) when it is made, so changing next year's rules never rewrites this year's.
 * Once a campaign opens those rows are locked (AnnualEvaluationRules.Locked). Carriers are the Supplier Register's
 * own rows; evaluators outside SCMOS answer through a link whose token is stored only as a hash.
 */

/// <summary>One year's evaluation of the carriers.</summary>
public class EvaluationCampaign
{
    public int Id { get; set; }

    /// <summary>AE-2026 — unique, and what the screens and the audit trail call it.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Year { get; set; }

    /// <summary>The jobs, issues and invoices read into the snapshot are the ones inside these two days.</summary>
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    /// <summary>When the departments' links start and stop working, Bangkok days.</summary>
    public DateOnly? OpenOn { get; set; }
    public DateOnly? DueOn { get; set; }

    /// <summary>Points of the hundred given to the measured part and to the departments' part. Together 100.</summary>
    public decimal SystemWeight { get; set; } = AnnualEvaluationRules.DefaultSystemWeight;
    public decimal HumanWeight { get; set; } = AnnualEvaluationRules.DefaultHumanWeight;

    /// <summary>Completed jobs in the period for a full evaluation; fewer is limited data, none is no activity.</summary>
    public int MinimumJobs { get; set; } = AnnualEvaluationRules.DefaultMinimumJobs;

    /// <summary>
    /// The share of <see cref="SystemWeight"/>, in percent, that has to be measurable before a system score is given.
    /// Below it the carrier is insufficient data rather than scored on the one KPI that happened to be recorded.
    /// </summary>
    public decimal MinimumSystemCoverage { get; set; } = AnnualEvaluationRules.DefaultMinimumSystemCoverage;

    /// <summary>A rating at or below this needs a comment, unless a question says otherwise.</summary>
    public int CommentRequiredAtOrBelow { get; set; } = AnnualEvaluationRules.DefaultCommentRequiredAtOrBelow;

    /// <summary>One of <see cref="AnnualEvaluationRules.Statuses"/>.</summary>
    public string Status { get; set; } = AnnualEvaluationRules.Draft;

    /// <summary>Moves on every change to the campaign's rules, so a result can say which rules it was worked under.</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset? LockedAt { get; set; }
    public string LockedBy { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One measured KPI of a campaign's system part, with its weight.</summary>
public class EvaluationKpi
{
    public int Id { get; set; }
    public int CampaignId { get; set; }

    /// <summary>otd · safety · claim · billing · pricing · documents · certification — see <see cref="AnnualEvaluationRules.Kpis"/>.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameTh { get; set; } = "";

    /// <summary>Points of the hundred. The enabled KPIs add up to the campaign's system weight.</summary>
    public decimal Weight { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>band — scored from the measured value through the bands · manual — Subcontract Management's own 0–100.</summary>
    public string Method { get; set; } = AnnualEvaluationRules.Band;

    /// <summary>higher — a larger value is better (on-time %) · lower — a smaller one is (incidents per 100 jobs).</summary>
    public string Direction { get; set; } = AnnualEvaluationRules.Higher;

    /// <summary>What the value is, in words — the formula a reviewer reads beside the score.</summary>
    public string Measure { get; set; } = "";

    /// <summary>The score when no band is met.</summary>
    public decimal FallbackScore { get; set; }
    public int Position { get; set; }
}

/// <summary>
/// One step of a KPI's scoring: a value at or past <see cref="Threshold"/> scores <see cref="Score"/> — at or above
/// for a higher-is-better KPI, at or below for a lower-is-better one; the best step met wins.
/// </summary>
public class EvaluationKpiBand
{
    public int Id { get; set; }
    public int KpiId { get; set; }
    public decimal Threshold { get; set; }
    public decimal Score { get; set; }
}

/// <summary>Where a final score falls — Excellent, Pass … Nothing is assumed: a campaign opens only once these are set.</summary>
public class EvaluationScoreBand
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal MinScore { get; set; }
}

/// <summary>
/// A department that evaluates carriers — Operation, Customer Service, Billing, Finance, EHSQ, Sales/KAM,
/// Subcontract Management. SCMOS had no department list (an account has a role, not a department), and most of
/// these people never sign in, so this is the list; campaigns weight it.
/// </summary>
public class EvaluationDepartment
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public int Position { get; set; }
}

/// <summary>How much one department's view counts in a campaign — relative, so it does not have to add to anything.</summary>
public class EvaluationCampaignDepartment
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public int DepartmentId { get; set; }
    public decimal Weight { get; set; } = 1m;
    public bool Enabled { get; set; } = true;
}

/// <summary>One question of a campaign's department part, with its points of the hundred.</summary>
public class EvaluationQuestion
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public string TextTh { get; set; } = "";
    public decimal Weight { get; set; }
    public bool Enabled { get; set; } = true;
    public int Position { get; set; }
}

/// <summary>Whether a department is asked a question — not every department sees the drivers.</summary>
public class EvaluationQuestionDepartment
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public int DepartmentId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Required { get; set; } = true;

    /// <summary>Overrides the campaign's comment threshold for this question and department; null keeps it.</summary>
    public int? CommentRequiredAtOrBelow { get; set; }
}

/// <summary>A carrier in a campaign: the Supplier Register's row, whether it is evaluated, and the decision on it.</summary>
public class EvaluationCarrier
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public int SupplierId { get; set; }

    public bool Included { get; set; } = true;
    public string ExcludedReason { get; set; } = "";

    /// <summary>Counted from the register over the campaign's period, when somebody last asked.</summary>
    public int? TotalJobs { get; set; }
    public int? CompletedJobs { get; set; }
    public DateTimeOffset? CountedAt { get; set; }

    /// <summary>full · limited-data · no-activity — from <see cref="CompletedJobs"/> and the campaign's minimum.</summary>
    public string Eligibility { get; set; } = "";

    /// <summary>Management's decision — never worked out from the score. Empty until somebody makes it.</summary>
    public string Decision { get; set; } = "";
    public string DecisionNote { get; set; } = "";
    public string DecidedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }

    public string AddedBy { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>
/// The evidence a carrier is scored on, as it stood when it was taken. Never edited: taking it again writes a new
/// version and moves <see cref="Current"/>, so what an evaluator was shown can always be shown again.
/// </summary>
public class EvaluationSnapshot
{
    public long Id { get; set; }
    public int CampaignId { get; set; }
    public int EvaluationCarrierId { get; set; }
    public int Version { get; set; }
    public bool Current { get; set; }
    public string Reason { get; set; } = "";
    public string GeneratedBy { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>One figure of a snapshot, with how it was worked out and the records it was worked out from.</summary>
public class EvaluationSnapshotMetric
{
    public long Id { get; set; }
    public long SnapshotId { get; set; }

    /// <summary>total-jobs · completed-jobs · carrier-otd · incidents-major … — see the snapshot service.</summary>
    public string Code { get; set; } = "";

    /// <summary>available · not-available · insufficient-data. Not available is never a zero.</summary>
    public string Status { get; set; } = AnnualEvaluationRules.Available;
    public decimal? Value { get; set; }
    public decimal? Numerator { get; set; }
    public decimal? Denominator { get; set; }
    public string Formula { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>The records behind the figure — job keys, issue codes, case references — as a JSON array.</summary>
    public string Sources { get; set; } = "[]";

    /// <summary>Whether an evaluator outside SCMOS may see it. Rates and bids never are.</summary>
    public bool External { get; set; }
}

/// <summary>Subcontract Management's own 0–100 for a manual KPI (pricing, until a benchmark can be trusted).</summary>
public class EvaluationManualScore
{
    public int Id { get; set; }
    public int EvaluationCarrierId { get; set; }
    public string KpiCode { get; set; } = "";
    public decimal Score { get; set; }
    public string Note { get; set; } = "";
    public string AssessedBy { get; set; } = "";
    public DateTimeOffset AssessedAt { get; set; }
}

/// <summary>Somebody asked to evaluate — usually not an SCMOS account, so a name, an address and a department.</summary>
public class EvaluationEvaluator
{
    public int Id { get; set; }
    public int CampaignId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int DepartmentId { get; set; }

    /// <summary>The staff id when the evaluator is also an SCMOS account; empty otherwise.</summary>
    public string StaffId { get; set; } = "";
    public bool Active { get; set; } = true;
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One evaluator's link for one carrier. The token itself is shown once, to the admin who copies the link; only
/// its SHA-256 is kept, so a database read cannot be turned into a working link.
/// </summary>
public class EvaluationInvitation
{
    public long Id { get; set; }
    public int CampaignId { get; set; }
    public int EvaluatorId { get; set; }
    public int EvaluationCarrierId { get; set; }

    /// <summary>Lower-case hex SHA-256 of the token. Unique.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>pending · sent · opened · submitted · revoked. Expired is worked out from <see cref="ExpiresAt"/>.</summary>
    public string Status { get; set; } = AnnualEvaluationRules.InvitationPending;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string RevokedBy { get; set; } = "";
    public string RevokeReason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An evaluator's one submission for one carrier. One per invitation, final once made.</summary>
public class EvaluationResponse
{
    public long Id { get; set; }
    public long InvitationId { get; set; }
    public int CampaignId { get; set; }
    public int EvaluationCarrierId { get; set; }
    public int EvaluatorId { get; set; }
    public int DepartmentId { get; set; }
    public string Comment { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
}

/// <summary>One answer. A null rating is N/A — left out of the evaluator's score, never counted as zero.</summary>
public class EvaluationAnswer
{
    public long Id { get; set; }
    public long ResponseId { get; set; }
    public int QuestionId { get; set; }
    public int? Rating { get; set; }
    public string Comment { get; set; } = "";
}

/// <summary>
/// A carrier's worked-out score. Recalculating writes a new version and moves <see cref="Current"/>; the breakdown
/// (each KPI's value, band, score and weight; each department's score and weight) is kept with it.
/// </summary>
public class EvaluationResult
{
    public long Id { get; set; }
    public int EvaluationCarrierId { get; set; }
    public int Version { get; set; }
    public bool Current { get; set; }
    public long? SnapshotId { get; set; }

    /// <summary>The campaign's rules version the score was worked under.</summary>
    public int CampaignVersion { get; set; }

    /// <summary>0–100 each, or null when there was not enough to give one.</summary>
    public decimal? SystemScore { get; set; }
    public decimal? HumanScore { get; set; }
    public decimal? FinalScore { get; set; }

    /// <summary>The system weight that could be measured, in points.</summary>
    public decimal SystemWeightAvailable { get; set; }

    /// <summary>The band the final score falls in — the calculated one, which a decision does not overwrite.</summary>
    public string Band { get; set; } = "";

    /// <summary>calculated · insufficient-data · management-review-required.</summary>
    public string Status { get; set; } = "";

    /// <summary>The whole breakdown as JSON, so the score can be re-read exactly as it was worked out.</summary>
    public string Detail { get; set; } = "{}";
    public string Reason { get; set; } = "";
    public string CalculatedBy { get; set; } = "";
    public DateTimeOffset CalculatedAt { get; set; }
}

/// <summary>
/// An AI summary of one carrier's evaluation (2 Oct 2026, Phase 12), kept with the numbered facts it was written from so
/// every line can be traced back. Only the lines that cited real facts and real figures are here; how many were dropped
/// is kept too. It is a reading aid: no score or decision reads it.
/// </summary>
public class EvaluationAiSummary
{
    public long Id { get; set; }
    public int CampaignId { get; set; }
    public int EvaluationCarrierId { get; set; }
    public long? SnapshotId { get; set; }
    public long? ResultId { get; set; }
    public string Summary { get; set; } = "";
    public string Strengths { get; set; } = "";
    public string Improvements { get; set; } = "";
    public string Trends { get; set; } = "";

    /// <summary>The facts as given to the model, JSON: [{id, kind, text}].</summary>
    public string Facts { get; set; } = "[]";

    /// <summary>Lines the model wrote that were not kept — no fact cited, an unknown fact, a figure not in the facts, a decision.</summary>
    public int Dropped { get; set; }
    public string Model { get; set; } = "";
    public bool Mock { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
}

/// <summary>One department's score inside a result: its evaluators' scores averaged, before its weight is applied.</summary>
public class EvaluationDepartmentScore
{
    public long Id { get; set; }
    public long ResultId { get; set; }
    public int DepartmentId { get; set; }
    public decimal? Score { get; set; }
    public int Responses { get; set; }
    public decimal Weight { get; set; }
}
