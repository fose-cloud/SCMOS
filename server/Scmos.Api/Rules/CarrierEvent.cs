using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scmos.Api.Rules;

/// <summary>
/// A status event a carrier's TMS reports on a job — Phase 3 of the Carrier
/// TMS API (20 Sep 2026): the truck left, picked up, is loading, is on the
/// road, is at the site, delivered, returned the box; or a note.
///
/// <para>
/// An event is not written to the register. It is queued exactly as a
/// haulier's LINE message is: a <c>line_events</c> row of type
/// <see cref="MessageType"/>, judged by the same <see cref="LineAuthority"/>
/// — forward only on the job's own ladder, "ถึงโรงงาน" resolved by category,
/// the arrival clock into empty cells — and approved by the job's owner
/// from My Job or the LINE screen. What the ladder has no rung for, a note,
/// goes into REMARK at once, dated, the way a haulier's "ติดต่อแถวอยู่ลานดิน"
/// does. Auto-apply for a named carrier is a later phase behind a flag.
/// </para>
///
/// <para>Pure: the body in, a payload and a reading out.</para>
/// </summary>
public static class CarrierEvent
{
    /// <summary>What a TMS event is stored as in <c>line_events.message_type</c>.</summary>
    public const string MessageType = "tms";

    public const string Dispatched = "dispatched";
    public const string PickedUp = "picked_up";
    public const string Loading = "loading";
    public const string InTransit = "in_transit";
    public const string Arrived = "arrived";
    public const string Delivered = "delivered";
    public const string ContainerReturned = "container_returned";
    public const string Note = "note";

    public static readonly string[] Types =
        [Dispatched, PickedUp, Loading, InTransit, Arrived, Delivered, ContainerReturned, Note];

    /// <summary>How far ahead of now an event may be stamped — clock drift, not a forecast.</summary>
    public static readonly TimeSpan MaxAhead = TimeSpan.FromMinutes(10);

    /// <summary>How far back an event may be stamped — a TMS catching up after an outage, not a rewrite of last month.</summary>
    public static readonly TimeSpan MaxBehind = TimeSpan.FromDays(7);

    public const int MaxRemark = 400;

    /// <summary>
    /// The status the event reports, as the parser would name it — "arrived"
    /// is <see cref="LineParser.SiteArrival"/>, which the job's category then
    /// settles into DELIVERED or DISPATCHED. Null for a note.
    /// </summary>
    public static string? StatusOf(string type) => type switch
    {
        Dispatched => JobStatus.Dispatched,
        PickedUp => JobStatus.PickedUp,
        Loading => JobStatus.Loading,
        InTransit => JobStatus.InTransit,
        Arrived => LineParser.SiteArrival,
        Delivered => JobStatus.Delivered,
        ContainerReturned => JobStatus.ContainerReturned,
        _ => null,
    };

    /// <summary>The event as the screens say it.</summary>
    public static string Label(string type) => type switch
    {
        Dispatched => "รถออกแล้ว",
        PickedUp => "รับตู้/รับสินค้าแล้ว",
        Loading => "กำลังขนถ่าย",
        InTransit => "ระหว่างขนส่ง",
        Arrived => "ถึงโรงงาน",
        Delivered => "ส่งเสร็จ",
        ContainerReturned => "คืนตู้แล้ว",
        Note => "หมายเหตุ",
        _ => type,
    };

    /// <summary>The type out of a body — case and dashes forgiven — or false.</summary>
    public static bool TryType(string? text, out string type)
    {
        type = (text ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return Types.Contains(type, StringComparer.Ordinal);
    }

    /// <summary>
    /// When the event happened: the body's <c>at</c> as ISO 8601 with an
    /// offset, or now when absent. Refused ahead of now by more than
    /// <see cref="MaxAhead"/> or behind by more than <see cref="MaxBehind"/>,
    /// and when it cannot be read.
    /// </summary>
    public static (DateTimeOffset? At, string? Problem) ReadAt(string? text, DateTimeOffset now)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0) return (now, null);
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var at))
            return (null, "at must be ISO 8601 with an offset, e.g. 2026-09-20T10:20:00+07:00");
        if (at > now + MaxAhead) return (null, "at is in the future");
        if (at < now - MaxBehind) return (null, $"at is more than {MaxBehind.TotalDays:0} days ago");
        return (at, null);
    }

    /// <summary>
    /// The event as the row keeps it — in <c>line_events.raw_payload</c>, the
    /// way the webhook keeps a LINE event — and as every reader reads it
    /// back. The supplier is the key's, settled when the event came in; the
    /// approval re-judges the job against it, never against the body.
    /// </summary>
    public sealed record Payload(
        string ClientId, string ClientName, int SupplierId, string SupplierName,
        string JobKey, string Type, DateTimeOffset At, string Remark, string CorrelationId)
    {
        [JsonIgnore] public string Source => "carrier-api";

        /// <summary>The status the event reports, or empty for a note.</summary>
        [JsonIgnore] public string Status => StatusOf(Type) ?? "";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        public static Payload? Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<Payload>(json, Json); }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// The event as the parser would have read a message saying the same:
        /// the status, the moment, and — for "arrived" — the arrival clock the
        /// approval writes into empty cells. No plates, no number, no box: a
        /// TMS gives the truck's details through the truck route, not here.
        /// </summary>
        public LineParser.Parsed AsParsed() => new(
            JobNumber: null,
            Status: Status.Length > 0 ? Status : null,
            // Bangkok, whatever offset the TMS wrote: the arrival is written
            // into the register as a local date and clock.
            EventTime: At.ToOffset(TimeSpan.FromHours(7)),
            Eta: null,
            Delayed: false,
            DelayCategory: null,
            DelayBasis: null,
            Plate: null,
            Remark: Remark,
            Confidence: 1,
            MatchedRules: [$"carrier-api:{Type}"],
            Warnings: [],
            ArrivalTime: Type == Arrived ? At.ToOffset(TimeSpan.FromHours(7)) : null);

        /// <summary>One line for the queue and the drawer: "TMS SHORE: ถึงโรงงาน 20/09/2026 10:20 · …".</summary>
        public string Text()
        {
            var here = At.ToOffset(TimeSpan.FromHours(7));
            var line = $"TMS {SupplierName}: {Label(Type)} {here:dd/MM/yyyy HH:mm}";
            return Remark.Length > 0 ? $"{line} · {Remark}" : line;
        }

        /// <summary>The room's name, as the screens show it beside a LINE row: the supplier and the credential.</summary>
        public string GroupLabel => $"{SupplierName} · {ClientName}";
    }

    /// <summary>A row's message id — unique, as the webhook's are; the ledger already keeps a retried request from making a second row.</summary>
    public static string MessageId(long clientRowId) => $"tms:{clientRowId}:{Guid.NewGuid():N}";

    /// <summary>How a queued row's state reads to the TMS that sent it.</summary>
    public static string StateOf(string processingStatus, string errorCode) => processingStatus switch
    {
        Data.LineProcessing.NeedReview => "queued",
        Data.LineProcessing.Processing => "queued",
        Data.LineProcessing.Processed => errorCode == LineRemark.Written ? "remark-written" : "applied",
        Data.LineProcessing.Ignored => errorCode.Length > 0 ? errorCode : "not-applied",
        _ => processingStatus.ToLowerInvariant(),
    };
}
