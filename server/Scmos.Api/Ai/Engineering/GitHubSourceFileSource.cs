using System.Text;
using System.Text.Json;

namespace Scmos.Api.Ai.Engineering;

/// <summary>
/// Fixed-host, fixed-repository, fixed-ref, GET-only reader of the tree and
/// its files through GitHub's contents API. No caller or model value reaches
/// a URL except a path the policy has already passed, escaped segment by
/// segment. Redirects are disabled at registration.
/// </summary>
public sealed class GitHubSourceFileSource(HttpClient client) : ISourceFileSource
{
    public const string Repository = GitHubEngineeringSource.Repository;
    /// <summary>The branch production is built from — the code the department runs, not whatever is newest somewhere.</summary>
    public const string Ref = "azure-dotnet-migration";
    public const string SourceName = "github_public_repo";

    public async Task<IReadOnlyList<SourceEntry>> ListAsync(string path, CancellationToken token)
    {
        using var json = await FetchAsync(path, token);
        if (json.RootElement.ValueKind != JsonValueKind.Array) throw new SourceRefusedException("not a directory at the ref");
        var entries = new List<SourceEntry>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var kind = item.GetProperty("type").GetString() ?? "";
            if (kind is not ("file" or "dir")) continue;
            var name = Clean(item.GetProperty("name").GetString());
            var full = Clean(item.GetProperty("path").GetString());
            var size = item.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
            if (name.Length == 0 || full.Length == 0) continue;
            entries.Add(new SourceEntry(name, full, kind, size));
        }
        return entries;
    }

    public async Task<SourceFile?> ReadAsync(string path, CancellationToken token)
    {
        using var json = await FetchAsync(path, token);
        if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = json.RootElement;
        if ((root.TryGetProperty("type", out var type) ? type.GetString() : "") != "file") return null;
        var size = root.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
        var sha = Clean(root.TryGetProperty("sha", out var h) ? h.GetString() : "");
        if (size > SourceReadService.MaxBytes) return new SourceFile(path, "", size, sha);
        var encoding = root.TryGetProperty("encoding", out var e) ? e.GetString() : "";
        var content = root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        if (encoding != "base64") throw new SourceRefusedException("the file could not be read at the ref");
        var bytes = Convert.FromBase64String(content.Replace("\n", "", StringComparison.Ordinal));
        return new SourceFile(path, Encoding.UTF8.GetString(bytes), size, sha);
    }

    private async Task<JsonDocument> FetchAsync(string path, CancellationToken token)
    {
        // The policy has passed this path already; this is the belt beside the braces.
        if (SourcePolicy.Normalise(path, true).Path is null && SourcePolicy.Normalise(path, false).Path is null)
            throw new InvalidOperationException("Invalid source path.");
        var escaped = string.Join("/", path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var url = $"repos/{Repository}/contents/{escaped}?ref={Ref}";
        using var response = await client.GetAsync(url, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new SourceRefusedException("no such path at the ref");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Repository source unavailable.");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
    }

    private static string Clean(string? value)
    {
        var text = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > 200 ? text[..200] : text;
    }
}
