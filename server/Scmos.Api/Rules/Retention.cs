namespace Scmos.Api.Rules;

/// <summary>
/// How long a document is kept, and where it lives while it is kept.
///
/// Ten years, in three tiers, and the last step is a person:
///
///   0–1 year   Hot      read often — this year's paperwork
///   1–3 years  Cool     read occasionally — last year's dispute
///   3–10 years Archive  read rarely — a customs or insurance question
///   10 years+  review   a person decides, and records why
///
/// The tiering is applied by a storage account lifecycle policy rather than per
/// upload, so changing the rule is a policy change and not a deployment. What
/// this file owns is the part the policy cannot express: **nothing is ever
/// deleted automatically.**
///
/// That is a deliberate refusal. A lifecycle rule with a delete action is one
/// mistyped prefix away from destroying a customs file that a dispute three
/// years later depends on, and blob deletion is not something an operator can
/// undo. Retention end raises a review; a person with the right to approve it
/// says yes, in writing, with a reason, and that decision lands in the audit
/// trail. There is no code path in this system that deletes a document.
/// </summary>
public static class Retention
{
    /// <summary>Blob access tiers, named as the storage account names them.</summary>
    public const string Hot = "Hot";
    public const string Cool = "Cool";
    public const string Archive = "Archive";

    public const int CoolAfterDays = 365;
    public const int ArchiveAfterDays = 365 * 3;

    /// <summary>Ten years. The longest of the retention obligations the operation is under.</summary>
    public const int RetentionDays = 365 * 10;

    /// <summary>How far ahead of retention end a document appears on the review list.</summary>
    public const int ReviewWindowDays = 90;

    /// <summary>Where a document of this age should be sitting.</summary>
    public static string TierFor(int ageDays) =>
        ageDays >= ArchiveAfterDays ? Archive : ageDays >= CoolAfterDays ? Cool : Hot;

    /// <summary>
    /// What should happen to it: keep · review · overdue-review.
    ///
    /// Never "delete". The word is absent from the vocabulary on purpose — a
    /// state a program can produce is a state some later code will act on.
    /// </summary>
    public static string StateFor(int ageDays) =>
        ageDays >= RetentionDays ? "overdue-review"
        : ageDays >= RetentionDays - ReviewWindowDays ? "review"
        : "keep";

    public static int AgeDays(DateTimeOffset uploadedAt) =>
        (int)(DateTimeOffset.UtcNow - uploadedAt).TotalDays;
}
