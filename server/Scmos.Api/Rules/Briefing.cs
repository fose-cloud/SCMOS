namespace Scmos.Api.Rules;

/// <summary>
/// What the dashboard would say if it could talk.
///
/// <para>
/// The front page shows ten figures and says nothing. Half of them are zero or
/// "cannot be measured", and a person opening it has to work out for themselves
/// which of the other half matters this morning. This turns the same numbers
/// into a short list of sentences, worst first, each with the figure it came
/// from and the screen that answers it.
/// </para>
/// <para>
/// <b>Nothing here is generated.</b> Every finding is a fact that was already
/// counted somewhere else — the monitor's risk list, the problem list, the
/// delay register — restated. There is no wording that appears without a number
/// behind it, and when there is nothing wrong the list is empty rather than
/// padded, because a briefing that always finds five things to say is one
/// nobody reads by the second week.
/// </para>
/// <para>
/// Pure, so <c>--check-briefing</c> can run every case without a register.
/// </para>
/// </summary>
public static class Briefing
{
    /// <summary>How much of somebody's attention a finding is asking for.</summary>
    public enum Urgency
    {
        /// <summary>Something has already gone wrong and is still going wrong.</summary>
        Now,

        /// <summary>Something will go wrong today unless somebody moves.</summary>
        Soon,

        /// <summary>A pattern worth knowing, which nobody has to act on this hour.</summary>
        Watch,

        /// <summary>
        /// Not about the work — about how much of it can be judged at all.
        ///
        /// Last by rank and never dropped, because it qualifies every finding
        /// above it. A morning where two thirds of the register cannot be
        /// measured is a morning where "nothing is wrong" means less than it
        /// looks.
        /// </summary>
        Records,
    }

    /// <param name="Kind">Machine name, so the screen can colour and test it.</param>
    /// <param name="Headline">The finding, in the words the team uses.</param>
    /// <param name="Detail">What is behind it — the base, the name, the second number.</param>
    /// <param name="Count">The figure the headline is about.</param>
    /// <param name="Screen">Where somebody goes to do something about it.</param>
    public readonly record struct Finding(
        Urgency Urgency, string Kind, string Headline, string Detail, int Count, string Screen);

    /// <summary>
    /// Everything the briefing reads, counted elsewhere and handed over.
    ///
    /// A record of numbers rather than the board itself, so this file cannot
    /// reach past what it was given and start counting things of its own.
    /// </summary>
    /// <param name="Live">Jobs neither finished nor cancelled.</param>
    /// <param name="Overdue">
    /// Past its plan date with nothing recorded as having arrived.
    /// </param>
    /// <param name="MissingBeforeRun">
    /// The rest of the monitor's risk list: not yet due, but short of an owner,
    /// a carrier or a lorry.
    ///
    /// Deliberately the remainder rather than the whole list. Told both, the
    /// briefing said "123 overdue" and "123 to deal with today" one after the
    /// other — the same jobs counted twice, which is exactly the padding this
    /// file exists to avoid, and it took running it against a real register to
    /// see.
    /// </param>
    /// <param name="WithProblem">Live jobs with at least one thing wrong.</param>
    /// <param name="ArrivedLate">Measured: past plan by more than the allowed minutes.</param>
    /// <param name="LateMinutes">What "late" means here, so the sentence can say it.</param>
    /// <param name="Incidents">Written into the INCIDENT REPORT column by an operator.</param>
    /// <param name="OpenDelays">Delays recorded, categorised, and never closed.</param>
    /// <param name="Unmeasurable">Live jobs whose lateness cannot be worked out at all.</param>
    /// <param name="BusiestOwner">Whoever is carrying the most flagged work, or empty.</param>
    /// <param name="BusiestOwnerFlagged">How many of the risk list are theirs.</param>
    /// <param name="TopDelayParty">Who the month's recorded delays were put down to.</param>
    /// <param name="TopDelayCases">How many cases that was.</param>
    /// <param name="ShowTeam">
    /// Whether this reader may see other people's workloads. A briefing is still
    /// a view of the register, and naming who is drowning is team information.
    /// </param>
    public readonly record struct Facts(
        int Live, int Overdue, int MissingBeforeRun, int WithProblem, int ArrivedLate, int LateMinutes,
        int Incidents, int OpenDelays, int Unmeasurable,
        string BusiestOwner, int BusiestOwnerFlagged,
        string TopDelayParty, int TopDelayCases,
        bool ShowTeam);

    /// <summary>
    /// Where the records gap stops being a footnote and becomes the finding.
    ///
    /// A fifth. Below that it is the ordinary untidiness of a working register;
    /// above it, every rate and percentage on the page is drawn from a minority
    /// of the work and the reader has to be told before they quote one.
    /// </summary>
    public const int UnmeasurableShare = 5;

    /// <summary>
    /// The morning, read.
    ///
    /// Worst first, and short. Nothing is added to reach a length: a quiet
    /// register returns an empty list and the screen says so in one line, which
    /// is the honest thing for it to say and the only way the list keeps meaning
    /// something on the days it is long.
    /// </summary>
    public static IReadOnlyList<Finding> Read(Facts facts)
    {
        var found = new List<Finding>();

        // Already wrong, and nobody has closed it.
        if (facts.Incidents > 0)
            found.Add(new Finding(Urgency.Now, "incident",
                $"มีเหตุผิดปกติที่บันทึกไว้ {facts.Incidents} งาน",
                "พนักงานพิมพ์ไว้เองในช่อง Incident Report", facts.Incidents, "monitoring"));

        if (facts.OpenDelays > 0)
            found.Add(new Finding(Urgency.Now, "openDelay",
                $"ความล่าช้า {facts.OpenDelays} เรื่องยังไม่ปิด",
                "บันทึกไว้พร้อมสาเหตุและผู้รับผิดชอบ แต่ยังไม่มีใครปิดเรื่อง",
                facts.OpenDelays, "monitoring"));

        // Whose backlog it is, said once, on whichever of the next two appears
        // first. Repeating a person's name down the list would read as a
        // complaint about them rather than a fact about the work.
        var carrying = facts.ShowTeam && facts.BusiestOwner.Length > 0 && facts.BusiestOwnerFlagged > 0
            ? $" · {facts.BusiestOwnerFlagged} อยู่กับ {facts.BusiestOwner} คนเดียว"
            : "";

        if (facts.Overdue > 0)
        {
            found.Add(new Finding(Urgency.Now, "overdue",
                $"เลยกำหนดแล้ว {facts.Overdue} งาน",
                "ยังไม่มีบันทึกว่ารถถึง และวันตามแผนผ่านไปแล้ว" + carrying,
                facts.Overdue, "monitoring"));
            carrying = "";
        }

        // The rest of the risk list — not yet due, and short of something.
        if (facts.MissingBeforeRun > 0)
            found.Add(new Finding(Urgency.Soon, "today",
                $"ต้องจัดการก่อนถึงวันวิ่ง {facts.MissingBeforeRun} งาน",
                "ยังขาดเจ้าของงาน ผู้ขนส่ง หรือรถ" + carrying,
                facts.MissingBeforeRun, "monitoring"));

        // Worth knowing, not worth interrupting for.
        if (facts.ArrivedLate > 0)
            found.Add(new Finding(Urgency.Watch, "late",
                $"ถึงช้ากว่าแผน {facts.ArrivedLate} เที่ยว",
                $"วัดจากแผนเทียบเวลาถึงจริง เกิน {facts.LateMinutes} นาที", facts.ArrivedLate, "monitoring"));

        if (facts.TopDelayCases > 0 && facts.TopDelayParty.Length > 0)
            found.Add(new Finding(Urgency.Watch, "blame",
                $"ความล่าช้า 30 วันที่ผ่านมา ลงที่ {facts.TopDelayParty} มากที่สุด",
                $"{facts.TopDelayCases} ครั้ง — จากสาเหตุที่ผู้ปฏิบัติงานบันทึกเอง ไม่ได้คำนวณใหม่",
                facts.TopDelayCases, "monitoring"));

        // Last, and only when it is big enough to change how the rest is read.
        if (facts.Live > 0 && facts.Unmeasurable * UnmeasurableShare >= facts.Live)
            found.Add(new Finding(Urgency.Records, "unmeasurable",
                $"{facts.Unmeasurable} จาก {facts.Live} งานยังบอกไม่ได้ว่าตรงเวลาหรือไม่",
                "ไม่มีเวลาตามแผนหรือเวลาถึง — ทุกเปอร์เซ็นต์ในหน้านี้คิดจากส่วนที่วัดได้เท่านั้น",
                facts.Unmeasurable, "myjob"));

        return found;
    }

    /// <summary>
    /// What to say when the list is empty.
    ///
    /// Two different sentences, because "nothing is wrong" and "nothing can be
    /// seen to be wrong" are not the same claim and the register knows which one
    /// it is entitled to make.
    /// </summary>
    public static string Quiet(Facts facts) =>
        facts.Live == 0
            ? "ไม่มีงานที่ยังไม่จบในขอบเขตนี้"
            : facts.Unmeasurable * UnmeasurableShare >= facts.Live
                ? $"ยังไม่พบปัญหาที่ต้องจัดการ — แต่ {facts.Unmeasurable} จาก {facts.Live} งานยังวัดไม่ได้"
                : $"ยังไม่พบปัญหาที่ต้องจัดการจากงานที่ยังไม่จบ {facts.Live} งาน";
}
