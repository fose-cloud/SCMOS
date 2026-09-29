using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Booking;

public sealed record BookingDraftResult(BookingDraft? Draft, string? Error, int Status);

/// <summary>The customers a booking may name: the ones the register already holds — the add-job form's own list.</summary>
public interface IKnownCustomers
{
    Task<IReadOnlyCollection<string>> ListAsync(CancellationToken token);
}

public sealed class RegisterCustomers(JobRegisterCache register) : IKnownCustomers
{
    public async Task<IReadOnlyCollection<string>> ListAsync(CancellationToken token) =>
        Of((await register.ReadAsync(token, staleOk: true)).Rows);

    public static IReadOnlyCollection<string> Of(IEnumerable<CachedJobRow> rows) =>
        rows.Select(row => Formats.Clean(row.Record?.Customer ?? "")).Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>The Booking Agent's names, as the registry, the audit and the decision log spell them.</summary>
public static class BookingAgent
{
    public const string Id = "booking-agent";
    public const string Tool = "draft_booking";
    public const string DecisionType = "booking_draft";
}

/// <summary>
/// The Booking Agent, pasted-text half (AI Agent Platform specification §27):
/// a booking request copied from mail or chat, read into the add-job form. The
/// model proposes each field with the words it came from; <see cref="BookingVerification"/>
/// keeps only what those words say, and only a customer the register already
/// knows. A person reviews, edits and saves through the form as always — no
/// job is created here, and the existing booking path is untouched.
///
/// <para>
/// Under the platform's controls, as the document reader is (<see cref="Documents.ExtractionRun"/>):
/// the agent's flag and governance, the shared run limiter, and a run in
/// <c>ai_audit_logs</c> committed before anything is released. No text, value
/// or quote is written to the audit — its evidence key is <c>paste:1</c>.
/// </para>
/// </summary>
public sealed class BookingDraftService(IBookingTextReader reader, IAiExecutionAudit audit, AiRunLimiter limiter,
    IAiGovernance governance, IKnownCustomers customers, IOptions<AiOptions> options, TimeProvider clock,
    IOptions<OpenAiOptions>? providerOptions = null)
{
    public async Task<BookingDraftResult> DraftAsync(AppUser user, string? category, string? text, string correlationId, CancellationToken token)
    {
        var ai = options.Value;
        var scope = AiPermissionPolicy.Scope(user);
        if (scope is null || user.Role == Roles.Subcontractor)
            return new(null, "Booking reading is for the department's own accounts.", StatusCodes.Status403Forbidden);
        var body = (text ?? "").Trim();
        if (body.Length == 0) return new(null, "Paste the booking text first.", StatusCodes.Status400BadRequest);
        if (body.Length > BookingTextReader.MaxText)
            return new(null, $"The text is too long — paste the booking itself, up to {BookingTextReader.MaxText} characters.", StatusCodes.Status413PayloadTooLarge);
        var wanted = BookingVerification.CategoryOf(category);
        // AI__Enabled is the server's own switch; under it the Control Tower's switch, or the flag, decides.
        var gate = ai.Enabled
            ? (await governance.SnapshotAsync(token)).Gate(BookingAgent.Id, ai.BookingAgentEnabled, AgentNeed.Recommend)
            : new GovernanceGate(false, "agent_disabled", "SCMOS AI is disabled in configuration.", AiAutonomy.Disabled);
        if (!gate.Allowed) return new(null, gate.Reason, StatusCodes.Status503ServiceUnavailable);
        if (!reader.Configured) return new(null, "Booking reading is not configured.", StatusCodes.Status501NotImplemented);
        using var lease = limiter.TryEnter();
        if (lease is null) return new(null, "Booking reading is busy — try again in a moment.", StatusCodes.Status429TooManyRequests);
        if (!await audit.CheckReadyAsync(token))
            return new(null, "Booking reading is paused: the AI audit is not ready.", StatusCodes.Status503ServiceUnavailable);

        var runId = Guid.NewGuid().ToString("N");
        var toolCallId = Guid.NewGuid().ToString("N");
        var view = wanted.ToLowerInvariant();
        var model = providerOptions?.Value.Model is { Length: > 0 } named ? named : "unconfigured";
        var correlation = AiAuditRules.IsCorrelation(correlationId) ? correlationId : "";
        string[] keys = ["paste:1"];
        var toolStarted = false;
        async Task Record(string kind, string status, bool withEvidence = false, CancellationToken? cleanup = null)
        {
            var withTool = kind is "tool_started" or "tool_completed" || (kind == "run_completed" && toolStarted);
            var step = kind is "tool_started" or "tool_completed" ? 1 : kind == "run_completed" ? (toolStarted ? 1 : 0) : (int?)null;
            await audit.RecordAsync(new(runId, user.UserId, user.Role, BookingAgent.Id, kind,
                withTool ? BookingAgent.Tool : null, status, clock.GetUtcNow(),
                withEvidence ? keys.Length : null, withEvidence ? keys.Length : null, scope, null,
                withTool ? toolCallId : null, model, withTool ? view : null,
                withTool ? 1 : null, withEvidence ? keys : null, correlation, step), cleanup ?? token);
        }

        var started = false;
        try
        {
            await Record("run_started", "running");
            started = true;
            await Record("tool_started", "running");
            toolStarted = true;
            BookingReadResult read;
            try { read = await reader.ReadAsync(wanted, body, token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { read = new(null, "Could not read the text.", StatusCodes.Status502BadGateway); }
            var status = read.Reading is not null ? "succeeded"
                : read.Status == StatusCodes.Status429TooManyRequests ? "provider_busy" : "provider_unavailable";
            await Record("tool_completed", status, read.Reading is not null);
            await Record("run_completed", status, read.Reading is not null);
            if (read.Reading is null) return new(null, read.Error ?? "Could not read the text.", read.Status);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(Formats.Zone).DateTime);
            var draft = BookingVerification.Check(wanted, body, read.Reading.Fields, await customers.ListAsync(token), today);
            return new(draft, null, StatusCodes.Status200OK);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (started) await Cleanup("cancelled");
            throw;
        }
        catch (Exception)
        {
            if (started) await Cleanup("audit_failed");
            return new(null, "Booking reading is paused: the AI audit could not record it.", StatusCodes.Status503ServiceUnavailable);
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
}
