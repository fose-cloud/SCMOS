using Microsoft.EntityFrameworkCore;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Links old issues to their jobs by the container number already on the row.
///
/// The resolver only ever searched the "งานที่เกี่ยวข้อง" field. Operators put
/// the shipping line's booking number there — SWELCHNSA26090001 on the row that
/// started this — which matches nothing, while the container beside it,
/// WHLU0282184, matches a job exactly. The save path looks at both now; rows
/// written before that do not link themselves.
///
/// It matters beyond the column being blank. CarrierScorecard.Build counts only
/// issues that reach a job, so an unlinked case is counted against no carrier
/// at all — which is precisely the number this log exists to produce.
///
/// <para>Reports by default and writes only with <c>--apply</c>. It fills blanks
/// and never replaces a link that already exists, so the worst it can do is
/// attach an issue to a job whose container it names — and every row it touches
/// is printed with its id, so a wrong one can be cleared by hand.</para>
/// </summary>
public static class IssueLinkFix
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var apply = args.Contains("--apply");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        var issues = scope.ServiceProvider.GetRequiredService<OperationalIssueService>();

        var loose = await db.OperationalIssues
            .Where(issue => issue.JobKey == "")
            .ToListAsync();

        Console.WriteLine($"{loose.Count} issues carry no job.");
        if (loose.Count == 0) return 0;

        var found = new List<(OperationalIssue Issue, string Key)>();
        var stillLoose = 0;

        foreach (var issue in loose)
        {
            var key = await issues.ResolveJobKeyAsync(issue.JobRef, default, issue.ContainerNo);
            if (key.Length == 0) { stillLoose++; continue; }
            found.Add((issue, key));
        }

        Console.WriteLine($"{found.Count} of them name a job the register holds.");
        Console.WriteLine($"{stillLoose} still cannot be matched — no job code and no container that lands.");
        Console.WriteLine();

        foreach (var (issue, key) in found.Take(40))
        {
            Console.WriteLine($"  {issue.Code,-14} ref={Short(issue.JobRef),-22} "
                + $"container={Short(issue.ContainerNo),-14} -> {key}");
        }
        if (found.Count > 40) Console.WriteLine($"  … and {found.Count - 40} more");
        Console.WriteLine();

        if (!apply)
        {
            Console.WriteLine("Nothing written. Add --apply to link them.");
            return 0;
        }

        // Ids first, so a wrong link can be undone without guessing which rows
        // this run touched.
        Console.WriteLine("Linking. Ids touched: " + string.Join(",", found.Select(f => f.Issue.Id)));
        foreach (var (issue, key) in found) issue.JobKey = key;
        await db.SaveChangesAsync();
        Console.WriteLine($"{found.Count} issues linked. They now count on the carrier scorecard.");
        return 0;
    }

    private static string Short(string value) =>
        value.Length <= 22 ? value : value[..21] + "…";
}
