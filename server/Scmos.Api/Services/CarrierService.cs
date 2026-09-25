using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// What a carrier can see, and what they can do about it.
///
/// This is a boundary, not a screen. Everyone else in SCMOS works for one
/// company and may see the whole plan; a subcontractor works for a different
/// company, and the register they would otherwise be handed contains their
/// competitors' assignments, every customer's name, and the rates those
/// customers pay. So a carrier account is scoped to exactly one supplier and
/// sees exactly three things: work offered to them, work they accepted, and
/// nothing else.
///
/// The scoping is done here rather than in each endpoint on purpose. The last
/// time a permission check lived in the endpoints, two of them were written
/// without one and a read-only account wrote to the register for a week.
/// </summary>
public class CarrierService(ScmosDbContext db, JobsRepository jobs, JobRegisterCache register, CarrierTenantContext tenants,
    ILogger<CarrierService> log, CarrierWebhookQueue webhooks)
{
    /// <param name="Category">IMPORT · EXPORT · DELIVERY — which ladder the status is on.</param>
    /// <param name="Booking">The booking, on an import or export.</param>
    /// <param name="PlanTime">The plan clock, beside <paramref name="Date"/>.</param>
    /// <param name="ArrDate">The arrival stamp, when the truck has been reported; what on-time delivery reads.</param>
    /// <param name="Seal">The seal, on an export once loaded.</param>
    public record CarrierJob(
        string Key, string JobCode, string Customer, string Destination, string Type,
        string CyYard, string Weight, string Container, string Date, string PickupPlan,
        string Status, long? RequestId, int? QuotedPrice, DateTimeOffset? RequestedAt,
        string Licence, string Driver, string Contact,
        string Category = "", string Booking = "", string PlanTime = "", string Plant = "", string ReturnLoc = "",
        string ArrDate = "", string ArrTime = "", string Seal = "", DateTimeOffset? RespondedAt = null,
        string AssignmentOutcome = "", bool OperationalAvailable = false);

    public record Portal(
        int SupplierId, string SupplierName,
        IReadOnlyList<CarrierJob> Offered,
        IReadOnlyList<CarrierJob> Accepted,
        IReadOnlyList<CarrierJob> Schedule);

    /// <param name="Before">
    /// What the job held before the change, so the caller can record it. The
    /// audit trail exists to answer "what was it before"; a row that only
    /// carries the new value answers half the question, and the half it drops
    /// is the one somebody needs when a plate turns out to be wrong.
    /// </param>
    /// <param name="Code">Why it was refused, for a caller that answers in codes rather than sentences — one of <see cref="ResultCode"/>; empty when Ok.</param>
    /// <param name="Conflicts">Cells the register already holds with a different value, when the refusal is <see cref="ResultCode.Conflict"/>.</param>
    /// <param name="Written">The cells the call wrote, by name.</param>
    /// <param name="Skipped">Cells sent with the value the register already held — nothing to write, nothing wrong.</param>
    /// <param name="Previous">What each written cell held before, as written — a placeholder such as "-" counts as empty for the write and is still recorded here.</param>
    public record Result(bool Ok, string Message, string Before = "", string Code = "",
        IReadOnlyList<Conflict>? Conflicts = null, IReadOnlyDictionary<string, string>? Written = null,
        IReadOnlyList<string>? Skipped = null, IReadOnlyDictionary<string, string>? Previous = null,
        bool Replayed = false, long? AssignmentId = null);

    /// <summary>A cell the register holds with one value while the caller sent another.</summary>
    public record Conflict(string Field, string Current, string Sent);

    public static class ResultCode
    {
        public const string NoCompany = "no-company";
        public const string Invalid = "invalid";
        /// <summary>No request waiting for this carrier's answer on that job.</summary>
        public const string NotOffered = "not-offered";
        /// <summary>The register does not name this carrier on that job.</summary>
        public const string NotHeld = "not-held";
        public const string Closed = "closed";
        public const string Conflict = "conflict";
        public const string Failed = "failed";
    }

    /// <summary>The cells the truck's details live in, in the order the messages name them.</summary>
    private static readonly (string Name, string Label)[] TruckCells =
        [("licence", "ทะเบียน"), ("driver", "คนขับ"), ("contact", "เบอร์"), ("container", "เลขตู้"), ("seal", "เลขซีล")];

    /// <summary>
    /// The supplier this person speaks for, or null when they speak for nobody.
    ///
    /// Null is the safe answer and is returned for every account that is not a
    /// carrier account — including an administrator, who has no business
    /// appearing to be one.
    /// </summary>
    public async Task<Supplier?> CompanyOfAsync(AppUser user, CancellationToken token)
    {
        var tenant = await tenants.ResolveAsync(user, token);
        return tenant is null
            ? null
            : await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == tenant.SupplierId, token);
    }

    /// <summary>
    /// Every name this carrier trades under.
    ///
    /// The register writes carriers as the plan workbook spells them, which is
    /// not always the supplier's registered name — that is what the alias table
    /// is for. Matching on the registered name alone would hide a carrier's own
    /// jobs from them and look like the boundary was working.
    /// </summary>
    private async Task<HashSet<string>> NamesOfAsync(Supplier company, CancellationToken token)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            company.Name.Trim(), company.Code.Trim(),
        };
        var aliases = await db.SupplierAliases.AsNoTracking()
            .Where(a => a.SupplierId == company.Id).Select(a => a.Alias).ToListAsync(token);
        foreach (var alias in aliases) names.Add(alias.Trim());
        names.RemoveWhere(name => name.Length == 0);
        return names;
    }

    public async Task<Portal?> ReadAsync(AppUser user, CancellationToken token)
    {
        var company = await CompanyOfAsync(user, token);
        if (company is null) return null;
        return await ReadForAsync(company, token);
    }

    /// <summary>
    /// The portal for a supplier the caller has already settled — a person's
    /// account through <see cref="CompanyOfAsync"/>, a TMS's key through
    /// <c>CarrierApiAuth</c>. One reading of the register for both doors, so
    /// what a carrier's TMS is shown is exactly what its person is shown.
    /// </summary>
    public async Task<Portal> ReadForAsync(Supplier company, CancellationToken token)
    {
        var names = await NamesOfAsync(company, token);

        // Work offered but not yet answered. The request is the invitation, and
        // until it is answered the carrier has been told about the job without
        // being given it.
        var spellings = names.ToList();
        var mine = await db.SupplierRequests.AsNoTracking()
            .Where(request => request.SupplierId == company.Id
                || (request.SupplierId == null && spellings.Contains(request.Carrier)))
            .ToListAsync(token);
        var active = mine.Where(request => CarrierAssignment.IsActive(request.Outcome)).ToList();
        var assignedKeys = (await db.SupplierRequests.AsNoTracking()
                .Select(request => request.JobKey).Distinct().ToListAsync(token))
            .ToHashSet(StringComparer.Ordinal);

        // The register arrives as `{"jobs":[…]}` — the envelope the workspace
        // reads — not as a bare array.
        var (json, _) = await jobs.LoadAsync(token);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("jobs", out var all)
            || all.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            log.LogError("The register did not come back in the expected shape; showing this carrier nothing.");
            return new Portal(company.Id, company.Name, [], [], []);
        }

        var offered = new List<CarrierJob>();
        var accepted = new List<CarrierJob>();

        foreach (var row in all.EnumerateArray())
        {
            var key = Field(row, "key");
            if (key.Length == 0) continue;

            var request = active.FirstOrDefault(r => r.JobKey == key && r.Outcome == CarrierAssignment.Pending);
            if (request is not null)
            {
                offered.Add(Describe(row, key, request));
                continue;
            }

            request = active.FirstOrDefault(r => r.JobKey == key && r.Outcome == CarrierAssignment.Confirmed);
            if (request is not null)
            {
                accepted.Add(Describe(row, key, request));
                continue;
            }

            // Historical jobs written before assignments existed remain
            // visible by their register carrier. Once a job has assignment
            // history, only its current confirmed assignment grants access.
            if (!assignedKeys.Contains(key) && names.Contains(Field(row, "trucker").Trim()))
                accepted.Add(Describe(row, key, null));
        }

        var schedule = accepted.OrderByDescending(job => job.Date).ToList();
        return new Portal(company.Id, company.Name,
            offered.OrderBy(job => job.RequestedAt ?? DateTimeOffset.MaxValue).ToList(),
            schedule,
            schedule);
    }

    /// <summary>
    /// Accepting a job makes it operationally available immediately. Truck and
    /// driver details are optional here and remain a separate Phase 3 action;
    /// existing callers that send them continue to write them in the same call.
    /// </summary>
    public async Task<Result> AcceptAsync(AppUser user, string jobKey, long? requestId, string licence,
        string driver, string contact, CancellationToken token)
    {
        var company = await CompanyOfAsync(user, token);
        if (company is null) return new Result(false, "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา", Code: ResultCode.NoCompany);

        licence = licence.Trim();
        driver = driver.Trim();
        contact = contact.Trim();
        return await AcceptAssignmentAsync(company, jobKey, requestId, licence, driver, contact,
            "", "", user.Signature, token);
    }

    /// <summary>
    /// The acceptance for a supplier the caller has settled — the portal
    /// through <see cref="CompanyOfAsync"/>, the Carrier TMS API through its
    /// key — with the plate, the driver and the number already checked by
    /// the caller. The box and the seal (an export's) go only into empty
    /// cells; one the register holds with another value is a conflict, and
    /// nothing is written.
    /// </summary>
    public async Task<Result> AcceptForAsync(Supplier company, string jobKey, string licence, string driver,
        string contact, string container, string seal, string by, CancellationToken token)
        => await AcceptAssignmentAsync(company, jobKey, null, licence, driver, contact, container, seal, by, token);

    public async Task<Result> AcceptAssignmentAsync(Supplier company, string jobKey, long? requestId,
        string licence, string driver, string contact, string container, string seal,
        string by, CancellationToken token)
    {
        var names = await NamesOfAsync(company, token);

        // The request is the authority. Without one addressed to this carrier
        // there is nothing to accept — and accepting on the strength of a job
        // key alone would let any carrier take any job by guessing it.
        var request = await db.SupplierRequests.Where(r => r.JobKey == jobKey).ToListAsync(token);
        var eligible = request.Where(r => CarrierAssignment.BelongsTo(
            r.SupplierId, r.Carrier, company.Id, names)).ToList();
        var ours = requestId is { } wanted
            ? eligible.FirstOrDefault(r => r.Id == wanted)
            : eligible.Where(r => r.Outcome is CarrierAssignment.Pending or CarrierAssignment.Confirmed)
                .OrderByDescending(r => r.Rank).ThenByDescending(r => r.Id).FirstOrDefault();
        if (ours is null)
            return new Result(false, "งานนี้ไม่ได้ถูกส่งมาให้บริษัทนี้ หรือถูกตอบไปแล้ว", Code: ResultCode.NotOffered);
        var decision = CarrierAssignment.DecideAnswer(ours.Outcome, CarrierAssignment.Confirmed);
        if (decision == AssignmentAnswerDecision.Replay)
            return new Result(true, $"รับงาน {jobKey} ไว้แล้ว", Replayed: true, AssignmentId: ours.Id);
        if (decision == AssignmentAnswerDecision.Refuse)
            return new Result(false, "assignment นี้ปิดหรือถูกแทนที่แล้ว", Code: ResultCode.NotOffered);

        // Read before writing. There is no other copy: the register holds
        // current state only, and once these three fields are overwritten the
        // previous plate and driver are gone from the system entirely.
        var before = await jobs.SnapshotAsync([jobKey], token);
        var was = before.TryGetValue(jobKey, out var fields)
            ? $"{Value(fields, "trucker")} · {Value(fields, "licence")} · {Value(fields, "driver")} · {Value(fields, "contact")}"
            : "";
        var jobStatus = fields?.GetValueOrDefault("status", "") ?? "";
        if (string.Equals(jobStatus, JobStatus.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(jobStatus, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            return new Result(false, $"งานนี้ปิดแล้ว ({jobStatus})", Code: ResultCode.Closed);

        var writes = new Dictionary<string, string>
        {
            ["trucker"] = ours.Carrier,
            ["status"] = JobStatus.SupplierConfirmed,
        };
        // The box and the seal are not the acceptance; they are offered to
        // empty cells, and a cell the department keyed differently stops the
        // whole call before the request is answered.
        var conflicts = new List<Conflict>();
        var skipped = new List<string>();
        var previous = new Dictionary<string, string>();
        Offer(writes, conflicts, skipped, previous, fields, "licence", licence);
        Offer(writes, conflicts, skipped, previous, fields, "driver", driver);
        Offer(writes, conflicts, skipped, previous, fields, "contact", contact);
        Offer(writes, conflicts, skipped, previous, fields, "container", container);
        Offer(writes, conflicts, skipped, previous, fields, "seal", seal);
        if (conflicts.Count > 0)
            return new Result(false, Describe(conflicts), Code: ResultCode.Conflict, Conflicts: conflicts);

        ours.Outcome = CarrierAssignment.Confirmed;
        ours.RespondedAt = DateTimeOffset.UtcNow;
        ours.RespondedBy = by;

        if (!await ApplyJobFieldsAsync(jobKey, writes, by, token))
            return new Result(false, "บันทึกข้อมูลรถไม่สำเร็จ", Code: ResultCode.Failed);

        // Any other carrier still holding an open invitation for this job is no
        // longer being asked. Leaving those pending would have two carriers
        // believing the work is theirs.
        foreach (var other in request.Where(r => r.Id != ours.Id && r.Outcome == CarrierAssignment.Pending))
        {
            other.Outcome = CarrierAssignment.Superseded;
            other.Reason = "งานถูกรับโดยผู้รับเหมารายอื่นแล้ว";
            other.ReasonCode = "OTHER_CARRIER_ACCEPTED";
            other.Remark = other.Reason;
            other.RespondedAt = DateTimeOffset.UtcNow;
            other.RespondedBy = by;
        }

        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var latest = await db.SupplierRequests.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == ours.Id, token);
            if (latest?.Outcome == CarrierAssignment.Confirmed)
                return new Result(true, $"รับงาน {jobKey} ไว้แล้ว", Replayed: true, AssignmentId: ours.Id);
            return new Result(false, "assignment ถูกตอบหรือเปลี่ยนผู้ขนส่งระหว่างทำรายการ",
                Code: ResultCode.NotOffered);
        }
        register.Invalidate();
        // Each carrier whose ask just closed hears so, when its system asked to.
        foreach (var other in request.Where(r => r.Id != ours.Id && r.Outcome == CarrierAssignment.Superseded))
            await webhooks.CancelledAsync(jobKey, other.Carrier, other.Id, other.Reason, "", token);
        var vehicle = new[] { licence, driver }.Where(value => value.Length > 0).ToArray();
        return new Result(true, $"รับงาน {jobKey} แล้ว" + (vehicle.Length > 0 ? " · " + string.Join(" · ", vehicle) : ""),
            was, Written: writes, Skipped: skipped, Previous: previous, AssignmentId: ours.Id);
    }

    public async Task<Result> DeclineAsync(AppUser user, string jobKey, long? requestId,
        string reasonCode, string remark, CancellationToken token)
    {
        var company = await CompanyOfAsync(user, token);
        if (company is null) return new Result(false, "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา", Code: ResultCode.NoCompany);
        return await DeclineAssignmentAsync(company, jobKey, requestId, reasonCode, remark, user.Signature, token);
    }

    /// <summary>The refusal for a supplier the caller has settled — see <see cref="AcceptForAsync"/>.</summary>
    public async Task<Result> DeclineForAsync(Supplier company, string jobKey, string reason, CancellationToken token)
        => await DeclineAssignmentAsync(company, jobKey, null, "OTHER", reason,
            $"carrier:{company.Code}", token);

    public async Task<Result> DeclineAssignmentAsync(Supplier company, string jobKey, long? requestId,
        string reasonCode, string remark, string by, CancellationToken token)
    {
        var code = reasonCode.Trim().ToUpperInvariant();
        var detail = remark.Trim();
        if (code.Length == 0) code = "OTHER";
        if (code == "OTHER" && detail.Length == 0)
            return new Result(false, "ต้องระบุเหตุผลที่รับงานไม่ได้", Code: ResultCode.Invalid);

        var names = await NamesOfAsync(company, token);
        var eligible = (await db.SupplierRequests.Where(r => r.JobKey == jobKey).ToListAsync(token))
            .Where(r => CarrierAssignment.BelongsTo(r.SupplierId, r.Carrier, company.Id, names)).ToList();
        var ours = requestId is { } wanted
            ? eligible.FirstOrDefault(r => r.Id == wanted)
            : eligible.Where(r => r.Outcome is CarrierAssignment.Pending or CarrierAssignment.Rejected)
                .OrderByDescending(r => r.Rank).ThenByDescending(r => r.Id).FirstOrDefault();
        if (ours is null) return new Result(false, "ไม่พบคำขอที่ยังรอตอบสำหรับบริษัทนี้", Code: ResultCode.NotOffered);
        var decision = CarrierAssignment.DecideAnswer(ours.Outcome, CarrierAssignment.Rejected);
        if (decision == AssignmentAnswerDecision.Replay)
            return new Result(true, "แจ้งปฏิเสธงานไว้แล้ว", Replayed: true, AssignmentId: ours.Id);
        if (decision == AssignmentAnswerDecision.Refuse)
            return new Result(false, "assignment นี้ปิดหรือถูกแทนที่แล้ว", Code: ResultCode.NotOffered);

        ours.Outcome = CarrierAssignment.Rejected;
        ours.Reason = detail.Length > 0 ? detail : code;
        ours.ReasonCode = code;
        ours.Remark = detail;
        ours.RespondedAt = DateTimeOffset.UtcNow;
        ours.RespondedBy = by;
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var latest = await db.SupplierRequests.AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == ours.Id, token);
            if (latest?.Outcome == CarrierAssignment.Rejected)
                return new Result(true, "แจ้งปฏิเสธงานไว้แล้ว", Replayed: true, AssignmentId: ours.Id);
            return new Result(false, "assignment ถูกตอบหรือเปลี่ยนผู้ขนส่งระหว่างทำรายการ",
                Code: ResultCode.NotOffered);
        }

        // Deliberately does not move the job on. Who to ask next is the
        // operator's decision and the escalation rule's business, not the
        // carrier's who just said no.
        return new Result(true, "แจ้งปฏิเสธงานแล้ว", AssignmentId: ours.Id);
    }

    /// <summary>
    /// Stages a partial job update in this DbContext so accepting the assignment
    /// and updating My Jobs commit atomically in the following SaveChanges.
    /// </summary>
    private async Task<bool> ApplyJobFieldsAsync(string jobKey, IReadOnlyDictionary<string, string> fields,
        string by, CancellationToken token)
    {
        var job = await db.OperationJobs.FirstOrDefaultAsync(row => row.Key == jobKey, token);
        if (job is null) return false;

        System.Text.Json.Nodes.JsonObject? node;
        try { node = System.Text.Json.Nodes.JsonNode.Parse(job.Data)?.AsObject(); }
        catch (System.Text.Json.JsonException) { return false; }
        if (node is null) return false;

        foreach (var (name, value) in fields) node[name] = value;
        if (fields.TryGetValue("trucker", out var trucker)) job.Trucker = trucker;
        if (fields.TryGetValue("status", out var status)) job.Status = status;
        if (fields.TryGetValue("container", out var container)) job.Container = container;
        job.Data = node.ToJsonString();
        job.UpdatedBy = by;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// The truck's details on a job this carrier holds, written into the
    /// cells that are empty — the rule every outside writer follows (LINE's
    /// approval writes the same way). A cell already holding what was sent
    /// is skipped; one holding something else is a conflict, and then
    /// nothing is written: the register's value stands and the person who
    /// keyed it changes it on the grid. A closed job takes nothing.
    /// </summary>
    public async Task<Result> UpdateTruckForAsync(Supplier company, string jobKey, string licence, string driver,
        string contact, string container, string seal, string by, CancellationToken token)
    {
        var names = await NamesOfAsync(company, token);
        var before = await jobs.SnapshotAsync([jobKey], token);
        if (!before.TryGetValue(jobKey, out var fields) || !names.Contains(fields.GetValueOrDefault("trucker", "").Trim()))
            return new Result(false, "งานนี้ไม่ได้อยู่กับบริษัทนี้", Code: ResultCode.NotHeld);
        var status = fields.GetValueOrDefault("status", "");
        if (string.Equals(status, JobStatus.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            return new Result(false, $"งานนี้ปิดแล้ว ({status})", Code: ResultCode.Closed);

        var writes = new Dictionary<string, string>();
        var conflicts = new List<Conflict>();
        var skipped = new List<string>();
        var previous = new Dictionary<string, string>();
        Offer(writes, conflicts, skipped, previous, fields, "licence", licence);
        Offer(writes, conflicts, skipped, previous, fields, "driver", driver);
        Offer(writes, conflicts, skipped, previous, fields, "contact", contact);
        Offer(writes, conflicts, skipped, previous, fields, "container", container);
        Offer(writes, conflicts, skipped, previous, fields, "seal", seal);
        if (conflicts.Count > 0)
            return new Result(false, Describe(conflicts), Code: ResultCode.Conflict, Conflicts: conflicts, Skipped: skipped);
        if (writes.Count == 0)
            return new Result(true, "งานมีข้อมูลเหล่านี้อยู่แล้ว", Written: writes, Skipped: skipped);

        var was = string.Join(" · ", TruckCells.Where(cell => writes.ContainsKey(cell.Name)).Select(cell => Value(fields, cell.Name)));
        var saved = await jobs.PatchAsync(jobKey, writes, by, token);
        if (!saved) return new Result(false, "บันทึกข้อมูลรถไม่สำเร็จ", Code: ResultCode.Failed);
        return new Result(true, "บันทึก " + string.Join(" · ", TruckCells.Where(cell => writes.ContainsKey(cell.Name)).Select(cell => $"{cell.Label} {writes[cell.Name]}")),
            was, Written: writes, Skipped: skipped, Previous: previous);
    }

    /// <summary>
    /// A value offered to one cell: written when the cell is empty, skipped
    /// when the cell already says so, a conflict when it says otherwise. An
    /// empty offer is no offer.
    /// </summary>
    private static void Offer(Dictionary<string, string> writes, List<Conflict> conflicts, List<string> skipped,
        Dictionary<string, string> previous, IReadOnlyDictionary<string, string>? fields, string name, string value)
    {
        if (value.Length == 0) return;
        var raw = fields?.GetValueOrDefault(name, "") ?? "";
        var current = Formats.Clean(raw);
        if (current.Length == 0) { writes[name] = value; previous[name] = raw; }
        else if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase)) skipped.Add(name);
        else conflicts.Add(new Conflict(name, current, value));
    }

    private static string Describe(IReadOnlyList<Conflict> conflicts) =>
        "งานมี " + string.Join(" · ", conflicts.Select(one =>
            $"{(TruckCells.FirstOrDefault(cell => cell.Name == one.Field).Label ?? one.Field)} {one.Current} อยู่แล้ว (ส่งมา {one.Sent})"))
        + " — แก้ในตารางงานถ้าต้องการ";

    private static CarrierJob Describe(System.Text.Json.JsonElement row, string key, SupplierRequest? request) =>
        new(key, Field(row, "jobCode"), Field(row, "customer"), Field(row, "destination"),
            Field(row, "type"), Field(row, "cyYard"), Field(row, "weight"), Field(row, "container"),
            Field(row, "date"), Field(row, "pickupPlan"), Field(row, "status"),
            request?.Id, request?.QuotedPrice, request?.RequestedAt,
            Field(row, "licence"), Field(row, "driver"), Field(row, "contact"),
            Category: Field(row, "cat"), Booking: Field(row, "booking"), PlanTime: Field(row, "planTime"),
            Plant: Field(row, "plant"), ReturnLoc: Field(row, "returnLoc"),
            ArrDate: Field(row, "arrDate"), ArrTime: Field(row, "arrTime"), Seal: Field(row, "seal"),
            RespondedAt: request?.RespondedAt,
            AssignmentOutcome: request?.Outcome ?? "legacy",
            OperationalAvailable: request is null || request.Outcome == CarrierAssignment.Confirmed);

    private static string Value(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) && value.Length > 0 ? value : "(ว่าง)";

    private static string Field(System.Text.Json.JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
