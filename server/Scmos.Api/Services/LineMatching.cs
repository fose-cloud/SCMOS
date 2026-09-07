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
    /// <param name="jobNumber">The twelve digits the parser read.</param>
    /// <param name="status">The status it read, or empty.</param>
    public static async Task<LineAuthority.LineDecision> DecideAsync(
        ScmosDbContext db,
        string lineGroupId,
        string jobNumber,
        string status,
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
        var candidates = group.Known
            && group.Active
            && string.Equals(group.GroupType, LineGroupType.Vendor, StringComparison.OrdinalIgnoreCase)
            && jobNumber.Length > 0
                ? await CandidatesAsync(db, jobNumber, token)
                : [];

        return LineAuthority.Decide(group, status, candidates);
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

        return new(Known: true, Active: group.IsActive, group.GroupType, name);
    }

    /// <summary>
    /// Every row carrying this job number — not filtered by carrier here.
    ///
    /// The rule does the filtering, because "the number exists but is somebody
    /// else's" and "the number does not exist" are different answers and only
    /// the unfiltered set can tell them apart.
    /// </summary>
    private static async Task<List<LineAuthority.JobCandidate>> CandidatesAsync(
        ScmosDbContext db, string jobNumber, CancellationToken token) =>
        await db.OperationJobs.AsNoTracking()
            .Where(one => one.JobCode == jobNumber)
            .Select(one => new LineAuthority.JobCandidate(one.Key, one.Cat, one.Trucker, one.Status))
            .ToListAsync(token);
}
