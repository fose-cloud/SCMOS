namespace Scmos.Api.Rules;

/// <summary>
/// Which changes have to be recorded, and which have to be explained.
///
/// Written down rather than left to each endpoint's judgement, because "did
/// anyone record that" is the question an audit asks, and a system where the
/// answer depends on which developer wrote which route has no audit trail —
/// it has a habit.
/// </summary>
public static class AuditActions
{
    public const string Update = "update";
    public const string Assign = "assign";
    public const string StatusChange = "status";
    public const string CarrierChange = "carrier";
    public const string RateChange = "rate";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Apply = "apply";
    public const string Close = "close";
    public const string Upload = "upload";
    public const string Register = "register";
    public const string RetentionReview = "retention-review";
    public const string BulkReplace = "bulk-replace";

    /// <summary>
    /// Changes a person must justify.
    ///
    /// Not every field: asking for a reason on every keystroke trains people to
    /// type "update" and means nothing is explained. These are the ones where
    /// the old and new value genuinely do not say why — swapping a carrier, a
    /// price, a supplier's approval, or replacing the register wholesale.
    /// </summary>
    public static readonly string[] NeedsReason =
        [CarrierChange, RateChange, Close, RetentionReview, BulkReplace];

    public static bool RequiresReason(string action) =>
        NeedsReason.Contains(action, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The fields on a job whose change is worth an audit row on its own.
    ///
    /// A job carries forty-odd fields and most edits are typing a container
    /// number that was always going to be that number. These are the ones that
    /// change what happens: who carries it, when, in what, and whether it is
    /// finished.
    /// </summary>
    private static readonly Dictionary<string, (string Action, string Label)> Significant =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["trucker"] = (CarrierChange, "ผู้ขนส่ง"),
            ["status"] = (StatusChange, "สถานะ"),
            ["licence"] = (Update, "ทะเบียนรถ"),
            ["driver"] = (Update, "คนขับ"),
            ["op"] = (Assign, "ผู้รับผิดชอบ"),
            ["date"] = (Update, "วันที่งาน"),
            ["planTime"] = (Update, "เวลานัด"),
            ["container"] = (Update, "เลขตู้"),
        };

    /// <summary>The action a changed field means, or null when it is not worth a row.</summary>
    public static (string Action, string Label)? For(string field) =>
        Significant.TryGetValue(field, out var found) ? found : null;
}
