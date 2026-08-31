namespace Scmos.Api.Rules;

/// <summary>
/// The twelve things worth interrupting somebody about.
///
/// A fixed list, because the value of an alert is inversely proportional to how
/// many kinds there are. Each one names what raises it and what a person is
/// meant to do about it — an alert nobody can act on is noise wearing a warning
/// colour, and the fastest way to make a team ignore a real alert is to sit it
/// next to nine they cannot fix.
/// </summary>
public enum AlertKind
{
    SupplierNotConfirmed,
    BookingMissingData,
    PreRunNotConfirmed,
    TruckDelay,
    ECardMismatch,
    DocumentUnclear,
    PodMissing,
    SupplierDocumentExpiring,
    DriverTrainingExpiring,
    DriverTrainingExpired,
    AuditExpiring,
    CarParOverdue,
    CapacityShortage,
    KpiBelowTarget,
    ActingForColleague,
    CoverArrangedForYou,
}

/// <summary>Critical needs somebody now; Warning needs somebody today; Information is context.</summary>
public enum AlertLevel { Information, Warning, Critical }

public record AlertDefinition(
    AlertKind Kind,
    string English,
    string Thai,
    AlertLevel Level,
    /// <summary>What a person is meant to do. Written here so every alert has one.</summary>
    string Action,
    /// <summary>Which screen answers it.</summary>
    string Screen);

public static class Notifications
{
    /// <summary>
    /// A KPI below this is worth raising. The team's own targets, not a
    /// statistical threshold — 95% on-time is what was agreed, so 94% is the
    /// thing to talk about even though the difference is one shipment.
    /// </summary>
    public const int OnTimeTarget = 95;

    /// <summary>How near an expiry has to be before it is worth saying so.</summary>
    public const int ExpiryWarningDays = 60;

    /// <summary>
    /// A booking with no carrier this close to its loading date is not "pending",
    /// it is a problem. Two days is what the escalation ladder needs to reach a
    /// third carrier.
    /// </summary>
    public const int CarrierWarningDays = 2;

    // Booking, Pre-Run and Document Verification left the menu on 2026-08-31,
    // and five of these were still pointing at them. An alert that opens a
    // screen the team has stopped using is a dead end wearing a warning colour,
    // so each one now names the screen where the work is actually done.
    //
    // Held to that by tests/alertTargets.test.mjs: every screen named here has
    // to be somewhere a person can actually get to. Nothing tied these strings
    // to the menu before, which is how three screens could leave it and take
    // seven alerts with them without anybody noticing.
    public static readonly AlertDefinition[] All =
    [
        new(AlertKind.SupplierNotConfirmed, "Supplier not confirmed", "ผู้ขนส่งยังไม่ยืนยัน",
            AlertLevel.Critical, "ติดต่อผู้ขนส่ง หรือส่งต่อรายถัดไปตามลำดับ", "myjob"),

        new(AlertKind.BookingMissingData, "Booking missing data", "ข้อมูลจองรถไม่ครบ",
            AlertLevel.Warning, "เติมทะเบียนรถ คนขับ และเบอร์ติดต่อให้ครบก่อนวันงาน", "myjob"),

        // The one on this list that is chased rather than typed: somebody has
        // to phone the carrier. That is following a shipment, so it goes where
        // shipments are followed — and it is the same question the supervisor's
        // risk board asks under "ยังไม่มีรถ/คนขับ".
        new(AlertKind.PreRunNotConfirmed, "Pre-run not confirmed", "ยังไม่ยืนยันก่อนออกงาน",
            AlertLevel.Critical, "โทรตามผู้ขนส่งให้ยืนยันรถและคนขับ", "monitoring"),

        new(AlertKind.TruckDelay, "Truck delay", "รถล่าช้า",
            AlertLevel.Critical, "บันทึกสาเหตุ แจ้งลูกค้า และประเมินเวลาที่จะถึงใหม่", "monitoring"),

        // Whatever it is named, what it counts is a container number that fails
        // its own check digit — see ContainerWillNotMatch below. The fix is to
        // retype it on the job row.
        new(AlertKind.ECardMismatch, "E-Card mismatch", "E-Card ไม่ตรงกับงาน",
            AlertLevel.Critical, "ให้ CS ตรวจสอบ E-Card เทียบกับ booking ก่อนรถเข้าท่า", "myjob"),

        // The only one of the five that is about a document rather than a job:
        // it carries no job key at all, so My Job would open a list with nothing
        // to look at. It goes where its sibling POD missing already goes.
        new(AlertKind.DocumentUnclear, "Document unclear", "เอกสารไม่ชัดเจน",
            AlertLevel.Warning, "ขอไฟล์ใหม่จากผู้ส่ง", "documents"),

        new(AlertKind.PodMissing, "POD missing", "ยังไม่มีใบรับของ",
            AlertLevel.Warning, "ขอ POD จากผู้ขนส่งก่อนวางบิล", "documents"),

        new(AlertKind.SupplierDocumentExpiring, "Supplier document expiring", "เอกสารผู้ขนส่งใกล้หมดอายุ",
            AlertLevel.Warning, "ขอเอกสารฉบับใหม่ก่อนหมดอายุ", "subcontractors"),

        // Sixty days is the agreed notice: long enough to book a course and
        // still have the driver on the roster when it runs.
        new(AlertKind.DriverTrainingExpiring, "Driver training expiring",
            "การอบรมคนขับใกล้หมดอายุ",
            AlertLevel.Warning, "จัดอบรมต่ออายุก่อนหมดอายุ", "training"),

        // Already lapsed, which is a different problem: that driver cannot be
        // put on the customer's work at all until it is renewed.
        new(AlertKind.DriverTrainingExpired, "Driver training expired",
            "การอบรมคนขับหมดอายุแล้ว",
            AlertLevel.Critical, "คนขับรายนี้รับงานของลูกค้าที่กำหนดไม่ได้", "training"),

        new(AlertKind.AuditExpiring, "Audit expiring", "ผลตรวจประเมินใกล้หมดอายุ",
            AlertLevel.Information, "นัดตรวจประเมินรอบใหม่", "evaluation"),

        // "carpar" until 2026-08-31. That id still resolves, so the alert worked
        // — but it names a door that is no longer in the menu, and the rules we
        // control should name the live entry rather than lean on an alias kept
        // for saved links.
        new(AlertKind.CarParOverdue, "CAR/PAR overdue", "CAR/PAR เกินกำหนด",
            AlertLevel.Critical, "ติดตามผู้รับผิดชอบ หรือขยายกำหนดพร้อมเหตุผล", "incident"),

        new(AlertKind.CapacityShortage, "Capacity shortage", "กำลังรถไม่พอ",
            AlertLevel.Warning, "หาผู้ขนส่งรายอื่น หรือเลื่อนงานที่ยืดหยุ่นได้", "capacity"),

        new(AlertKind.KpiBelowTarget, "KPI below target", "KPI ต่ำกว่าเป้า",
            AlertLevel.Warning, "ดูว่าผู้ขนส่งรายใดหรือเส้นทางใดฉุดค่าเฉลี่ย", "kpi"),

        // These two said "workspace", which was worse than a disused screen:
        // Workspace is a menu heading with nothing rendered behind it, so the
        // alert opened a blank page. The jobs being covered are on My Job.
        //
        // Not a problem to fix — a fact somebody needs before they start
        // editing. Information, because telling them it is urgent would be
        // untrue and would train them to ignore the level.
        new(AlertKind.ActingForColleague, "Covering for a colleague", "คุณกำลังถืองานของเพื่อนร่วมงาน",
            AlertLevel.Information, "งานของเขาจะแก้ได้จนถึงวันสิ้นสุด และยังบันทึกชื่อคุณเป็นผู้แก้", "myjob"),

        // Somebody arranged cover over your work without you doing it. Told,
        // not discovered: you should hear it from the system before you hear it
        // from the edit history.
        new(AlertKind.CoverArrangedForYou, "Cover arranged for your jobs", "มีคนมอบสิทธิ์งานของคุณให้ผู้อื่น",
            AlertLevel.Information, "ถ้าไม่ถูกต้อง ยกเลิกได้ที่หน้ามอบสิทธิ์", "myjob"),
    ];

    public static AlertDefinition Of(AlertKind kind) => All.First(alert => alert.Kind == kind);

    /* ------------------------------------------------------------- tests */

    /// <summary>
    /// A job that still has no carrier. The most expensive thing on this list:
    /// every other alert is about a job that will run.
    /// </summary>
    public static bool NeedsCarrier(JobRecord job) =>
        !JobRules.IsDone(job.Status) && job.Trucker.Trim().Length == 0;

    /// <summary>
    /// A carrier is named but nothing else is: no plate, no driver. The job will
    /// run and nobody knows what is turning up.
    /// </summary>
    public static bool MissingBookingData(JobRecord job) =>
        !JobRules.IsDone(job.Status)
        && job.Trucker.Trim().Length > 0
        && (job.Licence.Trim().Length == 0 || job.Driver.Trim().Length == 0);

    /// <summary>
    /// A container number that does not match the standard.
    ///
    /// This is the E-Card mismatch in the only form the register can currently
    /// see it: the gate reads the container off the card, and a container number
    /// that fails its own check digit will not match whatever the card says.
    /// A real card-to-booking comparison needs the cards, which are not in the
    /// system yet — so this is named for what it actually checks.
    /// </summary>
    public static bool ContainerWillNotMatch(JobRecord job) =>
        job.Container.Trim().Length > 0 && !Formats.IsContainer(Formats.Clean(job.Container));

    /// <summary>Days between a job's plan date and today; null when the date will not parse.</summary>
    public static int? DaysAway(JobRecord job, int today)
    {
        var planned = Formats.DateNumber(job.Date);
        if (planned == 0 || today == 0) return null;
        // Both are YYYYMMDD, so this is a calendar-day difference only when the
        // dates are close. That is all it is used for — near-term urgency.
        var a = ToDate(planned);
        var b = ToDate(today);
        return a is null || b is null ? null : (int)(a.Value - b.Value).TotalDays;
    }

    private static DateTime? ToDate(int number)
    {
        var year = number / 10000;
        var month = number / 100 % 100;
        var day = number % 100;
        if (year is < 1900 or > 2999 || month is < 1 or > 12 || day is < 1 or > 31) return null;
        try { return new DateTime(year, month, day); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
