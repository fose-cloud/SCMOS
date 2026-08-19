using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Whether a driver may run a customer's work today.
///
/// Everything here is derived from two things: what the customer requires, and
/// the most recent certificate the driver holds for each of those courses.
/// Nothing is stored as a status and nothing runs on a schedule — a certificate
/// that lapses overnight reads as expired the next morning because the question
/// is asked against today's date, not because a job woke up and rewrote a
/// column. A nightly task that failed quietly would otherwise leave lapsed
/// training showing as valid for as long as nobody checked the task.
/// </summary>
public class TrainingService(ScmosDbContext db)
{
    public record CourseState(
        int CourseId, string Code, string Name, bool Mandatory,
        string TrainingDate, string ExpiryDate, string CertificateNo, string Provider,
        int? DaysLeft, string Status, long? RecordId, long? DocumentId);

    public record DriverProfile(
        int DriverId, string Name, string DriverIdNo, string Phone,
        int? SupplierId, string SupplierName, bool Active,
        /// <summary>
        /// The photograph, for a screen that shows who the certificate belongs
        /// to. Served through the document endpoint like every other file, so
        /// the blob stays private and the same permission answers for it.
        /// </summary>
        long? PhotoDocumentId,
        IReadOnlyList<CourseState> Courses);

    /// <param name="Blocking">
    /// Mandatory courses that are expired or were never taken. Non-empty means
    /// this driver cannot be put on this customer's work.
    /// </param>
    public record Eligibility(
        int DriverId, string DriverName, string Customer, bool Eligible,
        IReadOnlyList<CourseState> Blocking, IReadOnlyList<CourseState> Warning,
        double? Compliance);

    public record Summary(
        int Drivers, int Valid, int Attention, int ExpiringSoon, int Expired, int Missing,
        double? Compliance);

    public record Result(bool Ok, string Message, long Id = 0);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /* --------------------------------------------------------------- reads */

    /// <summary>
    /// The latest record per course for one driver, voided rows ignored.
    ///
    /// "Latest" is by training date, not by row id: paperwork is often keyed
    /// weeks after the course, and the row entered last is not reliably the
    /// training taken last.
    /// </summary>
    private async Task<Dictionary<int, DriverTraining>> LatestForAsync(
        int driverId, CancellationToken token)
    {
        var rows = await db.DriverTrainings.AsNoTracking()
            .Where(record => record.DriverId == driverId && !record.Voided)
            .ToListAsync(token);

        return rows
            .GroupBy(record => record.CourseId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(record => TrainingRules.ParseDate(record.TrainingDate) ?? DateOnly.MinValue)
                    .ThenByDescending(record => record.Id)
                    .First());
    }

    /// <summary>
    /// What a customer requires. An empty customer asks for every active course
    /// the catalogue holds, which is what the driver's own profile shows.
    /// </summary>
    private async Task<List<(TrainingCourse Course, bool Mandatory)>> RequiredAsync(
        string customer, CancellationToken token)
    {
        var courses = await db.TrainingCourses.AsNoTracking()
            .Where(course => course.Active).ToListAsync(token);

        if (customer.Trim().Length == 0)
            return courses.Select(course => (course, true)).ToList();

        var requirements = await db.CustomerTrainingRequirements.AsNoTracking()
            .Where(requirement => requirement.Customer == customer).ToListAsync(token);

        return requirements
            .Join(courses, requirement => requirement.CourseId, course => course.Id,
                (requirement, course) => (course, requirement.Mandatory))
            .ToList();
    }

    public async Task<DriverProfile?> ProfileAsync(int driverId, string customer, CancellationToken token)
    {
        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId, token);
        if (driver is null) return null;

        var supplierName = driver.SupplierId is { } id
            ? (await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, token))?.Name ?? ""
            : "";

        var latest = await LatestForAsync(driverId, token);
        var required = await RequiredAsync(customer, token);

        var courses = required
            .Select(item => Describe(item.Course, item.Mandatory, latest.GetValueOrDefault(item.Course.Id)))
            .OrderBy(state => Array.IndexOf(TrainingRules.All, state.Status))
            .ToList();

        return new DriverProfile(driver.Id, driver.Name, driver.DriverIdNo, driver.Phone,
            driver.SupplierId, supplierName, driver.Active, driver.PhotoDocumentId, courses);
    }

    private static CourseState Describe(TrainingCourse course, bool mandatory, DriverTraining? record)
    {
        if (record is null)
        {
            return new CourseState(course.Id, course.Code, course.Name, mandatory,
                "", "", "", "", null, TrainingRules.Missing, null, null);
        }

        // A date that cannot be read is not the same as expired, and is not
        // shown as either — MISSING is the honest answer to "we have a row and
        // cannot tell you when it lapses".
        var status = TrainingRules.Status(record.ExpiryDate, Today) ?? TrainingRules.Missing;

        return new CourseState(course.Id, course.Code, course.Name, mandatory,
            record.TrainingDate, record.ExpiryDate, record.CertificateNo, record.Provider,
            TrainingRules.DaysLeft(record.ExpiryDate, Today), status, record.Id, record.DocumentId);
    }

    /// <summary>
    /// Whether this driver may run this customer's work, and what is stopping
    /// them if not.
    ///
    /// Only mandatory requirements block. A course the customer asks for but
    /// does not enforce appears as a warning, because refusing a driver over
    /// something the customer would wave through is a refusal nobody can act on.
    /// </summary>
    public async Task<Eligibility?> CheckAsync(int driverId, string customer, CancellationToken token)
    {
        var profile = await ProfileAsync(driverId, customer, token);
        if (profile is null) return null;

        var blocking = profile.Courses
            .Where(state => state.Mandatory && !TrainingRules.IsEligible(state.Status))
            .ToList();

        var warning = profile.Courses
            .Where(state => !blocking.Contains(state)
                            && (state.Status == TrainingRules.ExpiringSoon
                                || (!state.Mandatory && !TrainingRules.IsEligible(state.Status))))
            .ToList();

        var mandatory = profile.Courses.Count(state => state.Mandatory);
        var valid = profile.Courses.Count(state => state.Mandatory && TrainingRules.IsEligible(state.Status));

        return new Eligibility(profile.DriverId, profile.Name, customer,
            blocking.Count == 0, blocking, warning,
            TrainingRules.Compliance(valid, mandatory));
    }

    /// <summary>
    /// The dashboard's figures, counted across every active driver.
    ///
    /// A driver is counted once per required course, not once overall — the
    /// question the tiles answer is "how many pieces of training need attention",
    /// and a driver with three lapsed certificates is three pieces of work.
    /// </summary>
    public async Task<Summary> SummaryAsync(string customer, CancellationToken token)
    {
        var drivers = await db.Drivers.AsNoTracking().Where(d => d.Active).ToListAsync(token);
        var required = await RequiredAsync(customer, token);

        var counts = TrainingRules.All.ToDictionary(status => status, _ => 0);
        var valid = 0;
        var total = 0;

        foreach (var driver in drivers)
        {
            var latest = await LatestForAsync(driver.Id, token);
            foreach (var (course, mandatory) in required)
            {
                var state = Describe(course, mandatory, latest.GetValueOrDefault(course.Id));
                counts[state.Status] = counts.GetValueOrDefault(state.Status) + 1;
                if (!mandatory) continue;
                total++;
                if (TrainingRules.IsEligible(state.Status)) valid++;
            }
        }

        return new Summary(drivers.Count,
            counts[TrainingRules.Valid], counts[TrainingRules.Attention],
            counts[TrainingRules.ExpiringSoon], counts[TrainingRules.Expired],
            counts[TrainingRules.Missing],
            TrainingRules.Compliance(valid, total));
    }

    /// <summary>
    /// The answer the assignment screen acts on.
    ///
    /// Refused by default when a mandatory course has lapsed or was never
    /// taken. The refusal is not the end of it: the team that arranges carriers
    /// can go ahead anyway, because a substitute driver at six in the morning is
    /// a real situation and a system that simply says no gets worked around
    /// outside the system, where nobody can see it. What it cannot be is quiet
    /// — going ahead takes a reason, and the reason is recorded against the job
    /// with the name of whoever gave it.
    /// </summary>
    public record Gate(
        bool Allowed, bool Blocked, bool MayOverride, string Message,
        IReadOnlyList<CourseState> Blocking, IReadOnlyList<CourseState> Warning);

    public async Task<Gate?> GateAsync(int driverId, string customer, AppUser by, CancellationToken token)
    {
        var check = await CheckAsync(driverId, customer, token);
        if (check is null) return null;

        var mayOverride = by.Can(Capability.ManageTraining);

        if (check.Eligible)
        {
            var soon = check.Warning.Count > 0;
            return new Gate(true, false, mayOverride,
                soon
                    ? $"อบรมครบ แต่มี {check.Warning.Count} หลักสูตรใกล้หมดอายุ"
                    : "อบรมครบตามที่ลูกค้ากำหนด",
                [], check.Warning);
        }

        var names = string.Join(", ", check.Blocking.Select(state => state.Name));
        return new Gate(false, true, mayOverride,
            $"Training Expired — Driver is not eligible for this customer · ติดที่ {names}",
            check.Blocking, check.Warning);
    }

    /* -------------------------------------------------------------- writes */

    /// <summary>
    /// Records a certificate. Renewal writes a new row; nothing is overwritten.
    /// </summary>
    public async Task<Result> RecordAsync(DriverTraining entry, AppUser by, CancellationToken token)
    {
        if (!await db.Drivers.AnyAsync(d => d.Id == entry.DriverId, token))
            return new Result(false, "ไม่พบคนขับรายนี้");
        if (!await db.TrainingCourses.AnyAsync(c => c.Id == entry.CourseId, token))
            return new Result(false, "ไม่พบหลักสูตรนี้");

        var trained = TrainingRules.ParseDate(entry.TrainingDate);
        if (trained is null) return new Result(false, "วันที่อบรมไม่ถูกต้อง (วว/ดด/ปปปป)");

        // An expiry that was not given is worked out from the course's validity
        // rather than left blank — a record with no expiry can never lapse, and
        // would sit in the register reading as permanently valid.
        if (TrainingRules.ParseDate(entry.ExpiryDate) is null)
        {
            var course = await db.TrainingCourses.AsNoTracking()
                .FirstAsync(c => c.Id == entry.CourseId, token);
            var months = await db.CustomerTrainingRequirements.AsNoTracking()
                .Where(r => r.Customer == entry.Customer && r.CourseId == entry.CourseId)
                .Select(r => r.ValidMonths).FirstOrDefaultAsync(token) ?? course.ValidMonths;
            entry.ExpiryDate = TrainingRules.Write(trained.Value.AddMonths(Math.Max(1, months)));
        }

        if (TrainingRules.ParseDate(entry.ExpiryDate) is { } expiry && expiry <= trained.Value)
            return new Result(false, "วันหมดอายุต้องอยู่หลังวันที่อบรม");

        entry.CreatedBy = by.Signature;
        entry.CreatedAt = DateTimeOffset.UtcNow;
        db.DriverTrainings.Add(entry);
        await db.SaveChangesAsync(token);

        return new Result(true, "บันทึกการอบรมแล้ว", entry.Id);
    }

    /// <summary>
    /// Marks a record as keyed in error. Not a delete — the row stays, stops
    /// counting, and carries the reason, because a certificate that was entered
    /// and withdrawn is itself something an audit asks about.
    /// </summary>
    public async Task<Result> VoidAsync(long id, string reason, AppUser by, CancellationToken token)
    {
        if (reason.Trim().Length == 0) return new Result(false, "ต้องระบุเหตุผล");

        var record = await db.DriverTrainings.FirstOrDefaultAsync(r => r.Id == id, token);
        if (record is null) return new Result(false, "ไม่พบรายการนี้");
        if (record.Voided) return new Result(false, "รายการนี้ถูกยกเลิกไปแล้ว");

        record.Voided = true;
        record.VoidReason = reason.Trim();
        record.VoidedBy = by.Signature;
        await db.SaveChangesAsync(token);

        return new Result(true, "ยกเลิกรายการแล้ว — ประวัติยังอยู่", id);
    }
}
