namespace Scmos.Api.Data;

/// <summary>
/// The operation plan as the workspace works it.
///
/// The queryable fields are columns so the register can be filtered and reported
/// on in SQL; the job itself is kept as JSON in <see cref="Data"/>, because the
/// workspace's job model owns the other forty-odd fields and they change
/// together. One row per job, keyed by the job's own key, so a save is an upsert.
///
/// The column widths are the ones the previous Worker truncated to, so a job
/// that fitted before still fits.
/// </summary>
public class OperationJob
{
    public string Key { get; set; } = "";
    public string Cat { get; set; } = "";

    /// <summary>Display name of the operator, as it came off the plan workbooks.</summary>
    public string Owner { get; set; } = "";

    /// <summary>
    /// Stable owner id (OP-01…OP-05). Ownership used to be decided by matching
    /// the display name, which real sign-in would have broken on the first day —
    /// Entra returns an email and a full name, not "Watsana".
    /// </summary>
    public string OwnerId { get; set; } = "";

    public string WorkDate { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Trucker { get; set; } = "";
    public string JobCode { get; set; } = "";
    public string Container { get; set; } = "";
    public string Status { get; set; } = "";

    /// <summary>The whole job, as the workspace serialises it.</summary>
    public string Data { get; set; } = "";

    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One thing that happened to a job's workflow.
///
/// Append-only. A job's current stage is the last event, never a column that is
/// overwritten — so "why is this job parked" and "who released it" are always
/// answerable, which is the whole reason the Audit domain exists.
/// </summary>
public class WorkflowEvent
{
    public long Id { get; set; }
    public string JobKey { get; set; } = "";

    /// <summary>advance · hold · release · supplier-request · supplier-response</summary>
    public string Kind { get; set; } = "";

    /// <summary>The stage the job was at before this event.</summary>
    public string FromStage { get; set; } = "";

    /// <summary>The stage it moved to. Same as <see cref="FromStage"/> for a hold.</summary>
    public string ToStage { get; set; } = "";

    /// <summary>Empty unless the job is being parked or released.</summary>
    public string Hold { get; set; } = "";

    /// <summary>What the person answered at a gate, and why.</summary>
    public string Note { get; set; } = "";

    public string By { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// A carrier being asked to take a job.
///
/// The process escalates A to B to C, and how long each one took to answer — and
/// why they said no — is the only real measure of a subcontractor's
/// responsiveness. Cancelling a request is a fact worth keeping, not a row to
/// delete.
/// </summary>
public class SupplierRequest
{
    public long Id { get; set; }
    public string JobKey { get; set; } = "";

    /// <summary>1 for the first carrier asked, 2 for the next, and so on.</summary>
    public int Rank { get; set; }

    public string Carrier { get; set; } = "";

    /// <summary>What this carrier quoted when asked, if a rate was known.</summary>
    public int? QuotedPrice { get; set; }

    /// <summary>pending · confirmed · rejected · cancelled · no-response</summary>
    public string Outcome { get; set; } = "pending";

    /// <summary>Why they declined — "no trailer", "driver shortage".</summary>
    public string Reason { get; set; } = "";

    public string RequestedBy { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>Minutes taken to answer. Null while the request is still open.</summary>
    public int? ResponseMinutes => RespondedAt is null
        ? null
        : (int)Math.Round((RespondedAt.Value - RequestedAt).TotalMinutes);
}

/// <summary>
/// One pre-run list sent to a carrier for one job.
///
/// Every field here exists to be counted later: the two timestamps give the
/// response time, the outcome gives the confirmation rate, and the correction
/// text is the only record of what the carrier changed the night before. A
/// correction that is not written down becomes a surprise at the gate.
/// </summary>
public class PreRunCheck
{
    public long Id { get; set; }
    public string JobKey { get; set; } = "";

    /// <summary>The plan date this list covers, as the plan writes it (DD/MM/YYYY).</summary>
    public string ShipmentDate { get; set; } = "";

    public string Carrier { get; set; } = "";

    public DateTimeOffset SentAt { get; set; }
    public string SentBy { get; set; } = "";

    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>Who at the carrier answered. Their name, not ours.</summary>
    public string ConfirmedBy { get; set; } = "";

    /// <summary>What the carrier says will actually turn up.</summary>
    public string TruckNo { get; set; } = "";
    public string Driver { get; set; } = "";
    public string DriverContact { get; set; } = "";

    /// <summary>What they changed from what was sent. Empty on a clean confirmation.</summary>
    public string Correction { get; set; } = "";

    public string Remark { get; set; } = "";

    /// <summary>pending · confirmed · corrected · no-response</summary>
    public string Outcome { get; set; } = "pending";

    /// <summary>none · alert · follow-up · escalated</summary>
    public string Escalation { get; set; } = "none";

    /// <summary>Recorded when the answer lands, so the KPI does not recompute it every read.</summary>
    public int? ResponseMinutes { get; set; }
}

/// <summary>
/// One milestone on a shipment: dispatch, pickup, loading, transit, delivery,
/// container return, completed.
///
/// Planned and actual sit side by side on every row, because the gap between
/// them is the only thing a delay is. The carrier, truck and driver are recorded
/// per milestone rather than read off the job, since they legitimately change
/// mid-run — a truck breaks down and a different plate finishes the trip, and a
/// register that only holds the latest cannot say when it changed.
/// </summary>
public class ShipmentMilestone
{
    public long Id { get; set; }
    public string JobKey { get; set; } = "";

    /// <summary>Dispatched · PickedUp · Loading · InTransit · Delivered · ContainerReturned · Closed</summary>
    public string Stage { get; set; } = "";

    /// <summary>When the plan says this should happen, as the plan writes it.</summary>
    public string PlannedAt { get; set; } = "";

    public DateTimeOffset? ActualAt { get; set; }

    /// <summary>pending · done · delayed · skipped</summary>
    public string Status { get; set; } = "pending";

    public string Carrier { get; set; } = "";
    public string TruckNo { get; set; } = "";
    public string Driver { get; set; } = "";

    public string Remark { get; set; } = "";

    /// <summary>Required when the status is delayed — a delay without a reason teaches nobody anything.</summary>
    public string DelayReason { get; set; } = "";

    /// <summary>Blob object key of the supporting photo. Empty when none was attached.</summary>
    public string PhotoKey { get; set; } = "";

    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A delay, categorised and owned.
///
/// Separate from the milestone it happened on, because one shipment can be
/// delayed twice for different reasons by different people, and a single
/// delay_reason column on the job would keep only the last one — which is how a
/// supplier ends up blamed for a customs hold.
/// </summary>
public class DelayRecord
{
    public long Id { get; set; }
    public string JobKey { get; set; } = "";

    /// <summary>The milestone it was detected on.</summary>
    public string Stage { get; set; } = "";

    /// <summary>Truck · Driver · Port · Depot · Customer · Documentation · Traffic · Other</summary>
    public string Category { get; set; } = "Other";

    /// <summary>What the operator actually wrote, in their words.</summary>
    public string Detail { get; set; } = "";

    /// <summary>Subcontractor · Operation · CustomerService · Customer · Port · None</summary>
    public string Responsible { get; set; } = "None";

    /// <summary>How the category was arrived at: rule · ai · human.</summary>
    public string ClassifiedBy { get; set; } = "human";

    /// <summary>What the classifier matched on, kept so a wrong call can be argued with.</summary>
    public string ClassifierBasis { get; set; } = "";

    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>How late this made the shipment, in minutes, once it is known.</summary>
    public int? ImpactMinutes { get; set; }

    public DateTimeOffset? NotifiedAt { get; set; }
    public string NotifiedTeam { get; set; } = "";

    public string RecoveryAction { get; set; } = "";
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Whether it counts against the carrier's scorecard.</summary>
    public bool AgainstCarrier { get; set; }

    public string RecordedBy { get; set; } = "";
}

/// <summary>
/// A CAR/PAR case: something went wrong badly enough to need a corrective and a
/// preventive action, with a person and a date against each.
///
/// The fields follow the quality process the team already runs on paper — 5W1H,
/// root cause, corrective, preventive, effectiveness — so a case moved into the
/// system is the same case, not a new shape somebody has to learn.
/// </summary>
public class IncidentCase
{
    public long Id { get; set; }

    /// <summary>CAR-26-07 style reference, issued on creation.</summary>
    public string Reference { get; set; } = "";

    /// <summary>The job it came from. Empty for a case raised on its own.</summary>
    public string JobKey { get; set; } = "";

    /// <summary>CAR (corrective) or PAR (preventive).</summary>
    public string Kind { get; set; } = "CAR";

    /// <summary>
    /// accident · damage · delay · safety · quality · other.
    ///
    /// Separate from Kind because "was anybody hurt" and "is this corrective or
    /// preventive" are different questions, and the accident count reported
    /// upward is the answer to the first.
    /// </summary>
    public string Category { get; set; } = "other";

    public string Title { get; set; } = "";

    /// <summary>open · analysis · action · follow-up · monitoring · approval · closed</summary>
    public string Stage { get; set; } = "open";

    /* ---- 5W1H, as the quality form asks for it ---- */
    public string What { get; set; } = "";
    public string Where { get; set; } = "";
    public string When { get; set; } = "";
    public string Who { get; set; } = "";
    public string Why { get; set; } = "";
    public string How { get; set; } = "";

    /// <summary>Written by the AI summary agent from the evidence, for a person to correct.</summary>
    public string AiSummary { get; set; } = "";

    public string RootCause { get; set; } = "";
    public string CorrectiveAction { get; set; } = "";
    public string PreventiveAction { get; set; } = "";

    public string ResponsiblePerson { get; set; } = "";
    public string DueDate { get; set; } = "";

    public string FollowUpNote { get; set; } = "";
    public string EffectivenessNote { get; set; } = "";

    /* ---- the rest of ISO-FRM-TH-ISO-08-09, the 8D form the team fills in ----
     *
     * The fields above already covered D2 (what/where/when/who/why/how), D4
     * (root cause), D5 (corrective) and D7 (preventive) — the form was built
     * around the same method. These are the parts of it the record had nowhere
     * to put, so they were being written on paper beside a case that could not
     * hold them.
     */

    /// <summary>LSTH · LSSV · LSCC · TIH — which company the form is raised under.</summary>
    public string Company { get; set; } = "";

    /// <summary>Major or Minor for a CAR, OBS for a PAR. The form's own grading.</summary>
    public string Grade { get; set; } = "";

    /// <summary>Customer complaint · internal audit · management review · other.</summary>
    public string Source { get; set; } = "";

    /// <summary>The clause a non-conformity was raised against, when there is one.</summary>
    public string NcClause { get; set; } = "";

    /// <summary>D1 — who is working the case, as the form lists them.</summary>
    public string Team { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public string RequestedOn { get; set; } = "";

    /// <summary>D3 — what was done straight away, before the cause was known.</summary>
    public string ImmediateAction { get; set; } = "";
    public string ImmediateBy { get; set; } = "";
    public string ImmediateDue { get; set; } = "";

    /// <summary>Which documents the fix means rewriting: SOP, a form, something else.</summary>
    public string DocumentsToRevise { get; set; } = "";

    /// <summary>Who followed the action up, and who reviewed it. Two people, two jobs.</summary>
    public string FollowUpBy { get; set; } = "";
    public string ReviewedBy { get; set; } = "";

    /// <summary>
    /// The approval decision in the form's own words: closed, or not accepted
    /// with a reason. Kept apart from <see cref="ApprovedBy"/>, which records
    /// who signed — refusing to accept is also a signature.
    /// </summary>
    public string ApprovalOutcome { get; set; } = "";
    public string ApprovalNote { get; set; } = "";

    /// <summary>D8 — what the team is credited with when the case closes.</summary>
    public string TeamNote { get; set; } = "";

    /// <summary>
    /// A case cannot close without a person's name against it. The AI writes the
    /// summary; it does not sign off the action.
    /// </summary>
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset? ApprovedAt { get; set; }

    public string RaisedBy { get; set; } = "";
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// Evidence on a case is a file like any other and lives in StoredDocument with
// its CaseId set. It had its own table for exactly as long as it took to give
// files a single home — see Data/DocumentEntities.cs.

/* ------------------------------------------------------------------------
 * Carried over from the system this replaced.
 *
 * ReportUpload, OperationUpload and OperationEntry back /api/uploads and
 * /api/operations. No screen calls either route any more — the register is
 * OperationJob and files are StoredDocument — and all three tables are empty in
 * every environment checked so far.
 *
 * They are still here on purpose. "Empty locally" is not "empty in production",
 * and dropping a table is the one migration that cannot be undone by running the
 * next one. Before removing them, run this against the real database:
 *
 *   SELECT (SELECT COUNT(*) FROM report_uploads)    AS reports,
 *          (SELECT COUNT(*) FROM operation_uploads) AS op_uploads,
 *          (SELECT COUNT(*) FROM operation_entries) AS entries;
 *
 * If all three are zero, the tables, their endpoints and this comment can go.
 * ---------------------------------------------------------------------- */

/// <summary>A file kept in Blob Storage, with the row counts it was reported with.</summary>
public class ReportUpload
{
    public Guid Id { get; set; }
    public string Period { get; set; } = "";
    public string Filename { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public int RowCount { get; set; }
    public int IssueCount { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

/// <summary>Who submitted an upload, and for which flow.</summary>
public class OperationUpload
{
    public Guid Id { get; set; }
    public Guid UploadId { get; set; }
    public string OwnerName { get; set; } = "";
    public string Flow { get; set; } = "";
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
}

/// <summary>The keyed-entry table from the previous UI. Kept so its history survives the move.</summary>
public class OperationEntry
{
    public Guid Id { get; set; }
    public string OwnerName { get; set; } = "";
    public string WorkDate { get; set; } = "";
    public string ReportingPeriod { get; set; } = "";
    public string Flow { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Subcontractor { get; set; } = "";
    public string JobCode { get; set; } = "";
    public string? ContainerNo { get; set; }
    public string? EquipmentType { get; set; }
    public string PlanAt { get; set; } = "";
    public string? ActualAt { get; set; }
    public string OperationStatus { get; set; } = "";
    public string ValidationStatus { get; set; } = "";
    public string OtdStatus { get; set; } = "";
    public string? Remark { get; set; }
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One problem somebody hit while running a job, on the day they hit it.
///
/// Taken from the issue log the team already keeps in Excel, column for column,
/// so a month recorded there can be read here without anybody re-typing it.
///
/// Deliberately not an <see cref="IncidentCase"/>. A CAR/PAR is a quality
/// document — 5W1H, root cause, corrective and preventive action, an approval
/// — raised occasionally and worked for weeks. This is the daily log: a
/// container that could not be collected, a document missing from the card set,
/// a lorry sitting at a gate. Several a day, most closed the same week. Forcing
/// the light thing through the heavy form is how a log stops being kept.
///
/// The two meet at <see cref="JobKey"/>: an issue and a case can point at the
/// same job, and an issue that turns out to be serious is the evidence a case
/// gets raised from.
/// </summary>
public class OperationalIssue
{
    public long Id { get; set; }

    /// <summary>OTL-0001, issued in order and never reused.</summary>
    public string Code { get; set; } = "";

    /// <summary>dd/MM/yyyy, written the way work_date is so the two compare.</summary>
    public string FoundOn { get; set; } = "";

    /// <summary>HH:mm. Blank on a row logged after the fact, which is common.</summary>
    public string FoundAt { get; set; } = "";

    /// <summary>Where the problem came from: CS/Shipping, Subcontractor, Customer, Customs, Depot.</summary>
    public string Source { get; set; } = "";

    /// <summary>Who raised it — a person, or the carrier who rang in.</summary>
    public string Reporter { get; set; } = "";

    /// <summary>
    /// The job reference as it was written down, kept verbatim.
    ///
    /// Not everything here resolves to a job in the register: some rows carry
    /// two numbers separated by a slash, some name a shipment the register
    /// never held, and six rows in the delivered log carry none at all. The
    /// written reference is what the person actually recorded, so it is kept
    /// whether or not it matched anything.
    /// </summary>
    public string JobRef { get; set; } = "";

    /// <summary>
    /// The job this attaches to, when the reference found one. Empty otherwise,
    /// and empty is not a failure — an issue against a shipment that never
    /// became a job is still a real issue.
    /// </summary>
    public string JobKey { get; set; } = "";

    public string Detail { get; set; } = "";

    /// <summary>The logistics category: late collection, documents, rent, damage.</summary>
    public string Category { get; set; } = "";

    /// <summary>วิกฤต · สูง · ปานกลาง · ต่ำ</summary>
    public string Severity { get; set; } = "";

    /// <summary>What it did to the transport, in the reporter's own words.</summary>
    public string Impact { get; set; } = "";

    /// <summary>How it came in: phone, Line, Teams, email, on site.</summary>
    public string Channel { get; set; } = "";

    /*
     * Who was driving, what they were carrying and on which lorry.
     *
     * Kept on the issue and not only on the job. Two thirds of these rows are
     * about a driver, a container or a plate, and the job they attach to is
     * exactly the thing that sometimes does not exist — a written reference
     * that matched nothing still describes a real problem with a real lorry.
     * They are also what the case escalated from here has to name, and asking
     * somebody to remember a container number a week later is asking for a
     * different container number.
     */
    public string Driver { get; set; } = "";
    public string ContainerNo { get; set; } = "";
    public string Licence { get; set; } = "";

    /// <summary>
    /// Minor or Major, on an accident. Blank on anything else, and blank on an
    /// accident nobody has graded yet.
    ///
    /// Its own field rather than read off Severity. The severity ladder is
    /// วิกฤต · สูง · ปานกลาง · ต่ำ and answers "how fast must this be dealt
    /// with", which is a different question from how serious the accident was —
    /// a minor scrape that blocks a customer's gate is urgent and minor at
    /// once. The carrier scorecard weights Major at 35% and Minor at 15%, so
    /// deriving one from the other would put more than a third of a carrier's
    /// score on a mapping nobody agreed to.
    ///
    /// An ungraded accident is counted and reported, and left out of the score.
    /// Guessing either way would be inventing the answer.
    /// </summary>
    public string AccidentGrade { get; set; } = "";

    /// <summary>The SMT member holding it, and the id behind the name.</summary>
    public string Owner { get; set; } = "";
    public string OwnerId { get; set; } = "";

    /// <summary>dd/MM/yyyy HH:mm, or blank where none was agreed.</summary>
    public string DueOn { get; set; } = "";

    /// <summary>เปิด · กำลังดำเนินการ · รอข้อมูล · รออนุมัติ · แก้ไขแล้ว · ปิด · ยกเลิก</summary>
    public string Status { get; set; } = "";

    public string RootCause { get; set; } = "";

    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Which customer belongs to which operator, and who covers when they are away.
///
/// Taken from the rotation workbook the team keeps — one sheet per operator,
/// a row per customer, with the modes that operator handles for them and the
/// two people behind them. It is the answer to "why is this job mine", which
/// the register has never held: a job arrives carrying an operator's name off a
/// plan sheet, and nothing until now said whether that was the right name.
///
/// Reference data, not a transaction. It changes when the team reshuffles,
/// which the file's own name records — "Update 03.07.2026".
/// </summary>
public class RotationAssignment
{
    public long Id { get; set; }

    /// <summary>The customer as the rotation sheet spells them.</summary>
    public string Customer { get; set; } = "";

    /// <summary>Which sheet it came from, so a row can be traced back.</summary>
    public string Sheet { get; set; } = "";

    /* ---- the modes this operator handles for this customer ---- */
    public bool Import { get; set; }
    public bool Export { get; set; }
    public bool Fcl { get; set; }
    public bool Lcl { get; set; }
    public bool Domestic { get; set; }

    /// <summary>
    /// The primary operator's cell, kept whole.
    ///
    /// It holds an email, a mobile and an extension run together —
    /// "uthai.yodbunnok@leschaco.com 092-9919449 #7048". The email is pulled out
    /// because it is the identity; the rest is kept as written because it is
    /// what somebody needs to actually ring them, and re-formatting a phone
    /// number is how a digit goes missing.
    /// </summary>
    public string PrimaryContact { get; set; } = "";
    public string PrimaryEmail { get; set; } = "";

    /// <summary>The directory id behind the email, when the directory knows it.</summary>
    public string PrimaryId { get; set; } = "";

    public string BackupContact { get; set; } = "";
    public string BackupEmail { get; set; } = "";
    public string Backup2Contact { get; set; } = "";
    public string Backup2Email { get; set; } = "";

    /// <summary>Carriers named for this customer, as the sheet lists them.</summary>
    public string SubFcl { get; set; } = "";
    public string SubLcl { get; set; } = "";

    /// <summary>The customer service contact at LCB, where one is named.</summary>
    public string CsLcb { get; set; } = "";

    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
