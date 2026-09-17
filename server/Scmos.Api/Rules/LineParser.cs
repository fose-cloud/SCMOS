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
    /// A container number in running text: an owner code of three letters
    /// and a U, J or Z (ISO 6346), then seven digits, with or without a space
    /// or dash between.
    ///
    /// Stricter than the register's own <c>[A-Z]{4}\d{7}</c> on purpose. That
    /// rule checks a cell that is supposed to be a container; this reads a
    /// chat message, where "SEAL 1234567" and a booking reference can sit on
    /// the same line, and the fourth letter is the one thing that tells a box
    /// from anything else that looks like one.
    /// </summary>
    private static readonly Regex Container =
        new(@"(?<![A-Za-z0-9])([A-Za-z]{3}[UuJjZz])[\s-]?(\d{7})(?!\d)", RegexOptions.Compiled);

    /// <summary>
    /// A Thai plate in running text: 70-1234, 700-3232, กข 1234, 1กข-1234.
    ///
    /// Digits before the dash must have the dash: without it "1500" in
    /// "จำนวน 1500 กก" is a plate, and the register's own plate rule accepts
    /// exactly that because it is checking a plate cell, not reading prose.
    /// Neither side may touch a digit, a dash or another Thai character —
    /// "16-09-2026" holds no plate and "ถึง 1234" holds no plate either.
    /// </summary>
    private static readonly Regex Plate = new(
        @"(?<![\d\u0E00-\u0E7FA-Za-z-])(?:(\d{1,3})-(\d{3,4})|(\d?[ก-ฮ]{1,3})[-\s]?(\d{3,4}))(?![\d-])",
        RegexOptions.Compiled);

    /// <summary>
    /// A reference as the register writes one: a booking (LC2606594,
    /// MAEU123456789), an ABS number, a D-code, a delivery note — letters and
    /// digits, six to twenty long, at least three of them digits. Not a time
    /// (has a colon), not a date (has a slash), not a plate (has a dash), not
    /// the twelve-digit job number or a container, which have rules of their
    /// own. Which field of which job it names is the register's to say.
    /// </summary>
    private static readonly Regex Reference =
        new(@"(?<![A-Za-z0-9-])(?=[A-Za-z0-9]{6,20}(?![A-Za-z0-9-]))(?=(?:[A-Za-z]*\d){3})[A-Za-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// A seal number after the word for one: "ซีล 123456", "seal TH1234567",
    /// "SEAL NO. 0012345". Four to twelve letters and digits. Only after the
    /// word — a bare run of digits is a phone, a weight, a time, anything.
    /// </summary>
    private static readonly Regex Seal = new(
        @"(?:ซีล|seal)(?:\s*(?:no\.?|number|เลข|เบอร์|#|:))?\s*[:#]?\s*([A-Za-z]{0,3}\d{4,10}|[A-Za-z0-9]{4,12})(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A Thai mobile or landline in running text: 081-2345678, 0812345678,
    /// 081 234 5678, 02-1234567. Written back as the register writes one,
    /// 0XX-XXXXXXX.
    /// </summary>
    private static readonly Regex Phone =
        new(@"(?<!\d)(0\d{1,2})[-\s]?(\d{3})[-\s]?(\d{4})(?!\d)", RegexOptions.Compiled);

    /// <summary>
    /// The words a haulier puts around a driver's name and number, which are
    /// not the name: labels, titles, and the courtesy particles.
    /// </summary>
    private static readonly HashSet<string> LabelWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "คนขับ", "พขร", "พขร.", "ชื่อ", "ทะเบียน", "รถ", "เบอร์", "โทร", "โทร.", "ติดต่อ", "คุณ", "นาย", "นาง", "นางสาว",
        "ครับ", "ค่ะ", "คะ", "นะครับ", "นะคะ", "จ้า", "จ้ะ", "driver", "tel", "tel.", "name", "plate", "phone", "mobile",
        // What is left around an arrival word once it is taken out.
        "แล้ว", "แล้วครับ", "แล้วค่ะ", "จะ", "ยัง", "ยังไม่", "ไม่", "ใกล้", "เกือบ", "กำลัง", "กำลังจะ",
        "ตู้", "ตู้ที่", "ซีล", "เลขซีล", "seal",
    };

    /// <summary>
    /// Words that make a message a question rather than a report. "ถึงโรงงานที่
    /// โมงคะ @Vad" — the second real message, 16 Sep 2026 — asks the driver
    /// when the truck arrived; it contains the arrival phrase and reports
    /// nothing.
    /// </summary>
    private static readonly string[] QuestionWords =
        ["กี่โมง", "ที่โมง", "หรือยัง", "รึยัง", "ไหม", "มั้ย", "หรือเปล่า", "หรือไม่", "?"];

    /// <summary>
    /// The clock that follows an arrival word: "ถึงโรงงาน 05:00", "ถึงลูกค้าแล้ว
    /// 10.25", "arrived customer 10:25". Not the port — "ถึงท่า 08:10" is the
    /// pickup, and writing it as the arrival would make every import trip look
    /// hours early.
    /// </summary>
    private static readonly Regex ArrivalClock = new(
        @"(?:ถึง(?!ท่า|ประมาณ)|arrived(?! ?pickup| ?port))[^\d]{0,24}?(?<!\d)([01]?\d|2[0-3])[:.]([0-5]\d)(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The one status a message cannot resolve on its own: the truck is at the
    /// customer's site. On an import or a Domestic run that is the delivery; on
    /// an export it is the pickup. <see cref="LineAuthority.ResolveSite"/>
    /// settles it once the job — and so its category — is known.
    /// </summary>
    public const string SiteArrival = "ARRIVED";

    /// <summary>
    /// The order the statuses a vendor can report come in, so that a message
    /// naming two of them is read as the later one: "ถึงโรงงาน 05:00 / ลงเสร็จ"
    /// is a delivery, not an arrival. <see cref="SiteArrival"/> sits beside
    /// DISPATCHED because that is the least it can mean.
    /// </summary>
    private static readonly string[] Progress =
        ["TRUCK_ASSIGNED", "DISPATCHED", SiteArrival, "PICKED_UP", "IN_TRANSIT", "DELIVERED"];

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
        ("ลงของเสร็จแล้ว", "DELIVERED"),
        ("ลงของเสร็จ", "DELIVERED"),
        // "ลงเสร็จ" — unloading done — is how the first real message from a
        // haulier's group put it, 16 Sep 2026. Not in the specification.
        ("ลงเสร็จแล้ว", "DELIVERED"),
        ("ลงเสร็จ", "DELIVERED"),
        ("ส่งของเสร็จ", "DELIVERED"),
        ("ส่งของแล้ว", "DELIVERED"),
        ("unloading done", "DELIVERED"),
        ("unloaded", "DELIVERED"),
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
        // "พขร. เดินทางอยู่ บริเวณเส้น อ. คลองหลวง" — the third real message,
        // 17 Sep 2026: the driver is on the road.
        ("กำลังเดินทาง", "IN_TRANSIT"),
        ("เดินทางอยู่", "IN_TRANSIT"),
        ("กำลังไป", "IN_TRANSIT"),
        ("on the way", "IN_TRANSIT"),
        ("in transit", "IN_TRANSIT"),

        ("ถึงท่าแล้ว", "DISPATCHED"),
        ("ถึงท่า", "DISPATCHED"),
        ("รถออกแล้ว", "DISPATCHED"),
        ("arrived pickup", "DISPATCHED"),
        ("arrived port", "DISPATCHED"),

        // At the customer's site — which end of the trip that is depends on
        // the job. Resolved by category once the job is known; see SiteArrival.
        ("ถึงโรงงานแล้ว", SiteArrival),
        ("ถึงโรงงาน", SiteArrival),
        ("ถึงคลังแล้ว", SiteArrival),
        ("ถึงคลัง", SiteArrival),
        ("ถึงหน้างาน", SiteArrival),
        // "ถึงแล้ว" — arrived, and nothing about where — is the place the
        // truck was going. Asked for on 17 Sep 2026: ถึงโรงงาน means ถึงแล้ว.
        ("ถึงแล้ว", SiteArrival),
        // "CATALITE 260900760321 3 ตู้ อยู่โรงงาน" — the fourth real message,
        // 17 Sep 2026: at the plant is having reached it.
        ("อยู่ที่โรงงาน", SiteArrival),
        ("อยู่โรงงาน", SiteArrival),
        ("อยู่หน้างาน", SiteArrival),
        ("อยู่ที่คลัง", SiteArrival),
        ("อยู่คลัง", SiteArrival),
        ("อยู่ที่ลูกค้า", "DELIVERED"),
        ("อยู่ลูกค้า", "DELIVERED"),
        ("at factory", SiteArrival),
        ("at site", SiteArrival),
        ("at customer", "DELIVERED"),
        ("arrived site", SiteArrival),
        ("arrived factory", SiteArrival),
        ("arrived plant", SiteArrival),
        ("arrived warehouse", SiteArrival),

        ("รับรถแล้ว", "TRUCK_ASSIGNED"),
        ("จัดรถแล้ว", "TRUCK_ASSIGNED"),
        ("truck assigned", "TRUCK_ASSIGNED"),
    ];

    /// <summary>
    /// What may stand right before "ถึง…" and turn a report into a forecast or
    /// a denial: "จะถึงโรงงานแล้ว" is the truck about to arrive, "ยังไม่ถึง" is
    /// the truck not there. Neither says it arrived, and before 17 Sep 2026
    /// both were read as though it had.
    /// </summary>
    private static readonly string[] NotYetWords = ["จะ", "ใกล้", "เกือบ", "ไม่"];

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
        /// <summary>The first plate in the message, when one was read. See <see cref="Plates"/>.</summary>
        string? Plate,
        string Remark,
        double Confidence,
        IReadOnlyList<string> MatchedRules,
        IReadOnlyList<string> Warnings,
        /// <summary>The container the message names, when it names exactly one.</summary>
        string? Container = null,
        /// <summary>
        /// Every plate in the message. Two is ordinary — a tractor and its
        /// trailer are written "75-4384 / 75-4385" — so unlike a second job
        /// number it is not a warning, and any of them may match the job.
        /// </summary>
        IReadOnlyList<string>? Plates = null,
        /// <summary>
        /// When the truck reached the customer's site, when the message says
        /// with a clock after an arrival word. This is the register's ARRIVAL
        /// DATE / TIME — what on-time delivery is measured from.
        /// </summary>
        DateTimeOffset? ArrivalTime = null,
        /// <summary>
        /// Booking-shaped tokens — a booking, an ABS, a D-code, a delivery
        /// note — that the register may know a job by. See <see cref="FindReferences"/>.
        /// </summary>
        IReadOnlyList<string>? References = null,
        /// <summary>Whether the message asks rather than tells. A question reports no status.</summary>
        bool Question = false,
        /// <summary>The driver's contact number, as the register writes one, when the message carries it.</summary>
        string? Phone = null,
        /// <summary>
        /// The driver's name, read as what is left of a message about a truck
        /// once everything else is taken out. Offered, never written unasked;
        /// the person approving sees it beside the message.
        /// </summary>
        string? Driver = null,
        /// <summary>How many boxes the message is about — "3 ตู้" — or null. See <see cref="FindBoxCount"/>.</summary>
        int? BoxCount = null,
        /// <summary>
        /// The seal number the message gives, or null. An export job's SEAL
        /// NO. is known only once the box is loaded, and the haulier is who
        /// knows it (asked for 17 Sep 2026, with the container, for exports).
        /// </summary>
        string? SealNumber = null,
        /// <summary>
        /// Whether <see cref="ArrivalTime"/> is the moment the message was sent
        /// rather than a clock the driver wrote. "ถึงโรงงาน" with no time means
        /// the truck is there now — asked for on 17 Sep 2026 — and the send
        /// time is the best evidence of when "now" was. Said back to the room
        /// and shown to the reviewer as such, so a driver who typed it late
        /// can send the clock.
        /// </summary>
        bool ArrivalAtSend = false)
    {
        /// <summary>
        /// Whether the message carries details the morning reminder asks for:
        /// a plate, a number, a name — or, for the job it names by number or
        /// booking, its box and seal. A container on its own is what the
        /// message is about, not a detail of it.
        /// </summary>
        public bool HasDetails => (Plates is not null && Plates.Count > 0) || Phone is not null || Driver is not null
            || SealNumber is not null
            || (Container is not null && (JobNumber is not null || (References is not null && References.Count > 0)));

        /// <summary>Whether this may update a job without somebody reading it first.</summary>
        public bool CanAutoProcess => Confidence >= AutoThreshold
            && (JobNumber is not null || Container is not null)
            && Warnings.Count == 0
            && (Status is not null || Delayed || Eta is not null);

        /// <summary>
        /// Whether there is anything to look a job up by. The first real
        /// messages carried a container and a plate and no job number at all —
        /// "L'Oréal / TEMU7592765 / สมใจ / 700-3232 / ถึงโรงงาน 05:00 / ลงเสร็จ".
        /// </summary>
        public bool HasReference => JobNumber is not null || Container is not null
            || (Plates is not null && Plates.Count > 0)
            || (References is not null && References.Count > 0);

        /// <summary>
        /// Whether the message is about a job at all: names one, reports on
        /// one, or gives a truck's number. A greeting, a "รับทราบ", a sticker's
        /// caption say none of that, and until 17 Sep 2026 each one was queued
        /// for review and answered with a request for the container number —
        /// which is a bot arguing with a room. Such a message is filed and
        /// left alone.
        /// </summary>
        public bool AboutAJob => HasReference || Status is not null || Delayed
            || Eta is not null || ArrivalTime is not null || Phone is not null;
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

    /// <summary>
    /// The container in a message, and how many there were. Null unless
    /// exactly one, for the reason <see cref="FindJobNumber"/> gives.
    /// </summary>
    public static (string? Container, int Found) FindContainer(string normalised)
    {
        var found = Container.Matches(normalised)
            .Select(one => (one.Groups[1].Value + one.Groups[2].Value).ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return (found.Count == 1 ? found[0] : null, found.Count);
    }

    /// <summary>
    /// The booking-shaped tokens in a message, upper case, without repeats,
    /// leaving out what other rules already read: the job number, the
    /// containers, the plates.
    /// </summary>
    public static IReadOnlyList<string> FindReferences(string normalised)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in Container.Matches(normalised).Cast<Match>())
            taken.Add((match.Groups[1].Value + match.Groups[2].Value).ToUpperInvariant());
        foreach (var number in JobCodes.All(normalised)) taken.Add(number);
        // A phone number is the driver's, not a reference to a job.
        foreach (var match in Phone.Matches(normalised).Cast<Match>())
            taken.Add(match.Value.Replace("-", "").Replace(" ", ""));

        return [.. Reference.Matches(normalised)
            .Select(one => one.Value.ToUpperInvariant())
            .Where(one => !taken.Contains(one))
            // The container's digits alone, or its owner code, are not a reference.
            .Where(one => !taken.Any(had => had.Contains(one, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Take(5)];
    }

    /// <summary>The seal number the message gives, upper case, or null. See <see cref="Seal"/>.</summary>
    public static string? FindSeal(string normalised)
    {
        var match = Seal.Match(normalised);
        if (!match.Success) return null;
        var seal = match.Groups[1].Value.ToUpperInvariant();
        // "ซีล" followed by the container itself is not a seal.
        return ContainerNumbers.IsShaped(seal) ? null : seal;
    }

    /// <summary>The first phone number in a message, as 0XX-XXXXXXX, or null.</summary>
    public static string? FindPhone(string normalised)
    {
        var match = Phone.Match(normalised);
        if (!match.Success) return null;
        return $"{match.Groups[1].Value}-{match.Groups[2].Value}{match.Groups[3].Value}";
    }

    /// <summary>
    /// The driver's name: the Thai words left over once the numbers, the
    /// plate, the status phrase, the dates, the clocks and the labels are
    /// taken out of a message about a truck. One to three words, Thai
    /// letters only. Null when nothing is left, or when the message carries
    /// no plate and no number — a name on its own is not a truck.
    /// </summary>
    public static string? FindDriver(string normalised, IReadOnlyList<string> plates, string? phone)
    {
        if (plates.Count == 0 && phone is null) return null;

        var text = normalised;
        // Every status word, longest first, whether or not it reported
        // anything — "จะถึงโรงงานแล้ว" reports no arrival and is no name either.
        foreach (var (keyword, _) in StatusWords.OrderByDescending(one => one.Keyword.Length))
            text = Regex.Replace(text, Regex.Escape(keyword), " ", RegexOptions.IgnoreCase);
        text = Phone.Replace(text, " ");
        text = Seal.Replace(text, " ");
        text = Plate.Replace(text, " ");
        text = Container.Replace(text, " ");
        text = Clock.Replace(text, " ");
        // Anything with a digit or a Latin letter is a reference, a date, a
        // time, a customer's name in English — not a Thai given name.
        var words = text.Split([' ', '/', ',', '|', '(', ')', ':', ';', '-', '@', '#', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.All(c => c >= '฀' && c <= '๿'))
            .Where(word => word.Length >= 2 && word.Length <= 30)
            .Where(word => !LabelWords.Contains(word))
            .Where(word => !QuestionWords.Contains(word))
            .Take(3)
            .ToList();
        return words.Count == 0 ? null : string.Join(" ", words);
    }

    /// <summary>Whether the message asks something rather than reporting it.</summary>
    public static bool IsQuestion(string normalised)
    {
        var haystack = normalised.ToLowerInvariant();
        return QuestionWords.Any(word => haystack.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>Every plate in a message, as written, without duplicates.</summary>
    public static IReadOnlyList<string> FindPlates(string normalised) =>
        [.. Plate.Matches(normalised).Select(one => one.Value.Trim()).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// A plate reduced to what identifies it: the province off, then only its
    /// letters and digits. "700-3232", "700 3232" and "700-3232 กทม." are one
    /// plate; the register writes all three.
    /// </summary>
    public static string PlateKey(string? plate)
    {
        var text = Formats.StripProvince(Formats.Clean(plate));
        var built = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsDigit(c) || (c >= 'ก' && c <= 'ฮ')) built.Append(c);
        }
        return built.ToString();
    }

    /// <summary>
    /// Every status keyword in a message, longest first, each span read once —
    /// "ถึงลูกค้าแล้ว" is not also "ถึงลูกค้า".
    /// </summary>
    public static IReadOnlyList<(string Status, string Keyword)> FindStatuses(string normalised)
    {
        var haystack = normalised.ToLowerInvariant().ToCharArray();
        var found = new List<(string Status, string Keyword, int At)>();
        foreach (var (keyword, status) in StatusWords.OrderByDescending(one => one.Keyword.Length))
        {
            var needle = keyword.ToLowerInvariant();
            var from = 0;
            while (true)
            {
                var at = new string(haystack).IndexOf(needle, from, StringComparison.Ordinal);
                if (at < 0) break;
                // "จะถึงโรงงาน" is not "ถึงโรงงาน": the span is consumed so no
                // shorter arrival word is read out of it, and reports nothing.
                if (!NotYet(haystack, at, needle)) found.Add((status, keyword, at));
                // Blanked so a shorter keyword inside this one is not read again.
                for (var i = at; i < at + needle.Length; i++) haystack[i] = ' ';
                from = at + needle.Length;
            }
        }
        return [.. found.OrderBy(one => one.At).Select(one => (one.Status, one.Keyword))];
    }

    /// <summary>Whether an arrival word at <paramref name="at"/> has "จะ", "ใกล้", "เกือบ" or "ไม่" right before it.</summary>
    private static bool NotYet(char[] haystack, int at, string needle)
    {
        if (!needle.StartsWith("ถึง", StringComparison.Ordinal)) return false;
        var before = new string(haystack, 0, at).TrimEnd();
        return NotYetWords.Any(word => before.EndsWith(word, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether a status keyword is the truck reaching the customer's site —
    /// "ถึงโรงงาน", "ถึงลูกค้าแล้ว", "arrived site" — and not the port, and not
    /// a later step such as "ลงเสร็จ", after which the arrival was some time
    /// ago and is not known.
    /// </summary>
    public static bool IsArrivalWord(string? keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return false;
        var word = keyword.ToLowerInvariant();
        if (word.StartsWith("ถึง", StringComparison.Ordinal)) return !word.StartsWith("ถึงท่า", StringComparison.Ordinal);
        // "อยู่โรงงาน", "at site": there now, which is having arrived.
        if (word.StartsWith("อยู่", StringComparison.Ordinal) || word.StartsWith("at ", StringComparison.Ordinal)) return true;
        return word.StartsWith("arrived", StringComparison.Ordinal)
            && !word.Contains("pickup", StringComparison.Ordinal) && !word.Contains("port", StringComparison.Ordinal);
    }

    /// <summary>
    /// "3 ตู้" — how many boxes the message speaks for. A job number covers
    /// one row per box; a message naming the number and the count is about
    /// every one of them (LineAuthority.Decide). "ตู้ที่ 2" names one box and
    /// is not a count.
    /// </summary>
    private static readonly Regex Boxes = new(@"(?<![\d.:])(\d{1,2})\s*ตู้(?!ที่)", RegexOptions.Compiled);

    /// <summary>How many boxes the message says it is about, or null.</summary>
    public static int? FindBoxCount(string normalised)
    {
        var match = Boxes.Match(normalised);
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) && count > 0 ? count : null;
    }

    /// <summary>The minute the message was sent, in Bangkok — the arrival when the driver wrote no clock.</summary>
    public static DateTimeOffset SentAt(DateTimeOffset receivedAt)
    {
        var bangkok = TimeSpan.FromHours(7);
        var here = receivedAt.ToOffset(bangkok);
        return new DateTimeOffset(here.Year, here.Month, here.Day, here.Hour, here.Minute, 0, bangkok);
    }

    /// <summary>
    /// The status a message reports, and the keyword that said so.
    ///
    /// When a message names more than one, the one furthest along
    /// <see cref="Progress"/> wins: a driver writing "ถึงโรงงาน 05:00 / ลงเสร็จ"
    /// is reporting the delivery and mentioning the arrival on the way. Used
    /// to be the longest keyword, which read that message as the arrival.
    /// </summary>
    public static (string? Status, string? Keyword) FindStatus(string normalised, bool forecast = false)
    {
        var all = FindStatuses(normalised);
        // "ประมาณ 10.00 รถถึงโรงงาน" says when the truck will be there, not
        // that it is. The third real message, 17 Sep 2026, was read as the
        // arrival and approved as DELIVERED while the truck was still on the
        // road. An arrival word in a message with an estimate reports nothing
        // — unless it says the arrival is done ("ถึงโรงงานแล้ว ประมาณ 10.25"),
        // which is a report with a rough clock.
        if (forecast)
            all = [.. all.Where(one => !IsArrivalWord(one.Keyword) || one.Keyword.EndsWith("แล้ว", StringComparison.Ordinal))];
        if (all.Count == 0) return (null, null);
        var best = all.MaxBy(one => Array.IndexOf(Progress, one.Status));
        return (best.Status, best.Keyword);
    }

    /// <summary>The clock after an arrival word, on the day the message arrived, or null.</summary>
    public static DateTimeOffset? FindArrival(string normalised, DateTimeOffset receivedAt)
    {
        var match = ArrivalClock.Match(normalised);
        if (!match.Success) return null;
        return ResolveTime($"{match.Groups[1].Value}:{match.Groups[2].Value}", receivedAt);
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
        foreach (var (_, keyword) in FindStatuses(normalised))
        {
            reason = Regex.Replace(reason, Regex.Escape(keyword), " ", RegexOptions.IgnoreCase);
        }
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
        if (found > 1) warnings.Add("many-job-numbers");
        if (jobNumber is not null) rules.Add("job-number");

        // The container and the plates are read from every message, not only
        // one about assigning a truck: since 16 Sep 2026 they are how a job is
        // found when the haulier writes no job number, which is every time so
        // far.
        var (container, containers) = FindContainer(text);
        if (containers > 1) warnings.Add("many-containers");
        if (container is not null) rules.Add($"container:{container}");

        var plates = FindPlates(text);
        foreach (var plate in plates) rules.Add($"plate:{plate}");

        // A booking, an ABS, a D-code: the second real message, 16 Sep 2026,
        // named its job by the booking alone — "AKZO NOBEL // LC2606594
        // 16/09/2026 -- 14:00".
        var references = FindReferences(text);
        foreach (var reference in references) rules.Add($"ref:{reference}");

        var boxCount = FindBoxCount(text);
        if (boxCount is not null) rules.Add($"boxes:{boxCount}");

        // Nothing to find a job by. Was "no-job-number", which the rows already
        // stored still carry and the screen still names.
        if (jobNumber is null && container is null && plates.Count == 0 && references.Count == 0 && found == 0)
            warnings.Add("no-reference");

        var forecast = IsForecast(text);
        var (status, statusWord) = FindStatus(text, forecast);
        foreach (var (_, keyword) in FindStatuses(text)) rules.Add($"status:{keyword}");
        if (forecast && FindStatuses(text).Any(one => IsArrivalWord(one.Keyword)) && !IsArrivalWord(statusWord))
            rules.Add("arrival-is-estimate");

        // A question carries the words of a report and reports nothing.
        // "ถึงโรงงานที่โมงคะ" asks when; it does not say the truck arrived.
        var question = IsQuestion(text);
        if (question)
        {
            rules.Add("question");
            status = null;
            statusWord = null;
        }

        var (delayCategory, delayBasis, delayConfidence) = FindDelay(text, statusWord);
        if (delayCategory is not null) rules.Add($"delay:{delayCategory}");
        // The classifier drops to 0.6 when a reason could be two categories.
        // That uncertainty is the reviewer's to settle, not this parser's to
        // round away — a delay booked to the wrong party is an argument with a
        // supplier that nobody can win.
        if (delayCategory is not null && delayConfidence < 0.9) warnings.Add("delay-ambiguous");

        var time = ResolveTime(text, receivedAt);
        DateTimeOffset? eventTime = null;
        DateTimeOffset? eta = null;
        if (time is not null)
        {
            rules.Add(forecast ? "eta" : "event-time");
            if (forecast) eta = time; else eventTime = time;
        }

        // A second clock reading in one message is two times and no way to say
        // which is which. Reported rather than guessed at — unless one of them
        // stands after an arrival word, which says which it is.
        var arrival = forecast ? null : FindArrival(text, receivedAt);
        if (arrival is not null) rules.Add($"arrival:{arrival.Value:HH:mm}");
        if (Clock.Matches(text).Count > 1 && arrival is null) warnings.Add("many-times");

        // "ถึงโรงงาน" and no clock at all: the truck is there as the driver
        // types. The send time is the arrival, and is marked as such — only
        // when the arrival is what the message reports; after "ลงเสร็จ" the
        // arrival was earlier and is not known.
        var arrivalAtSend = false;
        if (arrival is null && !forecast && !question && Clock.Matches(text).Count == 0 && IsArrivalWord(statusWord))
        {
            arrival = SentAt(receivedAt);
            arrivalAtSend = true;
            rules.Add($"arrival-at-send:{arrival.Value:HH:mm}");
        }

        // The truck's details — what the morning reminder asks for and what
        // the haulier answers with: "LC2606594 70-1234 สมชาย ใจดี 081-2345678".
        var phone = question ? null : FindPhone(text);
        if (phone is not null) rules.Add($"phone:{phone}");
        var seal = question ? null : FindSeal(text);
        if (seal is not null) rules.Add($"seal:{seal}");
        var driver = question ? null : FindDriver(text, plates, phone);
        if (driver is not null) rules.Add($"driver:{driver}");
        var details = plates.Count > 0 || phone is not null || driver is not null || seal is not null
            || (container is not null && (jobNumber is not null || references.Count > 0));

        var delayed = delayCategory is not null && !question;
        // A message that names a job and gives its truck has said something
        // worth applying, with or without a status.
        var understood = status is not null || delayed || eta is not null
            || (details && (jobNumber is not null || container is not null || references.Count > 0));
        if (question) warnings.Add("question");
        else if (!understood) warnings.Add("nothing-understood");

        return new Parsed(
            jobNumber, status, eventTime, eta, delayed, question ? null : delayCategory, question ? null : delayBasis,
            Plate: plates.Count > 0 ? plates[0] : null,
            Remark: text,
            Confidence: Score(jobNumber, status, delayed, eta, warnings, container, plates.Count > 0 || references.Count > 0),
            MatchedRules: rules,
            Warnings: warnings,
            Container: container,
            Plates: plates,
            ArrivalTime: question ? null : arrival,
            References: references,
            Question: question,
            Phone: phone,
            Driver: driver,
            BoxCount: boxCount,
            SealNumber: seal,
            ArrivalAtSend: arrivalAtSend);
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
        IReadOnlyList<string> warnings, string? container = null, bool plate = false)
    {
        // A job number or a container names one trip. A plate names a truck,
        // which runs many — enough to find the candidates, never enough to
        // reach the threshold on its own.
        var score = jobNumber is not null || container is not null ? 0.55
            : plate ? 0.35
            : 0;
        if (score == 0) return 0;
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
