using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// One reading of a queued row, whatever brought it: a haulier's LINE
/// message is parsed again, with the box its photos gave; a carrier's TMS
/// event is read back from the payload it was stored with. The review
/// screen, the job drawer and the approval all go through here, so a row
/// is judged the same way each time it is looked at.
/// </summary>
public static class LineReadings
{
    /// <summary>Whether the row came through the Carrier TMS API rather than a LINE room.</summary>
    public static bool IsTms(LineEvent row) => row.MessageType == CarrierEvent.MessageType;

    /// <summary>The TMS event on the row, or null for a LINE row or a payload that will not read.</summary>
    public static CarrierEvent.Payload? EventOf(LineEvent row) =>
        IsTms(row) ? CarrierEvent.Payload.Read(row.RawPayload) : null;

    /// <summary>What the row says, as the rules read it.</summary>
    public static LineParser.Parsed Of(LineEvent row) =>
        EventOf(row) is { } ev
            ? ev.AsParsed()
            : LinePhotoPairing.WithPhoto(LineParser.Parse(row.RawText, row.ReceivedAt), row);

    /// <summary>
    /// What the row would do, judged now: a LINE row through its room, a
    /// TMS row through the key's supplier and the one job it named.
    /// </summary>
    public static Task<LineAuthority.LineDecision> DecideAsync(ScmosDbContext db, LineEvent row,
        LineParser.Parsed read, CancellationToken token) =>
        EventOf(row) is { } ev
            ? LineMatching.DecideForJobAsync(db, ev.SupplierId, ev.JobKey, read, row.ReceivedAt, token)
            : LineMatching.DecideAsync(db, row.LineGroupId, read, row.ReceivedAt, token);

    /// <summary>The audit source a write from this row is recorded under.</summary>
    public static string SourceOf(LineEvent row) => IsTms(row) ? EventSource.CarrierApi : EventSource.Line;

    /// <summary>"LINE: …" or "TMS: …" — the reason an approval writes when the approver gives none.</summary>
    public static string ReasonOf(LineEvent row) => $"{(IsTms(row) ? "TMS" : "LINE")}: {row.RawText}";
}
