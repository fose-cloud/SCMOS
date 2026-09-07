using Scmos.Api.Data;

namespace Scmos.Api.Rules;

/// <summary>
/// Whether a LINE message may move a job, and which job it would move.
///
/// <para>
/// <see cref="LineParser"/> answers what a message says. This answers whether
/// the person who sent it is allowed to say it about the job they named — the
/// security boundary of the whole integration. A vendor group is a chat room
/// anybody in it can type into, so "the message was understood" is nowhere near
/// enough to change the operational record.
/// </para>
///
/// <para>
/// <b>A job number is not a job.</b> Measured on the register as it stands:
/// 1,445 rows carry a twelve-digit job code, and those are only 1,038 distinct
/// numbers. 236 numbers sit on more than one row, covering 643 rows — 44% of
/// them. A number is a booking, and a booking can be several containers; some
/// of the rest are the same booking imported twice. So a number picks out a
/// set, and this returns the set rather than choosing a member of it.
/// </para>
///
/// <para>
/// Pure, so <c>--check-line</c> can prove it with no database. The caller looks
/// the rows up; the judgement is here.
/// </para>
/// </summary>
public static class LineAuthority
{
    /// <summary>
    /// Why a message did or did not reach a job, in a word a screen can group by.
    ///
    /// These land in <c>line_events.error_code</c> beside the ones the parser
    /// produces, which is why they read the same way.
    /// </summary>
    public static class Outcome
    {
        /// <summary>One job, this speaker's, and the status moves forward.</summary>
        public const string Ok = "ok";

        /* ---- the speaker ---- */

        /// <summary>The LINE group is not mapped to a supplier. Nobody owns this room.</summary>
        public const string UnknownGroup = "unknown-group";
        public const string GroupInactive = "group-inactive";

        /// <summary>A customer's or an internal room does not move a vendor's job.</summary>
        public const string GroupNotVendor = "group-not-vendor";

        /* ---- the job ---- */

        public const string NoSuchJob = "no-such-job";

        /// <summary>
        /// The number exists and belongs to somebody else. The refusal this
        /// whole module is for.
        /// </summary>
        public const string NotYourJob = "not-your-job";

        /// <summary>Several of the speaker's own rows carry it. A person chooses.</summary>
        public const string ManyJobs = "many-jobs";

        /* ---- the move ---- */

        /// <summary>A number and no status: nothing to apply.</summary>
        public const string NoStatus = "no-status";

        /// <summary>Reported a stage this job's category does not have.</summary>
        public const string NotOnLadder = "not-on-ladder";

        /// <summary>The job sits at a status that is not a controlled code.</summary>
        public const string StatusUnknown = "job-status-unknown";

        /// <summary>Already there. Not an error, and not a change.</summary>
        public const string AlreadyThere = "already-there";

        /// <summary>Would move the job back down the ladder.</summary>
        public const string Backwards = "backwards";

        /// <summary>Finished or cancelled. A vendor does not reopen either.</summary>
        public const string JobClosed = "job-closed";

        /// <summary>Parked by a person. A person releases it.</summary>
        public const string JobHeld = "job-held";
    }

    /// <summary>The room the message came from, as much as the decision needs.</summary>
    /// <param name="Known">Whether a mapping to a supplier exists at all.</param>
    /// <param name="Active">Whether that mapping is still in use.</param>
    /// <param name="GroupType">VENDOR · INTERNAL · CUSTOMER · OTHER.</param>
    /// <param name="SupplierName">The supplier the room speaks for.</param>
    public record SpeakerGroup(bool Known, bool Active, string GroupType, string SupplierName);

    /// <summary>One row the job number might mean, as much as the decision needs.</summary>
    public record JobCandidate(string Key, string Category, string Carrier, string Status);

    /// <summary>
    /// What should happen to the message.
    /// </summary>
    /// <param name="Result">One of <see cref="Outcome"/>.</param>
    /// <param name="Keys">
    /// The speaker's own rows carrying the number — one when the result is
    /// <c>ok</c>, several when it is <c>many-jobs</c>, none otherwise. Carried
    /// even when nothing can be applied, because the review screen needs to show
    /// what the choice was between.
    /// </param>
    /// <param name="From">The status the job holds now, when there is one job.</param>
    /// <param name="To">The status the message would set.</param>
    /// <param name="Detail">A sentence for a person, in Thai, or empty.</param>
    public record LineDecision(
        string Result,
        IReadOnlyList<string> Keys,
        string From,
        string To,
        string Detail)
    {
        /// <summary>Whether this decision should change a job at all.</summary>
        public bool Applies => Result == Outcome.Ok;
    }

    private static readonly string[] NoKeys = [];

    /// <summary>
    /// A carrier name with the punctuation taken out, for comparison only.
    ///
    /// The register writes the same haulier as "T.O.", "T.O", "TO." and "TO",
    /// and the supplier list writes one of them. Matching the exact string left
    /// 5 of 34 carrier names unresolved; matching on letters and digits alone
    /// resolves all 34 — and, checked against the supplier list, causes no two
    /// suppliers to collide. Both halves of that matter: a looser match on the
    /// boundary that decides whose job a vendor may touch is only safe because
    /// the collision count was measured and is zero.
    /// </summary>
    public static string NameKey(string? name)
    {
        var text = name ?? "";
        var built = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) built.Append(char.ToUpperInvariant(c));
        }
        return built.ToString();
    }

    /// <summary>Whether a job's carrier is the supplier this room speaks for.</summary>
    public static bool SameCarrier(string? supplier, string? carrier)
    {
        var a = NameKey(supplier);
        var b = NameKey(carrier);
        // An empty name matches nothing. A job with no carrier is not every
        // vendor's job; it is nobody's until somebody assigns it.
        return a.Length > 0 && a == b;
    }

    /// <summary>
    /// Where a status sits on its category's ladder, or -1.
    ///
    /// CANCELLED and HOLD are stored at the end of every ladder but are not
    /// rungs on it — they are places a job leaves the ladder for. Ranking them
    /// would put HOLD above COMPLETED and make releasing a hold look like
    /// progress.
    /// </summary>
    public static int Rank(string category, string status)
    {
        if (string.Equals(status, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)) return -1;
        if (string.Equals(status, JobStatus.Hold, StringComparison.OrdinalIgnoreCase)) return -1;

        var ladder = JobStatus.For(category);
        for (var i = 0; i < ladder.Length; i++)
        {
            if (string.Equals(ladder[i], status, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>
    /// The whole judgement, in the order the questions have to be asked.
    ///
    /// <para>
    /// The speaker first, before anything is said about jobs. Then which job.
    /// Then whether the move is one this job can make. Asking them in any other
    /// order would decide whether a job may be touched using facts about a job
    /// the speaker has no right to.
    /// </para>
    ///
    /// <para>
    /// <b>What comes back is for us, not for them.</b> <c>no-such-job</c> and
    /// <c>not-your-job</c> are deliberately different here so the review queue
    /// can tell an operator which happened. A reply sent back to the group must
    /// not: telling an unknown sender that a number exists but is somebody
    /// else's confirms the number is real. That is the reply service's rule to
    /// keep, and it is written down there too.
    /// </para>
    /// </summary>
    /// <param name="group">The room, resolved by the caller.</param>
    /// <param name="status">The status the parser read, or empty.</param>
    /// <param name="candidates">Every row carrying the job number — not yet filtered by carrier.</param>
    public static LineDecision Decide(
        SpeakerGroup group,
        string? status,
        IReadOnlyList<JobCandidate> candidates)
    {
        /* ------------------------------------------------- the speaker */

        if (!group.Known)
            return new(Outcome.UnknownGroup, NoKeys, "", "",
                "ยังไม่ได้ผูกกลุ่ม LINE นี้กับผู้ให้บริการขนส่ง");

        if (!group.Active)
            return new(Outcome.GroupInactive, NoKeys, "", "",
                "กลุ่ม LINE นี้ถูกปิดการใช้งานแล้ว");

        // LineGroupType, not a copy of its four words. Every rule written
        // twice in this repo has ended up disagreeing with itself, and
        // ScorecardColumn already reaches into Data for the same reason: a
        // shared vocabulary is worth the reference.
        if (!string.Equals(group.GroupType, LineGroupType.Vendor, StringComparison.OrdinalIgnoreCase))
            return new(Outcome.GroupNotVendor, NoKeys, "", "",
                $"กลุ่มประเภท {group.GroupType} ไม่มีสิทธิ์อัปเดตสถานะงาน");

        /* ----------------------------------------------------- the job */

        var all = candidates ?? [];
        if (all.Count == 0)
            return new(Outcome.NoSuchJob, NoKeys, "", "",
                "ไม่พบเลขงานนี้ในระบบ");

        var mine = all.Where(one => SameCarrier(group.SupplierName, one.Carrier)).ToList();
        if (mine.Count == 0)
            return new(Outcome.NotYourJob, NoKeys, "", "",
                $"เลขงานนี้ไม่ได้อยู่กับ {group.SupplierName}");

        var keys = mine.Select(one => one.Key).ToList();
        if (mine.Count > 1)
            return new(Outcome.ManyJobs, keys, "", status ?? "",
                $"เลขงานนี้มี {mine.Count} รายการของ {group.SupplierName} — ต้องเลือกก่อน");

        // Asked after the job is found, not before: which job it is, and whether
        // the speaker may touch it, are true regardless of what they said about
        // it.
        return Move(mine[0], status);
    }

    /// <summary>
    /// Whether this one job can make this move — the second half of
    /// <see cref="Decide"/>, on its own.
    ///
    /// <para>
    /// Public because approving a message is not the same act as receiving one.
    /// When a number covers several of a haulier's rows the operator picks one,
    /// and by then the authority question is already settled — the key came out
    /// of the set this module filtered. What is not settled is whether that
    /// particular row can make the move, since the set-level answer said nothing
    /// about any single row's status. This is that question, asked without
    /// having to invent a speaker to ask it through.
    /// </para>
    /// </summary>
    public static LineDecision Move(JobCandidate job, string? status)
    {
        var one_key = new[] { job.Key };

        // A message that names a job and no status is still worth filing
        // against that job.
        if (string.IsNullOrWhiteSpace(status))
            return new(Outcome.NoStatus, one_key, job.Status, "",
                "อ่านเลขงานได้ แต่ไม่พบสถานะในข้อความ");

        if (string.Equals(job.Status, JobStatus.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(job.Status, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            return new(Outcome.JobClosed, one_key, job.Status, status,
                $"งานนี้ปิดแล้ว ({job.Status})");

        if (string.Equals(job.Status, JobStatus.Hold, StringComparison.OrdinalIgnoreCase))
            return new(Outcome.JobHeld, one_key, job.Status, status,
                "งานนี้ถูกพักไว้ ต้องให้เจ้าหน้าที่ปลดก่อน");

        var to = Rank(job.Category, status);
        if (to < 0)
            return new(Outcome.NotOnLadder, one_key, job.Status, status,
                $"งานประเภท {job.Category} ไม่มีขั้นตอน {status}");

        var from = Rank(job.Category, job.Status);
        if (from < 0)
            // One job in the register still carries "Truck Confirmed" from
            // before the status set was controlled. Its position is unknown, so
            // whether this is forward is unknown too.
            return new(Outcome.StatusUnknown, one_key, job.Status, status,
                $"สถานะปัจจุบันของงาน ({job.Status}) ไม่ใช่รหัสมาตรฐาน");

        if (to == from)
            return new(Outcome.AlreadyThere, one_key, job.Status, status,
                $"งานอยู่ที่ {status} อยู่แล้ว");

        if (to < from)
            return new(Outcome.Backwards, one_key, job.Status, status,
                $"ข้อความนี้จะย้อนสถานะจาก {job.Status} กลับไป {status}");

        return new(Outcome.Ok, one_key, job.Status, status, "");
    }
}
