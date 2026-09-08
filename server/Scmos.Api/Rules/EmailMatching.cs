using Scmos.Api.Data;

namespace Scmos.Api.Rules;

/// <summary>
/// Which job a shipping email is about, and how sure anybody can be.
///
/// <para>
/// <see cref="EmailExtraction"/> answers what a message names. This answers
/// what those names point at, and — the part that matters — how much weight to
/// put on the answer. The thresholds are the specification's, kept in
/// <see cref="MailLink"/> so the rule and the column that stores its result
/// cannot drift apart: at or above 0.95 the link is made, from 0.70 it is
/// offered for a person to confirm, and below that it is not offered at all.
/// </para>
///
/// <para>
/// <b>An email attached to the wrong job is worse than one left unattached</b>,
/// because the second is visible and the first is not. Everything below leans
/// that way: evidence divides rather than accumulates when it is ambiguous, and
/// the two commonest reasons to be wrong — a number quoted from an earlier
/// message in the thread, and a number that points at several jobs — both pull
/// the answer down.
/// </para>
///
/// <para>
/// Pure, so <c>--check-email</c> can prove it with no mailbox and no database.
/// The caller looks the candidates up; the judgement is here.
/// </para>
/// </summary>
public static class EmailMatching
{
    /// <summary>
    /// How much a kind of identifier is worth when it points at exactly one job.
    ///
    /// <para>
    /// Not a guess. Measured over the register: of 1,522 distinct container
    /// numbers 1,383 sit on exactly one job (91%); of 1,038 job codes 802 do
    /// (77%); of 448 bookings only 299 do (67%). A container is the sharpest
    /// identifier the register holds, which is not the obvious answer — SCMOS's
    /// own job number is the weaker of the two, because a number is a booking
    /// and a booking can be several containers.
    /// </para>
    ///
    /// <para>
    /// These are what a kind is worth <b>given that it already resolved to one
    /// job</b>. The ambiguity the percentages describe is handled separately, by
    /// dividing across however many jobs a value actually reached — so the
    /// figures here are about what is left to be wrong about once that is known:
    /// a job missing from the register, or a number typed wrongly at either end.
    /// </para>
    /// </summary>
    public static double StrengthOf(string kind, bool wellFormed) => kind switch
    {
        // A container number is a globally unique box, and one that passes its
        // own check digit is very unlikely to be a different box by accident.
        EmailExtraction.Kind.Container when wellFormed => 0.96,
        // One that fails the check digit is a real container with a digit typed
        // wrong — eleven such live in the register — so it may point at the
        // wrong box rather than at none.
        EmailExtraction.Kind.Container => 0.85,

        // Our own number, issued by us, and read by the same rule everywhere.
        EmailExtraction.Kind.JobCode => 0.96,

        // A booking covers several containers by design. Even resolving to one
        // job, the mail may be about a different container on the same booking.
        EmailExtraction.Kind.Booking => 0.85,

        // The register stores bills of lading inconsistently — some sit in the
        // container column — so a match on one says less than it appears to.
        EmailExtraction.Kind.BillOfLading => 0.75,

        _ => 0.0,
    };

    /// <summary>
    /// What a value found in the body alone is worth, against the same value in
    /// the subject.
    ///
    /// A subject is written about this message. A body is the thread: quoted
    /// replies, a signature, and whatever the last five messages carried, so a
    /// number there may belong to a shipment that was settled a week ago. It is
    /// a discount rather than a refusal because most mail carries its
    /// identifiers in the body and nowhere else.
    /// </summary>
    public const double BodyOnly = 0.95;

    /// <summary>One identifier, and the jobs the register says carry it.</summary>
    /// <param name="Found">What the extractor read out of the message.</param>
    /// <param name="JobKeys">Every job holding that value. Empty means the register does not know it.</param>
    public record Evidence(EmailExtraction.Found Found, IReadOnlyList<string> JobKeys);

    /// <summary>
    /// One job the message might be about.
    /// </summary>
    /// <param name="JobKey">The register's key.</param>
    /// <param name="Confidence">0 to 1, read against <see cref="MailLink"/>'s thresholds.</param>
    /// <param name="Decision">LINK · SUGGEST · UNMATCHED.</param>
    /// <param name="MatchedOn">The kind of identifier that carried the most weight.</param>
    /// <param name="MatchedValue">And its value, so the link can be explained.</param>
    /// <param name="Because">A sentence for a person, in Thai.</param>
    public record JobMatch(string JobKey, double Confidence, string Decision,
        string MatchedOn, string MatchedValue, string Because);

    public static class Decision
    {
        public const string Link = "LINK";
        public const string Suggest = "SUGGEST";
        public const string Unmatched = "UNMATCHED";
    }

    /// <summary>
    /// Every job the message points at, strongest first.
    ///
    /// <para>
    /// Each identifier contributes to each job it names. Where one value reaches
    /// several jobs its weight is divided between them — the evidence really is
    /// split, and a booking that covers three deliveries is not three-quarters
    /// sure about any one of them. That single rule is what stops the commonest
    /// wrong link: a number the register holds against a whole set of rows being
    /// read as though it named one.
    /// </para>
    ///
    /// <para>
    /// Independent identifiers agreeing on the same job combine, and this is
    /// where near-certainty comes from. Two pieces of evidence each leaving a
    /// 4% doubt leave 0.16% together, so a message naming both a container and
    /// a job code that agree is linked without asking, while either alone from
    /// the body is only offered.
    /// </para>
    /// </summary>
    public static List<JobMatch> Match(IReadOnlyList<Evidence> evidence)
    {
        var doubt = new Dictionary<string, double>(StringComparer.Ordinal);
        var best = new Dictionary<string, (double Weight, string Kind, string Value)>(StringComparer.Ordinal);

        foreach (var one in evidence ?? [])
        {
            var keys = one.JobKeys ?? [];
            if (keys.Count == 0) continue;

            var weight = StrengthOf(one.Found.Type, one.Found.WellFormed);
            if (weight <= 0) continue;
            if (!one.Found.InSubject) weight *= BodyOnly;
            // Split across every job the value reached.
            weight /= keys.Count;

            foreach (var key in keys)
            {
                // Doubt multiplies: what is left over from one piece of evidence
                // is what the next has to work on. Adding the weights instead
                // would let three weak signals pass 1.0, which is not a
                // probability of anything.
                doubt[key] = doubt.TryGetValue(key, out var held) ? held * (1 - weight) : 1 - weight;
                if (!best.TryGetValue(key, out var top) || weight > top.Weight)
                    best[key] = (weight, one.Found.Type, one.Found.Value);
            }
        }

        var matches = new List<JobMatch>();
        foreach (var (key, left) in doubt)
        {
            var confidence = Math.Round(1 - left, 4);
            var (_, kind, value) = best[key];
            matches.Add(new(key, confidence, DecisionFor(confidence), kind, value,
                Explain(confidence, kind, value)));
        }

        return [.. matches.OrderByDescending(one => one.Confidence).ThenBy(one => one.JobKey, StringComparer.Ordinal)];
    }

    /// <summary>
    /// What to do at a given confidence.
    ///
    /// <para>
    /// The comparison is on the stored number, not on a rounded one. A value
    /// that displays as 0.95 having been 0.9499 would otherwise be linked
    /// without asking on the strength of the rounding.
    /// </para>
    /// </summary>
    public static string DecisionFor(double confidence) =>
        confidence >= MailLink.AutoLink ? Decision.Link
        : confidence >= MailLink.Suggest ? Decision.Suggest
        : Decision.Unmatched;

    /// <summary>
    /// Why, in words a person working the queue can act on.
    ///
    /// The value is always named, because the first thing anybody checks is
    /// whether the number the machine matched on is the number they see in the
    /// mail.
    /// </summary>
    private static string Explain(double confidence, string kind, string value)
    {
        var what = kind switch
        {
            EmailExtraction.Kind.Container => "เลขตู้",
            EmailExtraction.Kind.JobCode => "เลขงาน",
            EmailExtraction.Kind.Booking => "เลข Booking",
            EmailExtraction.Kind.BillOfLading => "เลข B/L",
            _ => "ข้อมูลในอีเมล",
        };
        return DecisionFor(confidence) switch
        {
            Decision.Link => $"จับคู่จาก{what} {value}",
            Decision.Suggest => $"น่าจะเป็นงานนี้ จาก{what} {value} — รอยืนยัน",
            _ => $"{what} {value} ตรงกับงานนี้ แต่ยังไม่พอให้จับคู่",
        };
    }

    /// <summary>
    /// The one match to act on, or null when there is not one.
    ///
    /// <para>
    /// Null when nothing reached the linking threshold, and null when two jobs
    /// both did — two confident answers to one question is a question, not an
    /// answer, and the message goes to a person. The alternative is picking the
    /// higher of two numbers that the arithmetic never meant to be compared that
    /// finely.
    /// </para>
    /// </summary>
    public static JobMatch? OnlyLink(IReadOnlyList<JobMatch> matches)
    {
        var linkable = matches.Where(one => one.Decision == Decision.Link).ToList();
        return linkable.Count == 1 ? linkable[0] : null;
    }
}
