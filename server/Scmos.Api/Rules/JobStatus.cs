namespace Scmos.Api.Rules;

/// <summary>
/// The controlled status set.
///
/// Free text made the register unanalysable: the July plan carries eight Thai
/// phrases meaning "waiting for a truck" and a dashboard cannot group them. Every
/// status a job may hold is now one of these codes, so a count is a count.
///
/// The codes are shared; the ladders are not. An import collects a laden
/// container and delivers it; an export collects an empty one and loads it at
/// the plant. LOADING is a real step for one and meaningless for the other, and
/// offering it on both is how a status set stops meaning anything.
/// </summary>
public static class JobStatus
{
    public const string Draft = "DRAFT";
    public const string Received = "RECEIVED";
    public const string Validating = "VALIDATING";
    public const string WaitingCs = "WAITING_CS";
    public const string ReadyForBooking = "READY_FOR_BOOKING";
    public const string WaitingSupplier = "WAITING_SUPPLIER";
    public const string SupplierConfirmed = "SUPPLIER_CONFIRMED";
    public const string TruckAssigned = "TRUCK_ASSIGNED";
    public const string PreRun = "PRE_RUN";
    public const string Ready = "READY";
    public const string Dispatched = "DISPATCHED";
    public const string PickedUp = "PICKED_UP";
    public const string Loading = "LOADING";
    public const string InTransit = "IN_TRANSIT";
    public const string Delivered = "DELIVERED";
    public const string ContainerReturned = "CONTAINER_RETURNED";
    public const string DocumentPending = "DOCUMENT_PENDING";
    public const string BillingPending = "BILLING_PENDING";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string Hold = "HOLD";

    /// <summary>Everything up to the truck leaving. Identical for every category.</summary>
    private static readonly string[] Booking =
    [
        Draft, Received, Validating, WaitingCs, ReadyForBooking,
        WaitingSupplier, SupplierConfirmed, TruckAssigned, PreRun, Ready, Dispatched,
    ];

    /// <summary>Everything after the goods are where they are going.</summary>
    private static readonly string[] Closing = [DocumentPending, BillingPending, Completed];

    /// <summary>Available on every job at any point.</summary>
    private static readonly string[] Exits = [Cancelled, Hold];

    /// <summary>
    /// The ladder a category may use.
    ///
    /// IMPORT has no LOADING: the box arrives full and is emptied at the
    /// customer, which DELIVERED covers. EXPORT has no separate delivery to a
    /// consignee — DELIVERED means the box reached the port — and its
    /// CONTAINER_RETURNED is the gate-in. DELIVERY moves goods, not boxes, so it
    /// has neither LOADING nor CONTAINER_RETURNED.
    /// </summary>
    public static readonly Dictionary<string, string[]> Ladder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IMPORT"] = [.. Booking, PickedUp, InTransit, Delivered, ContainerReturned, .. Closing, .. Exits],
        ["EXPORT"] = [.. Booking, PickedUp, Loading, InTransit, Delivered, ContainerReturned, .. Closing, .. Exits],
        ["DELIVERY"] = [.. Booking, PickedUp, InTransit, Delivered, .. Closing, .. Exits],
    };

    public static string[] For(string category) =>
        Ladder.TryGetValue(category, out var ladder) ? ladder : Ladder["IMPORT"];

    public static bool IsValid(string category, string status) =>
        For(category).Contains(status, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every code in the set, whichever ladder it appears on.</summary>
    public static readonly HashSet<string> All = new(Ladder["EXPORT"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a value is one of the controlled codes.
    ///
    /// The bucket tests need this: a known code must be answered from the code
    /// lists alone, never re-read by the legacy patterns. DELIVERED means the
    /// goods arrived and the paperwork has not, while the old free-text
    /// "Delivered" meant finished — letting the pattern see it would report
    /// running jobs as complete.
    /// </summary>
    public static bool IsControlled(string status) => All.Contains(status);

    /* ------------------------------------------------------------- buckets */

    /// <summary>Waiting on somebody else before a truck can even be asked for.</summary>
    public static bool IsWaiting(string status) => status is
        Draft or Received or Validating or WaitingCs or ReadyForBooking or WaitingSupplier;

    /// <summary>A carrier has taken it; the truck is being got ready.</summary>
    public static bool IsConfirmed(string status) => status is
        SupplierConfirmed or TruckAssigned or PreRun or Ready;

    /// <summary>On the road.</summary>
    public static bool IsRunning(string status) => status is
        Dispatched or PickedUp or Loading or InTransit or Delivered or ContainerReturned;

    /// <summary>
    /// Parked. The set has no DELAYED on purpose — a delay is a thing that
    /// happened to a job, recorded in delay_records with a category and an
    /// owner, not a place the job sits. A status that swallows the real position
    /// is how the July plan ended up with two delayed jobs and no idea what
    /// stage either had reached.
    /// </summary>
    public static bool IsHeld(string status) => status is Hold;

    public static bool IsDone(string status) => status is Completed;

    public static bool IsClosedOut(string status) => status is Completed or Cancelled;

    /* ------------------------------------------------------------- mapping */

    /// <summary>
    /// The old free-text status a job carries, as one of these codes.
    ///
    /// Used once to move the register and then whenever an import brings a
    /// workbook still written the old way. Anything unrecognised becomes DRAFT
    /// rather than being guessed at — a job at an unknown stage is a job
    /// somebody has to look at.
    /// </summary>
    public static string FromLegacy(string legacy)
    {
        var text = Formats.Clean(legacy).ToLowerInvariant();
        if (text.Length == 0) return Draft;

        return text switch
        {
            "new" => Received,
            "waiting information" => WaitingCs,
            "waiting truck" => WaitingSupplier,
            "scheduled" => WaitingSupplier,
            "truck confirmed" => SupplierConfirmed,
            "truck assigned" => TruckAssigned,
            "driver assigned" => TruckAssigned,
            "empty pickup" => PickedUp,
            "container pickup" or "pickup" => PickedUp,
            "arrived plant" => PickedUp,
            "loading" or "loading completed" => Loading,
            "departed port" or "departed plant" => InTransit,
            "in transit" => InTransit,
            "arrived customer" => Delivered,
            "delivery started" => Delivered,
            "port return" => Delivered,
            "empty return pending" => Delivered,
            "delivered" => Delivered,
            "empty returned" => ContainerReturned,
            "gate-in completed" => ContainerReturned,
            "delivery completed" => Completed,
            "completed" => Completed,
            "cancelled" => Cancelled,
            // Delayed says a job stopped but not where. It becomes a hold, which
            // is exactly what it is, and the two jobs carrying it need a person
            // to say which stage they actually reached.
            "delayed" => Hold,
            _ => Draft,
        };
    }
}
