using System.Globalization;
using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// Reading a vendor's LINE message into something SCMOS can act on.
///
/// <para>
/// Rules and keywords, no AI. That is V1's requirement and it is also the right
/// order: a deterministic parser can be checked against a fixed list of real
/// messages and will give the same answer every time, which is what an audit
/// trail needs. The interface is shaped so a model can be asked later about the
/// messages this cannot read, without the transport rules changing.
/// </para>
///
/// <para>
/// Pure on purpose — no database, no HTTP, no clock of its own. The caller
/// supplies the moment the message arrived, because "10.25" means nothing until
/// somebody says which day it belongs to, and because a parser that read the
/// server's clock could not be checked.
/// </para>
///
/// <para>
/// <b>The rule that matters most: when in doubt, say so.</b> Every path that
/// cannot be certain lowers the confidence rather than picking the likeliest
/// answer. A message that updates the wrong job is worse than one that waits
/// for a person, because the second is visible and the first is not.
/// </para>
/// </summary>
public static class LineParser
{
    /// <summary>
    /// Above this a message may update a job on its own; below it a person looks.
    ///
    /// Deterministic, so this is not a probability — it is a count of how much
    /// of the message was understood. 0.90 means the job number and the status
    /// were both read cleanly and nothing about the message was surprising.
    /// </summary>
    public const double AutoThreshold = 0.90;

    /// <summary>A time of day: 10:25, 10.25, 1025 is not accepted.</summary>
    private static readonly Regex Clock =
        new(@"(?<!\d)([01]?\d|2[0-3])[:.]([0-5]\d)(?!\d)", RegexOptions.Compiled);

    /// <summary>
    /// What a message says happened, mapped to the ladder SCMOS already has.
    ///
    /// Longest keyword first, so "ถึงลูกค้าแล้ว" is not read as "ถึง". The list
    /// is ordered by length at first use rather than by hand, because a hand
    /// ordering is a thing that rots.
    ///
    /// These map onto STATUS_LADDER and add nothing to it. Two are worth saying
    /// out loud:
    ///
    /// "เสร็จแล้ว" means the delivery is done, and becomes DELIVERED — not
    /// COMPLETED, which in SCMOS is after documents and billing. A vendor
    /// cannot close a job nobody has billed.
    ///
    /// "ถึงท่า" and "ถึงลูกค้า" are different arrivals. The first is the pickup
    /// and stays at DISPATCHED; only the second is DELIVERED.
    /// </summary>
    public static readonly (string Keyword, string Status)[] StatusWords =
    [
        ("ถึงลูกค้าแล้ว", "DELIVERED"),
        ("ถึงลูกค้า", "DELIVERED"),
        ("ส่งเสร็จแล้ว", "DELIVERED"),
        ("ส่งเสร็จ", "DELIVERED"),
        ("ลงของเสร็จ", "DELIVERED"),
        ("arrived customer", "DELIVERED"),
        ("arrived delivery", "DELIVERED"),
        ("delivered", "DELIVERED"),

        ("ออกจากท่าแล้ว", "PICKED_UP"),
        ("ออกจากท่า", "PICKED_UP"),
        ("โหลดเสร็จแล้ว", "PICKED_UP"),
        ("โหลดเสร็จ", "PICKED_UP"),
        ("รับของแล้ว", "PICKED_UP"),
        ("loading completed", "PICKED_UP"),
        ("loaded", "PICKED_UP"),
        ("departed", "PICKED_UP"),
        ("picked up", "PICKED_UP"),

        ("กำลังไปลูกค้า", "IN_TRANSIT"),
        ("กำลังไปส่ง", "IN_TRANSIT"),
        ("ระหว่างทาง", "IN_TRANSIT"),
        ("กำลังไป", "IN_TRANSIT"),
        ("on the way", "IN_TRANSIT"),
        ("in transit", "IN_TRANSIT"),

        ("ถึงท่าแล้ว", "DISPATCHED"),
        ("ถึงท่า", "DISPATCHED"),
        ("ถึงโรงงาน", "DISPATCHED"),
        ("รถออกแล้ว", "DISPATCHED"),
        ("arrived pickup", "DISPATCHED"),
        ("arrived port", "DISPATCHED"),

        ("รับรถแล้ว", "TRUCK_ASSIGNED"),
        ("จัดรถแล้ว", "TRUCK_ASSIGNED"),
        ("truck assigned", "TRUCK_ASSIGNED"),
    ];

    /// <summary>Words that mean the time being given is a forecast, not an event.</summary>
    private static readonly string[] EtaWords =
        ["eta", "คาดถึง", "คาดว่าถึง", "ประมาณ", "น่าจะถึง", "จะถึง", "ถึงประมาณ"];

    /// <summary>
    /// What one message was understood to say.
    ///
    /// Everything is nullable because a message is allowed to say only some of
    /// it — "260600800773 รถติด" is a delay with no status and no time, and that
    /// is a perfectly good message.
    /// </summary>
    public record Parsed(
        string? JobNumber,
        string? Status,
        DateTimeOffset? EventTime,
        DateTimeOffset? Eta,
        bool Delayed,
        DelayCategory? DelayCategory,
        /// <summary>Which words the delay classifier matched, so a reviewer can disagree.</summary>
        string? DelayBasis,
        string? Plate,
        string Remark,
        double Confidence,
        IReadOnlyList<string> MatchedRules,
        IReadOnlyList<string> Warnings)
    {
        /// <summary>Whether this may update a job without somebody reading it first.</summary>
        public bool CanAutoProcess => Confidence >= AutoThreshold
            && JobNumber is not null
            && Warnings.Count == 0
            && (Status is not null || Delayed || Eta is not null);
    }

    /// <summary>
    /// Tidies a message without destroying it.
    ///
    /// The raw text is kept by the caller; this is only what the keyword rules
    /// read. Full-width and Thai punctuation is folded to ASCII so that a
    /// message typed on a phone keyboard matches the same rule as one typed on
    /// a laptop.
    /// </summary>
    public static string Normalise(string? text)
    {
        // Placeholders out first, through the rule the register already uses —
        // a message of "-" is not a message.
        var value = Formats.Clean(text);
        if (value.Length == 0) return "";

        value = value
            .Replace(' ', ' ')
            .Replace('：', ':')
            .Replace('．', '.')
            .Replace('，', ',')
            .Replace('–', '-')
            .Replace('—', '-');

        // Newlines are separators, not content: a two-line message is one
        // message and the rules read across the break.
        value = Regex.Replace(value, @"\s+", " ");
        return value.Trim();
    }

    /// <summary>
    /// The job number in a message, and whether there was exactly one.
    ///
    /// Two numbers is not a message to guess at. A vendor writing about two
    /// trips in one line is asking for both to be updated, and V1 does not do
    /// that — it queues for review, which is the whole point of having a review
    /// queue.
    /// </summary>
    /// <remarks>
    /// The rule itself lives in <see cref="JobCodes"/>, because the mail
    /// extractor reads the same twelve digits out of a subject line, and two
    /// readings of "what is a job number" would drift apart.
    /// </remarks>
    public static (string? Number, int Found) FindJobNumber(string normalised) =>
        JobCodes.FindOne(normalised);

    /// <summary>The status a message reports, and the keyword that said so.</summary>
    public static (string? Status, string? Keyword) FindStatus(string normalised)
    {
        var haystack = normalised.ToLowerInvariant();
        // Longest first so a keyword that contains another wins.
        foreach (var (keyword, status) in StatusWords.OrderByDescending(one => one.Keyword.Length))
        {
            if (haystack.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal))
                return (status, keyword);
        }
        return (null, null);
    }

    /// <summary>
    /// Why a trip is late, if the message says — through the classifier the
    /// rest of SCMOS already uses.
    ///
    /// Not a second keyword table. <see cref="DelayReasons.Classify"/> reads
    /// Thai and English, knows the seven categories the delay register counts,
    /// and returns which words it matched. A LINE-specific copy of that would
    /// have been the third place in this repository where "รถติด" is given a
    /// meaning, and the two would eventually have disagreed about which party a
    /// delay is booked to.
    ///
    /// Its own confidence is carried through rather than flattened: a reason
    /// that could be two categories comes back at 0.6, and that has to reach
    /// the review queue rather than being written as though it were certain.
    /// </summary>
    public static (DelayCategory? Category, string? Basis, double Confidence) FindDelay(
        string normalised, string? statusKeyword = null)
    {
        // The status phrase comes out first, and this is not a detail.
        //
        // Classify was written to read a delay *reason* — the text of "why was
        // this late" — not a whole message. Fed one, its Customer words match
        // the ลูกค้า in "ถึงลูกค้าแล้ว", so every delivery report came back as a
        // customer delay. Fed the same message with the status phrase removed,
        // there is nothing left to misread.
        //
        // It also fixes the other half: "รถติด กำลังไปลูกค้า" matched Traffic
        // and Customer at once and dropped to 0.6, which sent an entirely
        // ordinary message to the review queue.
        var reason = normalised;
        if (!string.IsNullOrEmpty(statusKeyword))
        {
            reason = Regex.Replace(reason, Regex.Escape(statusKeyword), " ", RegexOptions.IgnoreCase);
        }

        var suggestion = DelayReasons.Classify(reason);
        // Confidence 0 is "no word here means anything", which is not a delay.
        if (suggestion.Confidence <= 0) return (null, null, 0);
        return (suggestion.Category, suggestion.Basis, suggestion.Confidence);
    }

    /// <summary>
    /// A clock reading resolved against the day the message arrived.
    ///
    /// Bangkok, because that is where the trucks are and what the driver meant.
    /// The caller passes the arrival instant; this converts to +07:00, puts the
    /// time on that date, and hands back an offset the rest of the system can
    /// store as UTC.
    ///
    /// One judgement, and it is worth stating: a time more than two hours ahead
    /// of when the message arrived is read as *yesterday*, not today. A driver
    /// writing "ถึงลูกค้า 23:50" just after midnight is reporting the trip that
    /// has just ended. Anything less than two hours ahead is clock drift or
    /// somebody rounding up, and stays on today.
    /// </summary>
    public static DateTimeOffset? ResolveTime(string clock, DateTimeOffset receivedAt)
    {
        var match = Clock.Match(clock);
        if (!match.Success) return null;

        var hour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        var bangkok = TimeSpan.FromHours(7);
        var here = receivedAt.ToOffset(bangkok);
        var candidate = new DateTimeOffset(here.Year, here.Month, here.Day, hour, minute, 0, bangkok);

        if (candidate - here > TimeSpan.FromHours(2)) candidate = candidate.AddDays(-1);
        return candidate;
    }

    /// <summary>
    /// Whether the time in a message is a forecast rather than something that
    /// happened.
    ///
    /// Read from the words around it, not from the status. "ถึงลูกค้าแล้ว 10:25"
    /// is an arrival; "รถติด คาดถึง 14:30" is an estimate, and writing the
    /// second into an actual-arrival field would record a delivery that has not
    /// happened.
    /// </summary>
    public static bool IsForecast(string normalised)
    {
        var haystack = normalised.ToLowerInvariant();
        return EtaWords.Any(word => haystack.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>Reads one message.</summary>
    public static Parsed Parse(string? raw, DateTimeOffset receivedAt)
    {
        var text = Normalise(raw);
        var rules = new List<string>();
        var warnings = new List<string>();

        var (jobNumber, found) = FindJobNumber(text);
        if (found == 0) warnings.Add("no-job-number");
        if (found > 1) warnings.Add("many-job-numbers");
        if (jobNumber is not null) rules.Add("job-number");

        var (status, statusWord) = FindStatus(text);
        if (statusWord is not null) rules.Add($"status:{statusWord}");

        var (delayCategory, delayBasis, delayConfidence) = FindDelay(text, statusWord);
        if (delayCategory is not null) rules.Add($"delay:{delayCategory}");
        // The classifier drops to 0.6 when a reason could be two categories.
        // That uncertainty is the reviewer's to settle, not this parser's to
        // round away — a delay booked to the wrong party is an argument with a
        // supplier that nobody can win.
        if (delayCategory is not null && delayConfidence < 0.9) warnings.Add("delay-ambiguous");

        var forecast = IsForecast(text);
        var time = ResolveTime(text, receivedAt);
        DateTimeOffset? eventTime = null;
        DateTimeOffset? eta = null;
        if (time is not null)
        {
            rules.Add(forecast ? "eta" : "event-time");
            if (forecast) eta = time; else eventTime = time;
        }

        // A second clock reading in one message is two times and no way to say
        // which is which. Reported rather than guessed at.
        if (Clock.Matches(text).Count > 1) warnings.Add("many-times");

        // A plate, and only on a message about assigning a truck. Read as the
        // last token the register's own plate rule accepts, rather than by a
        // pattern of this file's own — Formats.IsPlate already knows what a
        // Thai plate looks like and already strips the province off one.
        string? plateValue = null;
        if (status == "TRUCK_ASSIGNED")
        {
            plateValue = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(Formats.IsPlate);
            if (plateValue is not null) rules.Add("plate");
        }

        var delayed = delayCategory is not null;
        var understood = status is not null || delayed || eta is not null;
        if (!understood) warnings.Add("nothing-understood");

        return new Parsed(
            jobNumber, status, eventTime, eta, delayed, delayCategory, delayBasis, plateValue,
            Remark: text,
            Confidence: Score(jobNumber, status, delayed, eta, warnings),
            MatchedRules: rules,
            Warnings: warnings);
    }

    /// <summary>
    /// How much of the message was understood, from 0 to 1.
    ///
    /// A count, not a probability — every term below is something the parser
    /// either read or did not. Written out rather than tuned so that a message
    /// which lands just under the threshold can be explained to the person who
    /// has to review it.
    ///
    /// Any warning caps the result below the auto threshold. That is the rule
    /// doing the real work: a message with two job numbers cannot reach 0.90
    /// however clear the rest of it is.
    /// </summary>
    public static double Score(
        string? jobNumber, string? status, bool delayed, DateTimeOffset? eta,
        IReadOnlyList<string> warnings)
    {
        if (jobNumber is null) return 0;

        var score = 0.55;                       // one job number, unambiguous
        if (status is not null) score += 0.35;  // and it says what happened
        // A message that gives only a reason — "260600800773 รถเสีย" — is as
        // clear as one that gives only a status, and worth the same. Scored
        // lower at first, which put every delay-only message in the review
        // queue for no reason anybody could have explained.
        else if (delayed) score += 0.35;
        else if (eta is not null) score += 0.20;
        if (delayed && status is not null) score += 0.05;

        if (warnings.Count > 0) score = Math.Min(score, AutoThreshold - 0.05);
        return Math.Round(Math.Clamp(score, 0, 1), 2);
    }
}
