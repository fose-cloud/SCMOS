namespace Scmos.Api.Data;

/// <summary>
/// A transport subcontractor.
///
/// This table is the answer to a problem that has been blocking work since the
/// rate cards were read: the register spells the same company three ways
/// (TATIYAPOL, TTP, TATIYAPON) and nothing could reconcile them, so 21 carriers
/// cannot be scored and four with 310 jobs have no rate card anyone can find.
/// A supplier is now a row with an id, and every spelling points at it.
/// </summary>
public class Supplier
{
    public int Id { get; set; }

    /// <summary>Short code the team uses. Unique.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>draft · pending-audit · approved · suspended · rejected</summary>
    public string Status { get; set; } = "draft";

    public string VendorNo { get; set; } = "";
    public string TaxId { get; set; } = "";
    public string Address { get; set; } = "";
    public string ServiceArea { get; set; } = "";

    /* ---- from the company's ASL/BSL list in ABS ---------------------- */

    /// <summary>
    /// The supplier's number in ABS, which is where procurement keeps the
    /// approved-supplier list. The join between the two systems.
    /// </summary>
    public string AbsNo { get; set; } = "";

    /// <summary>ASL or BSL — which of the two lists this company is on.</summary>
    public string ListType { get; set; } = "";

    /// <summary>
    /// The company's registered name, as procurement's list writes it.
    ///
    /// <para>
    /// Beside <see cref="Name"/> rather than replacing it. The register knows
    /// this carrier as 9ISARA because that is what the plan writes, and every
    /// job row, rate card and alias points at that spelling; renaming it to
    /// "9 Isara Transport Co., Ltd." on the strength of an import would be a
    /// rename of the thing all of them refer to. So the legal name is recorded
    /// as its own field and the screens show it where a full name belongs —
    /// nothing has to be rewritten for both to be true.
    /// </para>
    /// </summary>
    public string LegalName { get; set; } = "";

    /// <summary>
    /// The one contact the ASL/BSL list carries, as an attribute of the company
    /// record rather than a row in <see cref="SupplierContact"/>.
    ///
    /// That table is for the several named people a carrier has — an
    /// operations contact, somebody in accounts — added by hand and merged
    /// when two supplier rows are folded together. These four came from the
    /// procurement register as fields of the company itself, and putting them
    /// in a table built for a list would mean every screen showing "the
    /// telephone number" first had to decide which of several it meant.
    /// </summary>
    public string ContactPerson { get; set; } = "";

    public string Telephone { get; set; } = "";
    public string Fax { get; set; } = "";
    public string Email { get; set; } = "";
    public string Website { get; set; } = "";

    /// <summary>
    /// Payment terms as the list writes them — "30 Days", "0 Day", "30".
    ///
    /// Kept as text rather than parsed to a number of days. Ten of the 642 rows
    /// carry one, in five different spellings, and turning that into an integer
    /// would mean inventing a 0 for the 632 that say nothing — which reads as
    /// "payment on delivery" rather than "nobody has filled this in".
    /// </summary>
    public string CreditTerm { get; set; } = "";

    /// <summary>What the company buys from them: Customs Clearance, Freight (Air / Sea).</summary>
    public string ServicesRequired { get; set; } = "";

    /// <summary>General or Deposit Container, in the list's own vocabulary.</summary>
    public string MainSpType { get; set; } = "";

    /// <summary>
    /// What they do, as procurement describes it — Logistic Agent, Sea Freight
    /// Agents/Liner, Transportation Services.
    ///
    /// Distinct from <see cref="ServiceType"/>, which is the rate card's
    /// vocabulary for what a truck can carry (FCL, ISO TANK). One says what
    /// kind of company this is; the other says what equipment it brings.
    /// </summary>
    public string TypeOfService { get; set; } = "";

    /// <summary>
    /// Whether this company moves cargo by road for us.
    ///
    /// The guard on everything the register feeds — the carrier scorecard,
    /// the annual evaluation, and the list of who a job may be given to. With
    /// 560 freight agents and customs brokers now in the register, a screen
    /// that asks "our carriers" and gets all of them is a screen that looks
    /// broken. Set at import from the union of two tests: what the list calls
    /// them, and whether they are already carrying work — see
    /// <see cref="Rules.SupplierRegister.LooksLikeCarrier"/>.
    /// </summary>
    public bool IsCarrier { get; set; } = true;

    /// <summary>FCL · LCL · ISO TANK · REEFER, comma separated as the team writes it.</summary>
    public string ServiceType { get; set; } = "";

    public bool DgCapable { get; set; }
    public bool ReeferCapable { get; set; }
    public bool IsoTankCapable { get; set; }
    public bool GpsEquipped { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
    public string ApprovedBy { get; set; } = "";

    /// <summary>Last annual evaluation score, 0-100. Null when never evaluated.</summary>
    public int? LastScore { get; set; }
    public string LastEvaluatedPeriod { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A spelling that means a supplier.
///
/// The register and the rate folder both carry names typed by hand over months.
/// Rather than correcting 2,102 rows and hoping no new spelling appears, every
/// spelling seen is recorded against the supplier it means, and lookups go
/// through here.
/// </summary>
public class SupplierAlias
{
    public int Id { get; set; }
    public int SupplierId { get; set; }

    /// <summary>The spelling as it appears, upper-cased. Unique.</summary>
    public string Alias { get; set; } = "";

    /// <summary>register · rate-card · manual — where this spelling was found.</summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// False when a person has not yet agreed that this spelling means this
    /// supplier. An unconfirmed alias is a suggestion, not a fact.
    /// </summary>
    public bool Confirmed { get; set; }
}

public class SupplierContact
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public bool Primary { get; set; }
}

// A supplier's insurance certificate, licence and audit report are files, and
// they live in StoredDocument with SupplierId set — one table, one path rule.
// The expiry the compliance screen watches is a column on that row.

public class SupplierTruck
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string Plate { get; set; } = "";

    /// <summary>The rate-card vocabulary: 4W, 6W, 10W, 20F, 40F, ISO TANK.</summary>
    public string VehicleType { get; set; } = "";

    public bool DgCapable { get; set; }
    public string RegistrationExpiry { get; set; } = "";
    public string Status { get; set; } = "active";
}

public class SupplierDriver
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LicenceNo { get; set; } = "";
    public string LicenceExpiry { get; set; } = "";
    public string TrainingExpiry { get; set; } = "";
    public string Status { get; set; } = "active";
}

/// <summary>How many trucks of a type a supplier says they have on a date.</summary>
public class SupplierCapacity
{
    public int Id { get; set; }
    public int SupplierId { get; set; }

    /// <summary>DD/MM/YYYY.</summary>
    public string Date { get; set; } = "";

    public string VehicleType { get; set; } = "";
    public int Available { get; set; }
    public int Committed { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// An annual evaluation, kept as a row so last year's score survives this
/// year's. A scorecard that only holds the current number cannot show whether a
/// carrier is improving, which is the only reason to run one every year.
/// </summary>
public class SupplierEvaluation
{
    public int Id { get; set; }
    public int SupplierId { get; set; }

    /// <summary>The year or period evaluated, e.g. "2026".</summary>
    public string Period { get; set; } = "";

    public int? OnTimeScore { get; set; }
    public int? ConfirmationScore { get; set; }
    public int? DelayScore { get; set; }
    public int? SafetyScore { get; set; }
    public int? DocumentScore { get; set; }

    /// <summary>The weighted total the meeting agreed on.</summary>
    public int? TotalScore { get; set; }

    public string Grade { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>draft · submitted · approved</summary>
    public string Stage { get; set; } = "draft";

    public string EvaluatedBy { get; set; } = "";
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/* --------------------------------------------------------------- rates */

/// <summary>One step of the fuel clause, shared by every quoted lane.</summary>
public class FuelBand
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }

    /// <summary>Position in fuel order. Lane prices index against this.</summary>
    public int Position { get; set; }
}

/// <summary>
/// A quoted lane.
///
/// Moved out of the file the web app was serving. It had to move for two
/// reasons: eighteen carriers' negotiated prices were sitting on a public path,
/// and the backend could not see them, so carrier priority could not be ordered
/// by price — which is the ordering the process actually wants.
/// </summary>
public class RateLane
{
    public long Id { get; set; }

    /// <summary>Set once the spelling is reconciled; the text is kept regardless.</summary>
    public int? SupplierId { get; set; }

    public string Carrier { get; set; } = "";
    public string Service { get; set; } = "";
    public string Customer { get; set; } = "";
    public string FromPlace { get; set; } = "";
    public string ToPlace { get; set; } = "";
    public string County { get; set; } = "";
    public string Remark { get; set; } = "";

    /// <summary>Source workbook, so a figure can be traced back to what the carrier sent.</summary>
    public string SourceFile { get; set; } = "";

    /// <summary>
    /// The rate-sheet lane this row was moved over from, when it was.
    ///
    /// Null for the great majority: those came off a carrier's own signed form
    /// and were never a quotation. Set, it is what makes a second move update
    /// this row instead of adding another one beside it — and what lets the New
    /// Transport Rate tab say which of its lanes have already been moved.
    /// </summary>
    public long? FromInquiryLaneId { get; set; }

    /// <summary>When it was moved, and by whom. Empty on a row that never was.</summary>
    public DateTime? PromotedAt { get; set; }

    public string PromotedBy { get; set; } = "";
}

/// <summary>One price: a lane, a vehicle type, a fuel band.</summary>
public class RatePrice
{
    public long Id { get; set; }
    public long LaneId { get; set; }
    public string Vehicle { get; set; } = "";
    public int BandPosition { get; set; }
    public int Price { get; set; }
}

/// <summary>The contract's extra charges — waiting time, cancellation, overnight.</summary>
public class RateSurcharge
{
    public int Id { get; set; }
    public string Service { get; set; } = "";
    public string No { get; set; } = "";
    public string Description { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Rate { get; set; } = "";
    public string Unit { get; set; } = "";
}

/* ------------------------------------------------------ AI permissions */

/// <summary>
/// What an AI tool is allowed to do.
///
/// Enforced at the tool layer, not in a prompt. A tool the assistant must never
/// call is a tool it is not given; a tool that needs sign-off returns a draft
/// and an approval id instead of writing. A model cannot be instructed out of a
/// capability it does not have.
/// </summary>
public class AiTool
{
    public int Id { get; set; }

    /// <summary>The tool name the agent calls, e.g. query_shipments.</summary>
    public string Name { get; set; } = "";

    /// <summary>Which agent owns it: operation · document · kpi · supplier · safety · management.</summary>
    public string Agent { get; set; } = "";

    /// <summary>allow · approval · deny — the permission matrix, as data.</summary>
    public string Permission { get; set; } = "deny";

    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Something the assistant proposed that a person has to agree to.
///
/// The payload is the exact change that would be made, so approving it applies
/// what was reviewed rather than re-running a model whose answer may differ.
/// </summary>
public class Approval
{
    public long Id { get; set; }

    public string Tool { get; set; } = "";
    public string Agent { get; set; } = "";

    /// <summary>What the assistant intends to do, in the user's language.</summary>
    public string Summary { get; set; } = "";

    /// <summary>The exact call arguments, as JSON.</summary>
    public string Payload { get; set; } = "";

    /// <summary>pending · approved · rejected · applied · expired</summary>
    public string State { get; set; } = "pending";

    public string RequestedBy { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }

    public string DecidedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    public string DecisionNote { get; set; } = "";

    /// <summary>What happened when it was applied, or the error if it failed.</summary>
    public string Result { get; set; } = "";
}
