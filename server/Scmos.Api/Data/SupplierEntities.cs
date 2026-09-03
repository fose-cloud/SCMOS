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
