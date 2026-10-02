namespace Scmos.Api.Rules;

/// <summary>
/// Management review of an annual evaluation (2 Oct 2026, Annual Evaluation Phases 8–9). Pure.
///
/// <para>
/// A score is worked out; a decision is made. The two are kept apart: the calculated band never becomes a decision on its
/// own, and every decision but "continue" carries the reason it was made. Before a campaign is approved every carrier in it
/// has a score that still matches its evidence and a decision; once it is finalized the results go to the Supplier Register
/// and nothing in the campaign changes again.
/// </para>
/// </summary>
public static class EvaluationReview
{
    public const string Continue = "continue";
    public const string ContinueWithPlan = "continue-with-improvement-plan";
    public const string CorrectiveAction = "corrective-action-required";
    public const string ManagementReview = "management-review-required";
    public const string SuspendNewAllocation = "suspend-new-allocation";
    public const string Inactive = "inactive";

    /// <summary>The decisions, in the order a reviewer reads them, with the words the screens use.</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> Decisions =
    [
        (Continue, "ใช้งานต่อ"),
        (ContinueWithPlan, "ใช้งานต่อ พร้อมแผนปรับปรุง"),
        (CorrectiveAction, "ต้องแก้ไข (Corrective action)"),
        (ManagementReview, "ส่งผู้บริหารพิจารณา"),
        (SuspendNewAllocation, "ระงับการจ่ายงานใหม่"),
        (Inactive, "เลิกใช้งาน"),
    ];

    public static bool IsDecision(string? code) => Decisions.Any(decision => decision.Code == code);

    public static string LabelOf(string code) => Decisions.FirstOrDefault(decision => decision.Code == code).Label ?? code;

    public const int MinimumNote = 4;

    /// <summary>
    /// Whether a decision needs its reason written down: every decision but continuing as before does, and so does changing
    /// one already recorded — the audit trail then says why it changed.
    /// </summary>
    public static bool NeedsNote(string decision, string previous) => decision != Continue || (previous.Length > 0 && previous != decision);

    /// <summary>Decisions whose consequence lives in the Supplier Register — shown to whoever finalizes, never applied by the campaign.</summary>
    public static bool NeedsRegisterFollowUp(string decision) => decision is SuspendNewAllocation or Inactive;

    /// <summary>Decisions that call for an improvement plan with the carrier.</summary>
    public static bool NeedsActionPlan(string decision) => decision is ContinueWithPlan or CorrectiveAction;

    /// <summary>What a result was worked out from, against what there is now.</summary>
    public sealed record Freshness(DateTimeOffset CalculatedAt, long? ResultSnapshotId, int ResultCampaignVersion, long? CurrentSnapshotId,
        int CampaignVersion, DateTimeOffset? LastResponseAt, DateTimeOffset? LastManualAt);

    /// <summary>Why a carrier's current score no longer matches its evidence; empty when it does.</summary>
    public static IReadOnlyList<string> Stale(Freshness now)
    {
        var why = new List<string>();
        if (now.LastResponseAt > now.CalculatedAt) why.Add("มีคำตอบจากผู้ประเมินหลังคำนวณ");
        if (now.LastManualAt > now.CalculatedAt) why.Add("คะแนนที่ประเมินด้วยมือเปลี่ยนหลังคำนวณ");
        if (now.CurrentSnapshotId != now.ResultSnapshotId) why.Add("มี snapshot ใหม่หลังคำนวณ");
        if (now.CampaignVersion != now.ResultCampaignVersion) why.Add("เกณฑ์แคมเปญเปลี่ยนหลังคำนวณ");
        return why;
    }

    /// <summary>One carrier as the approval sees it.</summary>
    public sealed record CarrierState(string Name, bool Calculated, bool Stale, string Decision);

    /// <summary>
    /// What stops a campaign from being approved, in words the screen shows; empty means it may be. A carrier with too little
    /// evidence for a final score still needs a decision — not having a score is not a reason to have no decision.
    /// </summary>
    public static IReadOnlyList<string> ApprovalProblems(IReadOnlyList<CarrierState> carriers)
    {
        var problems = new List<string>();
        if (carriers.Count == 0) { problems.Add("ไม่มีผู้ขนส่งในแคมเปญ"); return problems; }
        void Add(string what, IReadOnlyList<CarrierState> rows)
        {
            if (rows.Count == 0) return;
            var names = string.Join(", ", rows.Take(3).Select(row => row.Name)) + (rows.Count > 3 ? $" (+{rows.Count - 3})" : "");
            problems.Add($"{what} {rows.Count} ราย: {names}");
        }
        Add("ยังไม่ได้คำนวณคะแนน", carriers.Where(row => !row.Calculated).ToList());
        Add("ต้องคำนวณใหม่", carriers.Where(row => row.Calculated && row.Stale).ToList());
        Add("ยังไม่ได้ตัดสิน", carriers.Where(row => !IsDecision(row.Decision)).ToList());
        return problems;
    }

    /// <summary>Responses as a share of the links that are still live, or null when nobody has been asked.</summary>
    public static decimal? Completion(int responses, int invited) => invited <= 0 ? null : Math.Round(responses * 100m / invited, 2);
}
