using System.Diagnostics;

namespace Scmos.Api.Ai.Sre;

/// <summary>
/// One request as the ring remembers it: the route's pattern — never its
/// path, which carries a job key or a person's id — the status, how long it
/// took, the correlation id it came with, and the exception's type when it
/// threw. No query string, no body, no user, no message text.
/// </summary>
public sealed record RequestSample(DateTimeOffset At, string Method, string Route, int Status, double DurationMs, string Correlation, string? Exception);

/// <summary>
/// The last few thousand API requests, in memory — Phase 7's second read
/// (22 Sep 2026). The API logs to the App Service's console and nowhere
/// else; without Application Insights there was no way to ask "what was
/// slow at 09:41" or "what threw this morning" except reading a log stream
/// by hand. This ring answers those two questions from inside the process,
/// with safe identifiers, and forgets everything past its capacity or the
/// process's restart. It is not telemetry storage and does not try to be.
/// </summary>
public sealed class RequestTelemetry(TimeProvider clock)
{
    public const int Capacity = 5000;
    /// <summary>A request slower than this is worth a look — the same figure the database ping uses.</summary>
    public const int SlowMs = 5000;

    private readonly RequestSample?[] _ring = new RequestSample?[Capacity];
    private readonly Lock _gate = new();
    private int _next;
    private long _recorded;

    public DateTimeOffset StartedAt { get; } = clock.GetUtcNow();
    public long Recorded => Interlocked.Read(ref _recorded);

    public void Record(RequestSample sample)
    {
        lock (_gate)
        {
            _ring[_next] = sample;
            _next = (_next + 1) % Capacity;
            _recorded++;
        }
    }

    /// <summary>The samples since a moment, oldest first.</summary>
    public IReadOnlyList<RequestSample> Since(DateTimeOffset since)
    {
        lock (_gate)
        {
            return _ring.Where(sample => sample is not null && sample.At >= since).Select(sample => sample!)
                .OrderBy(sample => sample.At).ToList();
        }
    }
}

/// <summary>
/// Records every API request into the ring — its route pattern, status,
/// duration and correlation id — and the type of an exception it threw, then
/// rethrows it. It runs inside the exception handler (after it in the
/// pipeline), so the handler still answers the caller as it always did; ahead
/// of it, the handler would have answered first and the type would be lost.
/// </summary>
public sealed class RequestTelemetryMiddleware(RequestDelegate next, RequestTelemetry telemetry, TimeProvider clock)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") && path != "/health") { await next(context); return; }
        var watch = Stopwatch.StartNew();
        string? exception = null;
        try { await next(context); }
        catch (Exception error) { exception = error.GetType().Name; throw; }
        finally
        {
            telemetry.Record(new RequestSample(clock.GetUtcNow(), context.Request.Method, RouteOf(context),
                exception is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError, watch.Elapsed.TotalMilliseconds,
                CorrelationOf(context), exception));
        }
    }

    /// <summary>The endpoint's pattern — "/api/jobs/{key}" — never the path with its key filled in; an unrouted call is named by its first segment only.</summary>
    public static string RouteOf(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint endpoint && endpoint.RoutePattern.RawText is { Length: > 0 } pattern) return pattern;
        var first = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(2) ?? [];
        return "/" + string.Join("/", first) + "/(unrouted)";
    }

    private static string CorrelationOf(HttpContext context)
    {
        var header = context.Request.Headers["X-Correlation-Id"].ToString();
        return AiAuditRules.IsCorrelation(header) && header.Length > 0 ? header
            : AiAuditRules.IsCorrelation(context.TraceIdentifier) ? context.TraceIdentifier : "";
    }
}
