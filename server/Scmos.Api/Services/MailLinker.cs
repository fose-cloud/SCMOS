using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// What a stored message says, which job it is about, and writing both down.
///
/// <para>
/// The last join in the pipeline. <see cref="EmailExtraction"/> reads the
/// identifiers, this looks them up in the register, <see cref="EmailMatching"/>
/// weighs the answer, and <see cref="MailLinkPlan"/> decides what to write given
/// what is already there. Every one of those is pure and checked on its own;
/// this is the part that touches the database, and it is kept to exactly that.
/// </para>
///
/// <para>
/// <b>Re-running must be a no-op.</b> A message is matched more than once — an
/// abandoned claim goes back in the queue, the catch-up finds mail the webhook
/// already brought — so the second pass has to leave what the first did, except
/// where somebody has since confirmed or rejected a link. Those are left
/// untouched: a confirmation overwritten by a fresh guess is a decision that
/// silently stopped counting.
/// </para>
/// </summary>
public sealed class MailLinker(ScmosDbContext db, ILogger<MailLinker> log)
{
    /// <summary>What one pass did, for the worker's status and for the log.</summary>
    /// <param name="Found">Identifiers the message names.</param>
    /// <param name="Linked">Jobs attached without asking.</param>
    /// <param name="Suggested">Jobs offered for somebody to confirm.</param>
    /// <param name="NeedsAPerson">Whether anything is left for somebody to decide.</param>
    public record Result(int Found, int Linked, int Suggested, bool NeedsAPerson);

    /// <summary>
    /// Read one stored message, and attach it to the job it is about.
    /// </summary>
    public async Task<Result> LinkAsync(Email message, CancellationToken token)
    {
        var found = EmailExtraction.Read(message.Subject, message.BodyText.Length > 0
            ? message.BodyText
            : message.BodyHtml);

        await StoreEntitiesAsync(message.Id, found, token);

        // Every distinct value once, however many times the message said it.
        // Two mentions of one container are not two pieces of evidence.
        var evidence = new List<EmailMatching.Evidence>();
        foreach (var one in found)
            evidence.Add(new(one, await JobsHoldingAsync(one.Type, one.Value, token)));

        var matches = EmailMatching.Match(evidence);
        await ApplyAsync(message.Id, matches, token);

        var linked = matches.Count(one => one.Decision == EmailMatching.Decision.Link);
        var suggested = matches.Count(one => one.Decision == EmailMatching.Decision.Suggest);
        return new(found.Count, linked, suggested, MailLinkPlan.NeedsAPerson(matches));
    }

    /// <summary>
    /// Write what the message names, once each.
    ///
    /// <para>
    /// Kept rather than recomputed, so a link can be explained months later
    /// against the rules as they were and not as they have since become. The
    /// unique index on (email, kind, value) is the guard; this reads first only
    /// to keep the ordinary re-run off the exception path.
    /// </para>
    /// </summary>
    private async Task StoreEntitiesAsync(long emailId, IReadOnlyList<EmailExtraction.Found> found,
        CancellationToken token)
    {
        var already = await db.EmailEntities.AsNoTracking()
            .Where(one => one.EmailId == emailId)
            .Select(one => one.Kind + "|" + one.Value)
            .ToListAsync(token);
        var held = new HashSet<string>(already, StringComparer.Ordinal);

        var added = 0;
        foreach (var one in found)
        {
            if (!held.Add(one.Type + "|" + one.Value)) continue;
            db.EmailEntities.Add(new EmailEntity
            {
                EmailId = emailId,
                Kind = one.Type,
                Value = one.Value,
                InSubject = one.InSubject,
                Labelled = one.Labelled,
                WellFormed = one.WellFormed,
            });
            added++;
        }

        if (added == 0) return;
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            // Two passes over the same message at once. The index settled it,
            // and losing that race leaves exactly the rows we wanted anyway.
            db.ChangeTracker.Clear();
            log.LogInformation("Mail entities for message {Id} were written by another pass", emailId);
        }
    }

    /// <summary>
    /// Which jobs the register holds this value against.
    ///
    /// <para>
    /// Three of the four are promoted columns and are looked up directly. Only
    /// the booking lives inside the stored JSON, so it goes through
    /// <c>JSON_VALUE</c> — wrapped in <c>ISJSON</c>, because a row whose data
    /// will not parse makes <c>JSON_VALUE</c> raise and this register has such
    /// rows. The rest of the codebase skips those rather than failing the read,
    /// and so does this.
    /// </para>
    ///
    /// <para>
    /// A scan, for that one. The register is a few thousand rows and a mailbox
    /// delivers a message at a time, so the cost is a few milliseconds where a
    /// promoted column would be a schema change; if the mail volume ever makes
    /// that wrong, the fix is to promote the column, not to stop matching on it.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<string>> JobsHoldingAsync(string kind, string value,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        return kind switch
        {
            EmailExtraction.Kind.Container => await db.OperationJobs.AsNoTracking()
                .Where(one => one.Container == value).Select(one => one.Key).ToListAsync(token),

            EmailExtraction.Kind.JobCode => await db.OperationJobs.AsNoTracking()
                .Where(one => one.JobCode == value).Select(one => one.Key).ToListAsync(token),

            EmailExtraction.Kind.Booking => await ByJsonAsync("booking", value, token),

            // Against the container column, because that is where they are.
            // The register has no bill-of-lading field at all — EmailMatching
            // records that some sit in the container column, which is exactly
            // why a B/L is the weakest of the four at 0.75. Looking one up
            // against a field that does not exist would have matched nothing,
            // for ever, and read as mail about no job we hold.
            EmailExtraction.Kind.BillOfLading => await db.OperationJobs.AsNoTracking()
                .Where(one => one.Container == value).Select(one => one.Key).ToListAsync(token),

            _ => [],
        };
    }

    /// <summary>A field inside the stored job, matched exactly.</summary>
    private async Task<IReadOnlyList<string>> ByJsonAsync(string field, string value,
        CancellationToken token)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(token);

        var keys = new List<string>();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 30;
            // The field name is not a parameter — SQL Server will not take one
            // in a JSON path — so it never comes from anything but the two
            // literals above. The value, which does come from an email, is a
            // parameter.
            command.CommandText = $"""
                SELECT [key]
                FROM operation_jobs
                WHERE ISNULL(CASE WHEN ISJSON(data) = 1
                                  THEN JSON_VALUE(data, '$.{field}') END, '') = @value
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@value";
            parameter.Value = value;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) keys.Add(reader.GetString(0));
        }
        catch (SqlException problem)
        {
            // A lookup that failed is not a match of none. Said out loud, so a
            // message that quietly stopped matching on its booking number is
            // visible rather than looking like mail about nothing.
            log.LogError(problem, "Could not look up jobs by {Field}", field);
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
        return keys;
    }

    /// <summary>
    /// Write the links this pass implies, leaving anybody's decisions alone.
    /// </summary>
    private async Task ApplyAsync(long emailId, IReadOnlyList<EmailMatching.JobMatch> matches,
        CancellationToken token)
    {
        var rows = await db.EmailJobLinks.Where(one => one.EmailId == emailId).ToListAsync(token);
        var byJob = rows.ToDictionary(one => one.JobKey, StringComparer.Ordinal);

        var plan = MailLinkPlan.For(
            rows.Select(one => new MailLinkPlan.Existing(one.JobKey, one.Status)).ToList(), matches);

        var now = DateTimeOffset.UtcNow;
        var changed = 0;

        foreach (var change in plan)
        {
            switch (change.Action)
            {
                case MailLinkPlan.Do.Add when change.Match is { } match:
                    db.EmailJobLinks.Add(new EmailJobLink
                    {
                        EmailId = emailId,
                        JobKey = match.JobKey,
                        MatchedOn = match.MatchedOn,
                        MatchedValue = match.MatchedValue,
                        Confidence = match.Confidence,
                        // A link the machine made without asking is still the
                        // machine's until somebody agrees, which is what keeps
                        // an automatic link and an agreed one tellable apart.
                        Status = MailLink.Suggested,
                        CreatedAt = now,
                    });
                    changed++;
                    break;

                case MailLinkPlan.Do.Update when change.Match is { } match && byJob.TryGetValue(change.JobKey, out var row):
                    row.MatchedOn = match.MatchedOn;
                    row.MatchedValue = match.MatchedValue;
                    row.Confidence = match.Confidence;
                    changed++;
                    break;

                case MailLinkPlan.Do.Remove when byJob.TryGetValue(change.JobKey, out var stale):
                    db.EmailJobLinks.Remove(stale);
                    changed++;
                    break;
            }
        }

        if (changed == 0) return;
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            log.LogInformation("Mail links for message {Id} were written by another pass", emailId);
        }
    }
}
