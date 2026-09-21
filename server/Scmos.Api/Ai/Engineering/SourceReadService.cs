using System.Text.Json;
using System.Text.RegularExpressions;

namespace Scmos.Api.Ai.Engineering;

/// <summary>One entry of a directory listing, as the repository names it.</summary>
public sealed record SourceEntry(string Name, string Path, string Kind, long Size);

/// <summary>A file as the source hands it over: its text, its size and the blob it came from.</summary>
public sealed record SourceFile(string Path, string Text, long Size, string Sha);

/// <summary>The repository's tree and files at the server's fixed ref, behind a boundary the checks can stand in for.</summary>
public interface ISourceFileSource
{
    Task<IReadOnlyList<SourceEntry>> ListAsync(string path, CancellationToken token);
    /// <summary>Null when the path is not a file at the ref.</summary>
    Task<SourceFile?> ReadAsync(string path, CancellationToken token);
}

/// <summary>
/// One step of a source read: a listing of a directory, or a window of a
/// file's lines — numbered, bounded, with anything shaped like a secret masked.
/// </summary>
public sealed record SourceStep(int Step, string Mode, string Path, int From, int Lines, int Returned, int TotalLines, bool Truncated,
    long Size, string Sha, IReadOnlyList<SourceEntry> Entries, string Text, string Source);

/// <summary>
/// The Engineering Agent's source read (Phase 6, second increment): what was
/// listed and read, the model's analysis of it — labelled as the model's, not
/// the server's — and the basis: nothing run, changed or deployed.
/// </summary>
public sealed record SourceAnswer(string Repository, string Ref, int Total, int Returned, DateTimeOffset RetrievedAt,
    IReadOnlyList<SourceStep> Steps, string Analysis, string Basis);

/// <summary>
/// Which of the repository a read may touch — written once, applied before any request leaves.
///
/// The department's own code: the web app, the API, its tests and its
/// documents. Not its migrations (generated, huge, and a history nobody reads
/// through an agent), not the workflows that deploy it, not any file that has
/// ever held a setting or a secret — settings files, environment files, keys,
/// certificates, the launch configuration — and nothing outside the tree.
/// </summary>
public static class SourcePolicy
{
    public static readonly string[] AllowedRoots = ["app/", "server/Scmos.Api/", "tests/", "docs/"];
    public static readonly string[] AllowedTopLevelFiles = ["README.md", "AGENTS.md", "package.json", "tsconfig.json", "next.config.ts", "eslint.config.mjs"];
    public static readonly string[] DeniedSegments = ["Migrations", "node_modules", ".next", ".github", "bin", "obj", ".claude", ".git", "Properties"];
    public static readonly string[] AllowedExtensions = [".cs", ".ts", ".tsx", ".mjs", ".js", ".md", ".json", ".css", ".yml", ".yaml", ".sql", ".txt", ".csproj", ".sln"];
    public const int MaxPathLength = 200;

    private static readonly Regex Shape = new(@"^[A-Za-z0-9_.@'\-/ ]+$", RegexOptions.Compiled);
    private static readonly Regex DeniedName = new(@"^(appsettings.*|\.env.*|launch\.json|secrets.*|.*\.(pfx|key|pem|snk|p12|jks|crt|cer|kdbx))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The path as the repository spells it, or null when the read may not touch it — with the reason.</summary>
    public static (string? Path, string Reason) Normalise(string? wanted, bool directory)
    {
        var text = (wanted ?? "").Trim().Replace('\\', '/');
        while (text.StartsWith("./", StringComparison.Ordinal)) text = text[2..];
        text = text.TrimStart('/').TrimEnd('/');
        if (text.Length == 0) return directory ? ("", "") : (null, "a file needs a path");
        if (text.Length > MaxPathLength || !Shape.IsMatch(text) || text.Contains("//", StringComparison.Ordinal))
            return (null, "the path is not one the repository could hold");
        var segments = text.Split('/');
        if (segments.Any(segment => segment is "." or ".." || segment.Length == 0)) return (null, "a path may not climb");
        if (segments.Any(segment => DeniedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            return (null, "that part of the repository is not readable through the agent");
        if (DeniedName.IsMatch(segments[^1])) return (null, "settings, environment and key files are never read");
        const string outside = "only the app, the API, the tests and the docs are readable";
        var inRoot = AllowedRoots.Any(root => text.StartsWith(root, StringComparison.Ordinal) || text + "/" == root)
            // A directory on the way to a root — "server" — may be listed, so a reader can find the root.
            || (directory && AllowedRoots.Any(root => root.StartsWith(text + "/", StringComparison.Ordinal)));
        if (directory) return text.Length == 0 || inRoot ? (text, "") : (null, outside);
        if (!inRoot) return AllowedTopLevelFiles.Contains(text, StringComparer.Ordinal) ? (text, "") : (null, outside);
        var dot = segments[^1].LastIndexOf('.');
        var extension = dot < 0 ? "" : segments[^1][dot..];
        return AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? (text, "") : (null, "only source, test and document files are readable");
    }

    private static readonly Regex[] SecretShapes =
    [
        new(@"sk-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled),
        new(@"gh[pousr]_[A-Za-z0-9]{20,}", RegexOptions.Compiled),
        new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled),
        new(@"(?i)(password|pwd|secret|token|apikey|api_key|accountkey|sharedaccesskey)\s*[=:]\s*[^\s;""']{6,}", RegexOptions.Compiled),
        new(@"(?i)bearer\s+[A-Za-z0-9\-._~+/]{20,}=*", RegexOptions.Compiled),
        new(@"eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}", RegexOptions.Compiled),
    ];

    /// <summary>Anything shaped like a key, token or password becomes a mark. The tracked files hold none; this is the belt beside the braces.</summary>
    public static string Scrub(string text)
    {
        foreach (var shape in SecretShapes) text = shape.Replace(text, match =>
            match.Value.Contains('=') || match.Value.Contains(':') ? match.Value[..(match.Value.IndexOfAny(['=', ':']) + 1)] + "[REDACTED]" : "[REDACTED]");
        return text;
    }
}

/// <summary>
/// The Engineering Agent's second read — Phase 6, second increment (22 Sep
/// 2026): the repository's own source, read-only and bounded, so a question
/// about a cause can be answered from the code rather than from a title.
/// A listing of one directory, or one window of one file's lines, at the
/// server's fixed ref. No command is run, no file changed, nothing deployed;
/// the model may ask for a few of these in one run and then say what it
/// makes of them — as its opinion, never as the server's fact.
/// </summary>
public sealed class SourceReadService(ISourceFileSource? source, bool enabled = true)
{
    public const string Tool = "read_source";
    public static readonly string[] Modes = ["list", "file"];
    public const int MaxLines = 400;
    public const int DefaultLines = 200;
    public const int MaxFrom = 100000;
    public const int EntryLimit = 50;
    public const long MaxBytes = 256 * 1024;
    /// <summary>Lines longer than this are cut: a minified bundle is not something to read a cause from.</summary>
    public const int MaxLineLength = 400;

    /// <summary>Whether the tree stands behind this read and the department has turned it on.</summary>
    public bool Connected => source is not null && enabled;

    public async Task<SourceStep> ReadAsync(JsonElement arguments, int step, CancellationToken token)
    {
        if (source is null) throw new InvalidOperationException("No source tree is connected.");
        var mode = arguments.GetProperty("mode").GetString() ?? "";
        var wanted = arguments.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : "";
        var from = arguments.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : 1;
        var lines = arguments.TryGetProperty("lines", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : DefaultLines;
        var limit = arguments.GetProperty("limit").GetInt32();
        if (!Modes.Contains(mode, StringComparer.Ordinal) || from is < 1 or > MaxFrom || lines is < 1 or > MaxLines || limit is < 1 or > EntryLimit)
            throw new InvalidOperationException("Invalid source read arguments.");
        var (path, reason) = SourcePolicy.Normalise(wanted, mode == "list");
        if (path is null) throw new SourceRefusedException(reason);

        if (mode == "list")
        {
            var entries = (await source.ListAsync(path, token))
                .Where(entry => SourcePolicy.Normalise(entry.Path, entry.Kind == "dir").Path is not null)
                .OrderBy(entry => entry.Kind == "dir" ? 0 : 1).ThenBy(entry => entry.Name, StringComparer.Ordinal).ToList();
            var shown = entries.Take(limit).ToList();
            return new SourceStep(step, mode, path, 0, 0, shown.Count, entries.Count, entries.Count > shown.Count, 0, "", shown, "", GitHubSourceFileSource.SourceName);
        }

        var file = await source.ReadAsync(path, token) ?? throw new SourceRefusedException("no such file at the ref");
        if (file.Size > MaxBytes) throw new SourceRefusedException($"the file is larger than {MaxBytes / 1024} KB");
        var all = file.Text.Replace("\r\n", "\n").Split('\n');
        var total = all.Length;
        var window = all.Skip(from - 1).Take(lines)
            .Select((line, index) => $"{from + index,5}  {(line.Length > MaxLineLength ? line[..MaxLineLength] + " …" : line)}").ToList();
        var text = SourcePolicy.Scrub(string.Join("\n", window));
        return new SourceStep(step, mode, path, from, lines, window.Count, total, from - 1 + window.Count < total, file.Size, file.Sha, [], text,
            GitHubSourceFileSource.SourceName);
    }

    /// <summary>The audit's key for a step: the file, or the directory, read.</summary>
    public static string Key(SourceStep step)
    {
        var key = $"{step.Mode}:{(step.Path.Length == 0 ? "/" : step.Path)}";
        return key.Length > 80 ? key[..20] + "…" + key[^59..] : key;
    }
}

/// <summary>A read the policy refused, with a reason a person may be told — never the provider's or GitHub's words.</summary>
public sealed class SourceRefusedException(string reason) : Exception(reason);

public sealed class SourceReadHandler(SourceReadService service) : IAiReadToolHandler
{
    /// <summary>The service itself, for the agent that numbers each step of a run.</summary>
    public SourceReadService Service => service;
    public async Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token)
        => JsonSerializer.SerializeToElement(await service.ReadAsync(arguments, 1, token));
}
