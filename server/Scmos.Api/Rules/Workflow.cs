namespace Scmos.Api.Rules;

/// <summary>
/// The operational process, as the team actually runs it.
///
/// A booking arrives from CS, is checked, gets a carrier, gets its documents
/// verified, runs, and is closed. What makes this a workflow rather than a list
/// is the six points where it can fail and go backwards: the plan's own status
/// ladder is a straight line, and a straight line cannot say "this job is
/// parked because the B/L does not match the booking".
///
/// Every gate below is a real decision somebody makes, every hold is a real
/// place a job waits, and every return path is where it goes when the problem
/// is fixed. Nothing here is invented for symmetry.
/// </summary>
public enum Stage
{
    Received,
    Reviewed,
    DocumentVerification,
    SupplierSelection,
    CapacityRequested,
    SupplierAssigned,
    PreRunVerification,
    ECardReceived,
    DocumentCheck,
    DocumentReleased,
    Dispatched,
    PickedUp,
    Loading,
    InTransit,
    Delivered,
    ContainerReturned,
    PodCollected,
    BillingVerified,
    KpiCalculated,
    Closed,
}

/// <summary>Why a job is parked. A hold always names its reason — that is the point of it.</summary>
public enum HoldReason
{
    None,
    /// <summary>Booking information incomplete; returned to CS for correction.</summary>
    CsCorrection,
    /// <summary>Booking cannot be validated; being clarified with CS.</summary>
    CsClarification,
    /// <summary>The B/L does not match the booking. CS notified.</summary>
    BlMismatch,
    /// <summary>The document image cannot be read; a clear copy was asked for.</summary>
    ImageUnclear,
    /// <summary>An incident was raised; CAR/PAR is running.</summary>
    Incident,
}

/// <summary>A decision that has to be answered before the job can move on.</summary>
public record Gate(string Question, string Thai, HoldReason OnFail, Stage ReturnsTo);

public record StageInfo(Stage Stage, string English, string Thai, Gate? Gate);

public static class Workflow
{
    /// <summary>
    /// The flow in order. A stage with a gate cannot be passed without answering
    /// it; answering "no" parks the job with the named reason and, when the
    /// problem is resolved, it resumes at <c>ReturnsTo</c> rather than at the
    /// beginning — losing the work already done would be its own kind of error.
    /// </summary>
    public static readonly StageInfo[] Stages =
    [
        new(Stage.Received, "Receive booking from CS", "รับงานจาก CS", null),
        new(Stage.Reviewed, "Review booking information", "ตรวจข้อมูลการจอง",
            new Gate("Information complete?", "ข้อมูลครบหรือไม่", HoldReason.CsCorrection, Stage.Reviewed)),
        new(Stage.DocumentVerification, "Document verification", "ตรวจสอบเอกสาร",
            new Gate("Booking valid?", "การจองถูกต้องหรือไม่", HoldReason.CsClarification, Stage.DocumentVerification)),
        new(Stage.SupplierSelection, "Select trucking supplier", "เลือกผู้ขนส่ง", null),
        new(Stage.CapacityRequested, "Request truck capacity", "ขอกำลังรถ",
            // A carrier saying no is not a hold. It is the escalation loop: the
            // request is cancelled and the next carrier is asked, which is why
            // this gate returns to the stage before it rather than parking.
            new Gate("Supplier confirmed?", "ผู้ขนส่งยืนยันหรือไม่", HoldReason.None, Stage.SupplierSelection)),
        new(Stage.SupplierAssigned, "Assign supplier / truck", "มอบหมายรถและผู้ขนส่ง", null),
        new(Stage.PreRunVerification, "Pre-run verification", "ตรวจก่อนออกงาน", null),
        new(Stage.ECardReceived, "Card / E-Card received", "รับ E-Card", null),
        new(Stage.DocumentCheck, "Check B/L and document quality", "ตรวจ B/L และคุณภาพเอกสาร",
            new Gate("B/L match?", "B/L ตรงกับการจองหรือไม่", HoldReason.BlMismatch, Stage.DocumentCheck)),
        new(Stage.DocumentReleased, "Release document to transporter", "ส่งเอกสารให้ผู้ขนส่ง",
            new Gate("Image clear?", "ภาพเอกสารชัดหรือไม่", HoldReason.ImageUnclear, Stage.DocumentCheck)),
        new(Stage.Dispatched, "Dispatch", "จ่ายงาน", null),
        new(Stage.PickedUp, "Pickup", "รับตู้ / รับสินค้า", null),
        new(Stage.Loading, "Loading", "ขนถ่ายสินค้า", null),
        new(Stage.InTransit, "Transit", "ระหว่างขนส่ง", null),
        new(Stage.Delivered, "Delivery", "ส่งมอบ", null),
        new(Stage.ContainerReturned, "Container return", "คืนตู้", null),
        new(Stage.PodCollected, "POD / supporting documents", "เก็บ POD และเอกสารประกอบ", null),
        new(Stage.BillingVerified, "Billing verification", "ตรวจสอบการวางบิล", null),
        new(Stage.KpiCalculated, "KPI calculation", "คำนวณ KPI",
            new Gate("Incident?", "มีเหตุผิดปกติหรือไม่", HoldReason.Incident, Stage.KpiCalculated)),
        new(Stage.Closed, "Close job", "ปิดงาน", null),
    ];

    private static readonly Dictionary<Stage, int> Order =
        Stages.Select((info, index) => (info.Stage, index)).ToDictionary(x => x.Stage, x => x.index);

    public static StageInfo Info(Stage stage) => Stages[Order[stage]];

    public static int Position(Stage stage) => Order[stage];

    /// <summary>The stage after this one, or null at the end of the flow.</summary>
    public static Stage? Next(Stage stage)
    {
        var index = Order[stage] + 1;
        return index < Stages.Length ? Stages[index].Stage : null;
    }

    /// <summary>
    /// The KPI gate is the one place "no" is the good answer: no incident means
    /// the job closes, an incident sends it to CAR/PAR. Every other gate passes
    /// on "yes".
    /// </summary>
    public static bool PassMeansYes(Stage stage) => stage != Stage.KpiCalculated;

    /// <summary>
    /// Where the plan's own status puts a job on this flow.
    ///
    /// The register has 2,102 jobs that were worked long before the workflow was
    /// written down, and starting them all at "Received" would be false. Their
    /// status is the best evidence of where they actually are, so it is read as
    /// the starting position and the workflow takes over from there.
    /// </summary>
    public static Stage FromStatus(string status)
    {
        // A job written the old way is read through the same mapping the
        // migration uses, so the flow places it identically before and after.
        var code = JobStatus.IsValid("EXPORT", Formats.Clean(status))
            ? Formats.Clean(status).ToUpperInvariant()
            : JobStatus.FromLegacy(status);

        return code switch
        {
            JobStatus.Draft or JobStatus.Received => Stage.Received,
            JobStatus.Validating => Stage.DocumentVerification,
            JobStatus.WaitingCs => Stage.Reviewed,
            JobStatus.ReadyForBooking or JobStatus.WaitingSupplier => Stage.SupplierSelection,
            JobStatus.SupplierConfirmed => Stage.SupplierAssigned,
            JobStatus.TruckAssigned => Stage.SupplierAssigned,
            JobStatus.PreRun => Stage.PreRunVerification,
            JobStatus.Ready => Stage.DocumentReleased,
            JobStatus.Dispatched => Stage.Dispatched,
            JobStatus.PickedUp => Stage.PickedUp,
            JobStatus.Loading => Stage.Loading,
            JobStatus.InTransit => Stage.InTransit,
            JobStatus.Delivered => Stage.Delivered,
            JobStatus.ContainerReturned => Stage.ContainerReturned,
            JobStatus.DocumentPending => Stage.PodCollected,
            JobStatus.BillingPending => Stage.BillingVerified,
            JobStatus.Completed => Stage.Closed,
            // Hold and Cancelled say nothing about how far the job got, so they
            // start at the beginning rather than being guessed at.
            _ => Stage.Received,
        };
    }

    /// <summary>The status the plan should carry once a stage is reached, so the two agree.</summary>
    public static string? StatusFor(Stage stage, string category) => stage switch
    {
        Stage.Reviewed => JobStatus.WaitingCs,
        Stage.DocumentVerification => JobStatus.Validating,
        Stage.SupplierSelection => JobStatus.WaitingSupplier,
        Stage.CapacityRequested => JobStatus.WaitingSupplier,
        Stage.SupplierAssigned => JobStatus.SupplierConfirmed,
        Stage.PreRunVerification => JobStatus.PreRun,
        Stage.DocumentReleased => JobStatus.Ready,
        Stage.Dispatched => JobStatus.Dispatched,
        Stage.PickedUp => JobStatus.PickedUp,
        // LOADING is not on the import ladder — the box arrives full — so an
        // import passing this stage keeps the status it already had.
        Stage.Loading => JobStatus.IsValid(category, JobStatus.Loading) ? JobStatus.Loading : null,
        Stage.InTransit => JobStatus.InTransit,
        Stage.Delivered => JobStatus.Delivered,
        Stage.ContainerReturned => JobStatus.IsValid(category, JobStatus.ContainerReturned)
            ? JobStatus.ContainerReturned : null,
        Stage.PodCollected => JobStatus.DocumentPending,
        Stage.BillingVerified => JobStatus.BillingPending,
        Stage.Closed => JobStatus.Completed,
        _ => null,
    };
}
