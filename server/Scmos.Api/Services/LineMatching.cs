using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Looks up what <see cref="LineAuthority"/> needs, and asks it.
///
/// <para>
/// The split is deliberate. Everything that decides anything is in the rule,
/// where <c>--check-line</c> proves it with no database; everything here is
/// three queries. A rule that reached for a DbContext would be a rule nobody
/// could check.
/// </para>
///
/// <para>
/// Static rather than a registered service, so the review screen can call it
/// when an operator asks "what would this message do?" without the worker and
/// the endpoint answering differently.
/// </para>
/// </summary>
public static class LineMatching
{
    /// <summary>
    /// What the message would do, if anything.
    /// </summary>
    /// <param name="db">A context the caller owns.</param>
    /// <param name="lineGroupId">The room the message came from.</param>
    /// <param name="read">What the parser made of the message — the number, the box, the plates, the status.</param>
    /// <param name="receivedAt">When the message arrived, for the day the trip is on.</param>
    public static async Task<LineAuthority.LineDecision> DecideAsync(
        ScmosDbContext db,
        string lineGroupId,
        LineParser.Parsed read,
        DateTimeOffset receivedAt,
        CancellationToken token)
    {
        var group = await SpeakerAsync(db, lineGroupId, token);

        /*
         * The register is not touched until the speaker is settled.
         *
         * The same order the rule asks its questions in, and for the same
         * reason: an unidentified room should not cause a lookup against the
         * operational record at all. Passing an empty list here is not a
         * shortcut — the rule returns on the speaker before it reads it.
         */
        var may = group.Known
            && group.Active
            && string.Equals(group.GroupType, LineGroupType.Vendor, StringComparison.OrdinalIgnoreCase)
            && read.HasReference;
        var (candidates, named) = may
            ? await CandidatesAsync(db, read, token)
            : ([], "เลขงานนี้");

        var clue = new LineAuthority.Clue(
            named, read.Container, read.Plates ?? [], read.Remark,
            DateOnly.FromDateTime(receivedAt.ToOffset(TimeSpan.FromHours(7)).DateTime),
            PlateOnly: read.JobNumber is null && read.Container is null && (read.References ?? []).Count == 0,
            Details: read.HasDetails,
            BoxCount: read.BoxCount,
            Arrival: read.ArrivalTime is not null,
            Fills: LineAuthority.FillsOf(read));
        return LineAuthority.Decide(group, read.Status, candidates, clue);
    }

    /// <summary>
    /// The room, and the supplier it speaks for.
    ///
    /// A mapping with no supplier behind it counts as unknown rather than as a
    /// vendor with an empty name: an empty name matches no carrier, but saying
    /// "not your job" would blame the vendor for a row somebody forgot to
    /// finish setting up.
    /// </summary>
    private static async Task<LineAuthority.SpeakerGroup> SpeakerAsync(
        ScmosDbContext db, string lineGroupId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(lineGroupId))
            return new(Known: false, Active: false, "", "");

        var group = await db.LineGroups.AsNoTracking()
            .Where(one => one.LineGroupId == lineGroupId)
            .Select(one => new { one.SupplierId, one.GroupType, one.IsActive })
            .FirstOrDefaultAsync(token);

        if (group is null) return new(Known: false, Active: false, "", "");

        var name = await db.Suppliers.AsNoTracking()
            .Where(one => one.Id == group.SupplierId)
            .Select(one => one.Name)
            .FirstOrDefaultAsync(token);

        if (string.IsNullOrWhiteSpace(name))
            return new(Known: false, Active: false, group.GroupType, "");

        // The register's other spellings of this haulier — the same table the
        // carrier directory and the scorecard resolve names through.
        var aliases = await db.SupplierAliases.AsNoTracking()
            .Where(one => one.SupplierId == (int)group.SupplierId)
            .Select(one => one.Alias)
            .ToListAsync(token);

        return new(Known: true, Active: group.IsActive, group.GroupType, name, aliases);
    }

    /// <summary>
    /// Every row the message could mean — not filtered by carrier here.
    ///
    /// The rule does the filtering, because "the number exists but is somebody
    /// else's" and "the number does not exist" are different answers and only
    /// the unfiltered set can tell them apart.
    ///
    /// <para>
    /// By the job number when there is one; else by the container, which is a
    /// column; else by the plate, which lives in the job's JSON. The plate
    /// lookup is a text search on that JSON for the plate's digits and then a
    /// proper comparison in memory — a truck runs many trips, so the rows come
    /// back newest first and capped, and the rule's date tiebreak picks the
    /// one this message is about.
    /// </para>
    /// </summary>
    /// <returns>The rows, and what they were looked up by, for the sentence a person reads.</returns>
    private static async Task<(List<LineAuthority.JobCandidate> Rows, string Named)> CandidatesAsync(
        ScmosDbContext db, LineParser.Parsed read, CancellationToken token)
    {
        if (read.JobNumber is { } number)
            return (await ToCandidates(db.OperationJobs.AsNoTracking()
                .Where(one => one.JobCode == number), token), $"เลขงาน {number}");

        if (read.Container is { } container)
            return (await ToCandidates(db.OperationJobs.AsNoTracking()
                .Where(one => one.Container.Contains(container)), token), $"ตู้ {container}");

        // A booking, an ABS, a D-code, a delivery note: looked for in the
        // job's JSON by text, then held against the fields the register
        // actually keeps a reference in — a number that merely appears
        // somewhere in a row is not that row's reference.
        var references = read.References ?? [];
        var byReference = new List<LineAuthority.JobCandidate>();
        foreach (var reference in references.Take(3))
        {
            var found = await ToCandidates(db.OperationJobs.AsNoTracking()
                .Where(one => one.JobCode == reference || one.Data.Contains(reference))
                .OrderByDescending(one => one.UpdatedAt)
                .Take(100), token);
            byReference.AddRange(found.Where(one => one.References.Contains(reference, StringComparer.OrdinalIgnoreCase)
                && byReference.All(had => had.Key != one.Key)));
        }
        if (byReference.Count > 0)
            return (byReference, $"เลขอ้างอิง {string.Join(" / ", references.Take(3))}");

        var plates = read.Plates ?? [];
        var rows = new List<LineAuthority.JobCandidate>();
        foreach (var plate in plates)
        {
            // The digits after the dash are what a LIKE can find; the whole
            // plate is compared afterwards, province and punctuation aside.
            var digits = new string(plate.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            if (digits.Length < 3) continue;
            var found = await ToCandidates(db.OperationJobs.AsNoTracking()
                .Where(one => one.Data.Contains(digits))
                .OrderByDescending(one => one.UpdatedAt)
                .Take(200), token);
            rows.AddRange(found.Where(one => LineAuthority.PlateMatches(one.Plate, [plate])
                && rows.All(had => had.Key != one.Key)));
        }
        if (plates.Count > 0) return (rows, $"ทะเบียน {string.Join(" / ", plates)}");
        return (rows, $"เลขอ้างอิง {string.Join(" / ", references.Take(3))}");
    }

    /// <summary>
    /// The columns the rule reads, plus the plate out of the JSON. Only the
    /// matched rows are materialised, so reading their JSON is cheap.
    /// </summary>
    private static async Task<List<LineAuthority.JobCandidate>> ToCandidates(
        IQueryable<OperationJob> rows, CancellationToken token)
    {
        var picked = await rows
            .Select(one => new { one.Key, one.Cat, one.Trucker, one.Status, one.Customer, one.Container, one.WorkDate, one.Data, one.JobCode })
            .ToListAsync(token);
        return picked.Select(one => Candidate(one.Key, one.Cat, one.Trucker, one.Status, one.Data,
            one.Customer, one.Container, one.WorkDate, one.JobCode)).ToList();
    }

    /// <summary>
    /// One row as the rule sees it: the columns, the plate and the other
    /// cells a message can fill out of the JSON, and the references a
    /// haulier might quote it by. The approval builds its candidate here
    /// too, so "already on the job" is judged on the same cells both times.
    /// </summary>
    public static LineAuthority.JobCandidate Candidate(string key, string cat, string trucker, string status, string data,
        string customer = "", string container = "", string workDate = "", string jobCode = "")
    {
        var fields = FieldsOf(data, [.. FillCells, .. ReferenceFields]);
        return new LineAuthority.JobCandidate(
            key, cat, trucker, status, customer, container,
            fields.GetValueOrDefault(LineAuthority.Cells.Licence, ""), workDate,
            References: [.. ReferenceFields.Select(name => fields.GetValueOrDefault(name, ""))
                .Append(jobCode)
                .Select(value => value.Trim().ToUpperInvariant())
                .Where(value => value.Length > 0)],
            ArrDate: fields.GetValueOrDefault(LineAuthority.Cells.ArrDate, ""),
            ArrTime: fields.GetValueOrDefault(LineAuthority.Cells.ArrTime, ""),
            Driver: fields.GetValueOrDefault(LineAuthority.Cells.Driver, ""),
            Contact: fields.GetValueOrDefault(LineAuthority.Cells.Contact, ""),
            Seal: fields.GetValueOrDefault(LineAuthority.Cells.Seal, ""));
    }

    /// <summary>The JSON cells a message fills, read for the decision.</summary>
    private static readonly string[] FillCells =
        [LineAuthority.Cells.Licence, LineAuthority.Cells.Driver, LineAuthority.Cells.Contact,
         LineAuthority.Cells.Seal, LineAuthority.Cells.ArrDate, LineAuthority.Cells.ArrTime];

    /// <summary>
    /// The cells a haulier might quote a job by. The booking and the ABS on
    /// an import or export, the D-code, job number, SID, TMS id and customer
    /// PO on a Domestic run, the delivery note and SAP order on any.
    /// </summary>
    private static readonly string[] ReferenceFields =
        ["booking", "abs", "jobCode", "jobNo", "sid", "dCode", "tmsId", "customerPo", "deliverNo", "sapOrder"];

    /// <summary>The named string cells out of a job's JSON; a cell that is not there or not a string is absent.</summary>
    private static Dictionary<string, string> FieldsOf(string data, IReadOnlyList<string> names)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var json = JsonDocument.Parse(data);
            foreach (var name in names)
            {
                if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    found[name] = value.GetString() ?? "";
            }
        }
        catch (JsonException) { }
        return found;
    }
}
