using Microsoft.EntityFrameworkCore;
using Scmos.Api.Ai;

namespace Scmos.Api.Data;

/// <summary>
/// What the AI audit holds, with <c>--report-ai-audit</c> — Phase 9's
/// retention gate.
///
/// <para>
/// The audit is the reason the platform may read the register at all: every
/// run is a row, and nothing is released that was not recorded. That makes
/// it the one AI table that grows without an owner, and a retention rule
/// has to be a decision somebody takes rather than a default somebody
/// discovers. This measures what there is to decide about — how many runs,
/// how far back, by whom, how they ended, how much of it is tokens — and
/// changes nothing.
/// </para>
///
/// <para>
/// <b>It reads and prints. There is no --apply, and this file will never
/// delete an audit row.</b> A run that cannot be shown to have happened is
/// the failure this whole platform is built to avoid; deleting one is a
/// decision for the department, taken on what this prints, and carried out
/// as its own reviewed change.
/// </para>
/// </summary>
public static class AiAuditReport
{
    public static async Task<int> RunAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        var now = DateTimeOffset.UtcNow;

        var rows = await db.AiAuditLogs.AsNoTracking().CountAsync();
        Console.WriteLine();
        Console.WriteLine("The AI execution audit — what is there, and how far back.");
        Console.WriteLine();
        if (rows == 0)
        {
            Console.WriteLine("  No rows. Nothing has been asked of the platform on this database.");
            return 0;
        }

        var oldest = await db.AiAuditLogs.AsNoTracking().MinAsync(row => row.At);
        var newest = await db.AiAuditLogs.AsNoTracking().MaxAsync(row => row.At);
        var runs = await db.AiAuditLogs.AsNoTracking().Select(row => row.RunId).Distinct().CountAsync();
        var people = await db.AiAuditLogs.AsNoTracking().Select(row => row.UserId).Distinct().CountAsync();
        Console.WriteLine($"  {rows} row(s) · {runs} run(s) · {people} account(s) · {Stamp(oldest)} to {Stamp(newest)} ({(int)(now - oldest).TotalDays} days)");

        var completed = await db.AiAuditLogs.AsNoTracking().Where(row => row.Event == "run_completed")
            .GroupBy(row => new { row.AgentId, row.Status })
            .Select(group => new { group.Key.AgentId, group.Key.Status, Count = group.Count() })
            .ToListAsync();
        var started = await db.AiAuditLogs.AsNoTracking().Where(row => row.Event == "run_started")
            .GroupBy(row => row.AgentId).Select(group => new { AgentId = group.Key, Count = group.Count() }).ToListAsync();

        Console.WriteLine();
        Console.WriteLine("  Runs by agent, and how they ended:");
        foreach (var agent in started.OrderByDescending(one => one.Count))
        {
            var ends = completed.Where(one => one.AgentId == agent.AgentId).OrderByDescending(one => one.Count).ToList();
            var ended = ends.Sum(one => one.Count);
            Console.WriteLine($"   {agent.Count,6}  {agent.AgentId,-20} {string.Join(" · ", ends.Select(one => $"{one.Status} {one.Count}"))}"
                + (agent.Count > ended ? $" · ไม่จบ {agent.Count - ended}" : ""));
        }

        // A run that started and never completed is the shape that matters: it means a container
        // went away mid-run, and the reader shows it as incomplete rather than guessing success.
        var incomplete = started.Sum(one => one.Count) - completed.Sum(one => one.Count);
        var tokensIn = await db.AiAuditLogs.AsNoTracking().SumAsync(row => (long?)row.InputTokens) ?? 0;
        var tokensOut = await db.AiAuditLogs.AsNoTracking().SumAsync(row => (long?)row.OutputTokens) ?? 0;
        Console.WriteLine();
        Console.WriteLine($"  Incomplete runs: {incomplete} · tokens recorded: {tokensIn} in, {tokensOut} out");

        var months = (await db.AiAuditLogs.AsNoTracking()
            .Select(row => new { row.At })
            .ToListAsync())
            .GroupBy(row => row.At.ToOffset(Rules.Formats.Zone).ToString("yyyy-MM"))
            .OrderBy(group => group.Key)
            .Select(group => new { Month = group.Key, Count = group.Count() })
            .ToList();
        Console.WriteLine();
        Console.WriteLine("  Rows by month (Bangkok):");
        foreach (var month in months) Console.WriteLine($"   {month.Count,6}  {month.Month}");

        var keys = await db.AiAuditLogs.AsNoTracking().SumAsync(row => (long)row.SourceKeys.Length);
        Console.WriteLine();
        Console.WriteLine($"  Evidence identifiers stored: {keys / 1024} KB of job keys and row ids — no register content, no message text, no secret.");
        Console.WriteLine();
        var perDay = rows / Math.Max((now - oldest).TotalDays, 1);
        Console.WriteLine($"  Retention is the department's to set. Nothing here deletes a row: the audit is what lets a run be shown to have happened,");
        Console.WriteLine($"  and a run that cannot be shown is the failure the platform is built to avoid. At {perDay:0.#} row(s) a day, a year is about {perDay * 365:0} rows;");
        Console.WriteLine($"  the decision this report is for is how many months to keep and where the rest goes.");
        Console.WriteLine();
        app.Logger.LogInformation("AI audit report: {Rows} rows, {Runs} runs, {Days} days, {Incomplete} incomplete",
            rows, runs, (int)(now - oldest).TotalDays, incomplete);
        return 0;
    }

    private static string Stamp(DateTimeOffset at) => at.ToOffset(Rules.Formats.Zone).ToString("dd/MM/yyyy HH:mm");
}
