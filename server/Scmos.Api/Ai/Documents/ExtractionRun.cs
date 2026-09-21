using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Documents;

/// <summary>
/// The Workspace's document reader under the platform's controls — Phase 5
/// (22 Sep 2026).
///
/// <see cref="DocumentExtractor"/> has read booking confirmations and box-door
/// photos into the add-job form since before the platform existed, calling
/// the model on its own with no run limit and no audit — the assessment's
/// S4. It still makes the same call and answers the same shape; what changed
/// is around it: the shared per-instance limiter (four in flight, twenty a
/// minute, the same one every agent shares), an internal scope the audit can
/// record (a carrier's account is refused — the audit cannot hold one, and
/// the add-job form was never theirs), and a run in <c>ai_audit_logs</c> —
/// agent <c>document-agent</c>, tool <c>extract_document</c>, the category as
/// its view, one key per file — committed before the fields are released,
/// as every agent's read is. No field value, file name beyond a trimmed key,
/// prompt or document content is written to the audit.
/// </summary>
public sealed class ExtractionRun(IAiExecutionAudit audit, AiRunLimiter limiter, TimeProvider clock,
    IOptions<OpenAiOptions>? providerOptions = null)
{
    public const string Tool = "extract_document";

    public async Task<ExtractionResult> ReadAsync(AppUser user, string category, IReadOnlyList<DocumentPart> parts,
        IDocumentExtractor extractor, string correlationId, CancellationToken token)
    {
        if (!extractor.Configured) return await extractor.ReadAsync(category, parts, token);
        var scope = AiPermissionPolicy.Scope(user);
        if (scope is null || user.Role == Roles.Subcontractor)
            return new ExtractionResult(null, "Document reading is for the department's own accounts.", StatusCodes.Status403Forbidden);
        if (parts.Count is < 1 or > 4)
            return new ExtractionResult(null, "Attach one to four files.", StatusCodes.Status400BadRequest);
        using var lease = limiter.TryEnter();
        if (lease is null)
            return new ExtractionResult(null, "Document reading is busy — try again in a moment.", StatusCodes.Status429TooManyRequests);
        if (!await audit.CheckReadyAsync(token))
            return new ExtractionResult(null, "Document reading is paused: the AI audit is not ready.", StatusCodes.Status503ServiceUnavailable);

        var runId = Guid.NewGuid().ToString("N");
        var toolCallId = Guid.NewGuid().ToString("N");
        var view = ViewOf(category);
        var model = providerOptions?.Value.Model is { Length: > 0 } named ? named : "unconfigured";
        var correlation = AiAuditRules.IsCorrelation(correlationId) ? correlationId : "";
        var keys = parts.Select((part, index) => $"file:{index + 1}:{Key(part.FileName)}").ToArray();
        var toolStarted = false;
        async Task Record(string kind, string status, bool withEvidence = false, CancellationToken? cleanup = null)
        {
            // The tool's step is on its events; a completion after a step that never started names no tool.
            var withTool = kind is "tool_started" or "tool_completed" || (kind == "run_completed" && toolStarted);
            var step = kind is "tool_started" or "tool_completed" ? 1 : kind == "run_completed" ? (toolStarted ? 1 : 0) : (int?)null;
            await audit.RecordAsync(new(runId, user.UserId, user.Role, DocumentAgent.Id, kind,
                withTool ? Tool : null, status, clock.GetUtcNow(),
                withEvidence ? keys.Length : null, withEvidence ? keys.Length : null, scope, null,
                withTool ? toolCallId : null, model, withTool ? view : null,
                withTool ? parts.Count : null, withEvidence ? keys : null, correlation, step), cleanup ?? token);
        }

        var started = false;
        try
        {
            await Record("run_started", "running");
            started = true;
            await Record("tool_started", "running");
            toolStarted = true;
            ExtractionResult result;
            try { result = await extractor.ReadAsync(category, parts, token); }
            // The SDK's own timeout arrives as a cancellation that is not the request's: the provider did not answer.
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { result = new ExtractionResult(null, "Could not read the file.", StatusCodes.Status502BadGateway); }
            var status = result.Fields is not null ? "succeeded"
                : result.Status == StatusCodes.Status429TooManyRequests ? "provider_busy" : "provider_unavailable";
            await Record("tool_completed", status, result.Fields is not null);
            await Record("run_completed", status, result.Fields is not null);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (started) await Cleanup("cancelled");
            throw;
        }
        catch (Exception)
        {
            if (started) await Cleanup("audit_failed");
            // Nothing the model read is released without its record, the same as every agent's read.
            return new ExtractionResult(null, "Document reading is paused: the AI audit could not record it.", StatusCodes.Status503ServiceUnavailable);
        }

        async Task Cleanup(string status)
        {
            using var window = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                if (toolStarted) await Record("tool_completed", status, cleanup: window.Token);
                await Record("run_completed", status, cleanup: window.Token);
            }
            catch (Exception) { /* the run stays incomplete in the audit, which is what happened */ }
        }
    }

    /// <summary>The category as the audit's vocabulary spells it; anything else is read as an import, as the extractor itself does.</summary>
    public static string ViewOf(string category)
    {
        var view = (category ?? "").Trim().ToLowerInvariant();
        return AiAuditRules.ExtractViews.Contains(view, StringComparer.Ordinal) ? view : "import";
    }

    /// <summary>A file's name as an audit key: letters, digits, dots, dashes and underscores only, at most 40 of them.</summary>
    public static string Key(string fileName)
    {
        var kept = new string((fileName ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return kept.Length > 40 ? kept[..40] : kept;
    }
}
