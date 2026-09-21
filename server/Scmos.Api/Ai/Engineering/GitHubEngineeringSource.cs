using System.Text.Json;

namespace Scmos.Api.Ai.Engineering;

/// <summary>
/// Fixed-host, fixed-repository, GET-only GitHub REST reader. No caller or model
/// value is interpolated into a URL. Redirects are disabled at registration.
/// </summary>
public sealed class GitHubEngineeringSource(HttpClient client) : IEngineeringSource
{
    public const string Repository = "fose-cloud/SCMOS";

    public async Task<IReadOnlyList<EngineeringItem>> ReadAsync(string view, int limit, CancellationToken token)
    {
        if (!EngineeringReadService.Views.Contains(view, StringComparer.Ordinal)
            || limit is < 1 or > EngineeringReadService.EvidenceLimit)
            throw new InvalidOperationException("Invalid repository read.");
        var path = view switch
        {
            // GitHub's issues endpoint also returns PRs. Filter those out below.
            "open_issues" => $"repos/{Repository}/issues?state=open&per_page=100",
            "open_prs" => $"repos/{Repository}/pulls?state=open&per_page={limit}",
            _ => $"repos/{Repository}/commits?per_page={limit}",
        };
        using var response = await client.GetAsync(path, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Repository metadata unavailable.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Invalid repository metadata.");
        var rows = new List<EngineeringItem>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (view == "open_issues" && item.TryGetProperty("pull_request", out _)) continue;
            var row = view == "recent_commits" ? Commit(item) : IssueOrPull(item, view);
            rows.Add(row);
            if (rows.Count == limit) break;
        }
        return rows;
    }

    private static EngineeringItem IssueOrPull(JsonElement item, string view)
    {
        var number = item.GetProperty("number").GetInt32();
        if (number <= 0 || item.GetProperty("state").GetString() != "open")
            throw new InvalidOperationException("Invalid repository item.");
        var kind = view == "open_issues" ? "issue" : "pr";
        var at = item.GetProperty("updated_at").GetDateTimeOffset();
        return new($"{kind}:{number}", Clean(item.GetProperty("title").GetString()),
            $"https://github.com/{Repository}/{(kind == "pr" ? "pull" : "issues")}/{number}", "open", at);
    }

    private static EngineeringItem Commit(JsonElement item)
    {
        var sha = item.GetProperty("sha").GetString() ?? "";
        if (sha.Length != 40 || sha.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Invalid commit id.");
        var commit = item.GetProperty("commit");
        var subject = (commit.GetProperty("message").GetString() ?? "").Split('\n', 2)[0];
        var at = commit.GetProperty("committer").GetProperty("date").GetDateTimeOffset();
        return new($"commit:{sha}", Clean(subject), $"https://github.com/{Repository}/commit/{sha}", "commit", at);
    }

    private static string Clean(string? value)
    {
        var text = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > 160 ? text[..160] : text;
    }
}
