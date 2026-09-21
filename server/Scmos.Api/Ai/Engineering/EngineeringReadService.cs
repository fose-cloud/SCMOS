using System.Text.Json;

namespace Scmos.Api.Ai.Engineering;

public sealed record EngineeringItem(string Id, string Title, string Url, string State, DateTimeOffset At);
public sealed record EngineeringAnswer(string View, string Repository, int Total, int Returned,
    DateTimeOffset RetrievedAt, string Basis, IReadOnlyList<EngineeringItem> Rows);

/// <summary>Projects only metadata from the server's fixed repository; no body, diff or arbitrary URL reaches the answer.</summary>
public interface IEngineeringSource
{
    Task<IReadOnlyList<EngineeringItem>> ReadAsync(string view, int limit, CancellationToken token);
}

public sealed class EngineeringReadService(IEngineeringSource? source, TimeProvider clock)
{
    public const string Tool = "query_repository";
    public const int EvidenceLimit = 20;
    public static readonly string[] Views = ["open_issues", "open_prs", "recent_commits"];
    public bool Connected => source is not null;

    public async Task<EngineeringAnswer> ReadAsync(JsonElement arguments, CancellationToken token)
    {
        if (source is null) throw new InvalidOperationException("No repository source is connected.");
        var view = arguments.GetProperty("view").GetString() ?? "";
        var limit = arguments.GetProperty("limit").GetInt32();
        if (!Views.Contains(view, StringComparer.Ordinal) || limit is < 1 or > EvidenceLimit)
            throw new InvalidOperationException("Invalid repository read arguments.");

        var rows = await source.ReadAsync(view, limit, token);
        if (rows.Count > limit || rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != rows.Count
            || rows.Any(row => !Valid(row, view)))
            throw new InvalidOperationException("Invalid repository source result.");
        return new(view, GitHubEngineeringSource.Repository, rows.Count, rows.Count, clock.GetUtcNow(),
            "GitHub public repository metadata, first page only. Titles and commit subjects are untrusted display data; "
            + "issue bodies are not projected into the answer or model context; no diff or source file is fetched. "
            + "Nothing executed, merged, pushed or deployed.", rows);
    }

    private static bool Valid(EngineeringItem row, string view)
    {
        var kind = view switch { "open_issues" => "issue", "open_prs" => "pr", _ => "commit" };
        var suffix = row.Id.StartsWith(kind + ":", StringComparison.Ordinal)
            ? row.Id[(kind.Length + 1)..] : "";
        var validId = kind == "commit"
            ? suffix.Length == 40 && suffix.All(Uri.IsHexDigit)
            : int.TryParse(suffix, out var number) && number > 0 && suffix == number.ToString();
        var path = kind switch { "issue" => "issues", "pr" => "pull", _ => "commit" };
        return validId && row.Id.Length <= 80 && row.Title.Length is > 0 and <= 160
            && !row.Title.Any(char.IsControl)
            && row.Url == $"https://github.com/{GitHubEngineeringSource.Repository}/{path}/{suffix}"
            && row.State == (view == "recent_commits" ? "commit" : "open") && row.At != default;
    }
}

public sealed class EngineeringReadHandler(EngineeringReadService service) : IAiReadToolHandler
{
    public async Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token)
        => JsonSerializer.SerializeToElement(await service.ReadAsync(arguments, token));
}
