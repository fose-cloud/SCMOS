using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Data;

namespace Scmos.Api.Ai;

/// <summary>Strict durable writer, separate from SCMOS's best-effort business audit.</summary>
public sealed class SqlAiExecutionAudit(DbContextOptions<ScmosDbContext> options, IOptions<AiOptions>? ai = null) : IAiExecutionAudit, IAiPolicyAudit
{
    public bool Ready { get; private set; }

    /// <summary>
    /// How long one audit write, or the readiness probe, may take.
    ///
    /// Five seconds flat until 21 September 2026, when the production database,
    /// busy with a whole-register read in working hours, could not commit a
    /// run's first event inside it: from 14:37 every agent answered
    /// audit_not_ready with no row written — the Data Agent that had answered
    /// at noon among them. Half the run's own budget, never under five seconds
    /// nor over thirty: a write still fails rather than waits for ever, and the
    /// run's token bounds it besides, but it waits as long as the run it is
    /// recording would. A sink built without options keeps the five.
    /// </summary>
    public static int WriteSeconds(AiOptions? options) => options is null ? 5 : Math.Clamp(options.TimeoutSeconds / 2, 5, 30);

    private int Budget => WriteSeconds(ai?.Value);

    internal ScmosDbContext Open()
    {
        // Never share tracked entities/transactions with a business repository, or log parameter values.
        var db = new ScmosDbContext(new DbContextOptionsBuilder<ScmosDbContext>(options)
            .UseLoggerFactory(NullLoggerFactory.Instance).EnableSensitiveDataLogging(false).Options);
        db.Database.SetCommandTimeout(Budget);
        return db;
    }

    public async Task<bool> CheckReadyAsync(CancellationToken token)
    {
        if (Ready) return true; // Only scoped to this request; every write still has to commit.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Budget));
        try
        {
            await using var db = Open();
            // Select the actual mapped shape, not CanConnect: a missing migration must refuse readiness.
            await db.AiAuditLogs.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            return Ready = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return Ready = false; }
    }

    public async Task RecordAsync(AiExecutionEvent entry, CancellationToken token)
    {
        // Every run's start names the prompt that answered it — the build's commit — without any agent having to.
        if (entry.Event == "run_started" && entry.PromptVersion is null) entry = entry with { PromptVersion = AiBuild.PromptVersion };
        var row = AiAuditRules.From(entry);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Budget));
        try
        {
            await using var strategyContext = Open();
            await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
            {
                // A fresh context on each retry handles ambiguous commits via the unique event/fingerprint.
                await using var db = Open();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var prior = await db.AiAuditLogs.AsNoTracking().Where(e => e.RunId == row.RunId)
                    .OrderBy(e => e.Sequence).ToListAsync(ct);
                if (AiAuditRules.MayAppend(prior, row))
                {
                    row.Id = 0;
                    db.AiAuditLogs.Add(row);
                    await db.SaveChangesAsync(ct);
                }
                await transaction.CommitAsync(ct);
            }, timeout.Token);
            Ready = true;
        }
        catch { Ready = false; throw; }
    }

    public async Task<string?> RecordAuthorizationAsync(AiPolicyAuditEvent entry, AgentBudgetPolicy? budget, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Budget));
        await using var strategyContext = Open();
        string? refused = null;
        await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            refused = null; // A failed transaction must not carry a provisional refusal into its retry.
            await using var db = Open();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var existing = await db.AiAuthorizationLogs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == entry.Id, ct);
            if (existing is not null)
            {
                var repeated = AuthorizationRow(entry with { Decision = entry.Decision with
                {
                    Decision = Enum.Parse<AiAuthorizationVerdict>(existing.Decision), ReasonCode = existing.ReasonCode
                } }, existing.ReservedCost);
                if (repeated.Fingerprint != existing.Fingerprint) throw new InvalidOperationException("Conflicting authorization audit replay.");
                refused = existing.Decision == "Deny" ? existing.ReasonCode : null;
                return;
            }
            var decision = entry.Decision;
            var cost = 0m;
            if (decision.Allowed)
            {
                if (await db.AiTools.AsNoTracking().AnyAsync(x => x.Name == entry.Request.ToolId && !x.Enabled, ct)) refused = "tool_disabled";
                else if (!AiPolicyCatalog.ValidBudget(budget)) refused = "budget_required";
                else
                {
                    var at = entry.At.ToUniversalTime();
                    var day = new DateTimeOffset(at.Year, at.Month, at.Day, 0, 0, 0, TimeSpan.Zero);
                    var month = new DateTimeOffset(at.Year, at.Month, 1, 0, 0, 0, TimeSpan.Zero);
                    var agent = SafeId(entry.Request.AgentId, 40);
                    var rows = db.AiAuthorizationLogs.AsNoTracking().Where(x => x.AgentId == agent && x.At >= month && x.ReservedCost > 0);
                    var monthly = await rows.SumAsync(x => (decimal?)x.ReservedCost, ct) ?? 0;
                    var daily = await rows.Where(x => x.At >= day).SumAsync(x => (decimal?)x.ReservedCost, ct) ?? 0;
                    var requests = await rows.CountAsync(x => x.At >= day, ct);
                    // Reserve before dispatch. Lost/failed calls are not refunded; conservative accounting avoids double spending.
                    if (requests >= budget!.DailyRequests || daily + budget.MaxReservationCost > budget.DailyCostLimit
                        || monthly + budget.MaxReservationCost > budget.MonthlyCostLimit) refused = "budget_exhausted";
                    else cost = budget.MaxReservationCost;
                }
                if (refused is not null) decision = decision with { Decision = AiAuthorizationVerdict.Deny, ReasonCode = refused };
            }
            var row = AuthorizationRow(entry with { Decision = decision }, cost);
            db.AiAuthorizationLogs.Add(row);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, timeout.Token);
        return refused;
    }

    public static AiAuthorizationLog AuthorizationRow(AiPolicyAuditEvent entry, decimal reservation = 0)
    {
        // SQL decimal(18,6) preserves scale; canonical JSON/hash must not depend on 0.01 vs 0.010000.
        reservation = decimal.Parse(reservation.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        var request = entry.Request;
        // Rejecting unknown text must not copy an injected prompt/credential into audit.
        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            correlationId = SafeId(request.CorrelationId, 64), agentVersion = SafeId(request.AgentVersion, 60),
            userId = SafeId(request.UserId, 160), originAgentId = AgentId(request.OriginAgentId ?? request.AgentId),
            action = Enum.IsDefined(request.Action) ? request.Action.ToString() : "unknown_action",
            originalAction = request.OriginalAction?.ToString(), tool = AiPolicyEntry.ToolContracts.ContainsKey(request.ToolId ?? "")
                ? request.ToolId : Hash(request.ToolId ?? "", 60), inputValid = request.InputValid,
            resourceType = SafeId(request.ResourceType, 60), resourceId = SafeId(request.ResourceId, 160),
            customerId = SafeId(request.CustomerId, 160), carrierId = SafeId(request.CarrierId, 160), shipmentId = SafeId(request.ShipmentId, 160),
            scope = request.RequestedDataScope is null ? null : new { request.RequestedDataScope.Team, operatorId = SafeId(request.RequestedDataScope.OperatorId, 20) },
            risk = request.RiskLevel.ToString(), permission = entry.Decision.Permission.ToString(),
            approvalId = SafeId(request.ApprovalId, 160),
            delegationChain = (request.DelegationChain ?? []).Take(8).Select(AgentId).ToArray(),
            executionStatus = "not_executed", latencyMs = entry.LatencyMs,
            tokenUsage = (int?)null, toolUsage = 0, estimatedCost = reservation
        });
        var row = new AiAuthorizationLog
        {
            Id = entry.Id, At = entry.At, AgentId = AgentId(request.AgentId), PolicyVersion = entry.Decision.MatchedPolicy,
            Decision = entry.Decision.Decision.ToString(), ReasonCode = entry.Decision.ReasonCode,
            SecurityEvent = entry.SecurityEvent, ReservedCost = reservation, Metadata = metadata
        };
        row.Fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(row)));
        return row;
    }

    private static string SafeId(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (!value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) && value.Length <= max
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':' or '/')) return value;
        return Hash(value, max);
    }
    private static string AgentId(string? value) => new AgentRegistry().Find(value ?? "") is not null
        ? value! : Hash(value ?? "", 40);
    private static string Hash(string value, int max) => "hash:"
        + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..Math.Min(24, max - 5)];
}
