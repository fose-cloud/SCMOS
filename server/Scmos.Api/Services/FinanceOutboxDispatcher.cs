using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Delivers durable Finance outbox events. The adapter is called only after the
/// Billing transaction committed. A crash after the external call is safe: the
/// same idempotency key is used on every retry.
/// </summary>
public sealed class FinanceOutboxDispatcher(IServiceProvider services,
    IOptions<FinanceIntegrationOptions> options, ILogger<FinanceOutboxDispatcher> log) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.Enabled && options.Value.Valid)
                {
                    var count = await ProcessDueAsync(stoppingToken);
                    if (count > 0) log.LogInformation("Finance outbox: processed {Count} event(s)", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { log.LogError(error, "Finance outbox pass failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 5, 300)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    public async Task<int> ProcessDueAsync(CancellationToken token)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<FinanceAdapterResolver>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var adapter = resolver.Current;
        if (!adapter.Configured) return 0;
        var now = clock.GetUtcNow();
        var batch = Math.Clamp(options.Value.BatchSize, 1, 100);
        var due = await db.IntegrationOutbox.Where(row => row.EventType == FinanceEvents.SubmissionRequested
                && row.NextAttemptAt <= now
                && (row.Status == OutboxStatus.Pending || row.Status == OutboxStatus.Retry
                    || (row.Status == OutboxStatus.Processing && row.LockedUntil < now)))
            .OrderBy(row => row.NextAttemptAt).ThenBy(row => row.Id).Take(batch).ToListAsync(token);
        var processed = 0;
        foreach (var outbox in due)
        {
            try
            {
                outbox.Status = OutboxStatus.Processing;
                outbox.LockedUntil = now.AddMinutes(2);
                outbox.UpdatedAt = now;
                await db.SaveChangesAsync(token);

                var envelope = JsonSerializer.Deserialize<FinanceOutboxEnvelope>(outbox.PayloadJson, Json);
                var record = envelope is null ? null : await db.BillingFinanceRecords
                    .FirstOrDefaultAsync(row => row.Id == envelope.FinanceRecordId, token);
                if (record is null)
                {
                    outbox.Status = OutboxStatus.Dead; outbox.LastError = "Finance record not found";
                    outbox.LockedUntil = null; outbox.UpdatedAt = clock.GetUtcNow();
                    await db.SaveChangesAsync(token); processed++; continue;
                }
                if (!string.Equals(record.Adapter, adapter.Name, StringComparison.OrdinalIgnoreCase))
                {
                    await FailAsync(db, audit, outbox, record, record.InvoiceId.ToString(),
                        $"Configured adapter {adapter.Name} does not match queued adapter {record.Adapter}",
                        false, clock.GetUtcNow(), token);
                    processed++; continue;
                }
                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(Hash(record.PayloadJson)),
                        Encoding.ASCII.GetBytes(record.PayloadHash)))
                {
                    await FailAsync(db, audit, outbox, record, record.InvoiceId.ToString(),
                        "Canonical payload hash mismatch", false, clock.GetUtcNow(), token);
                    processed++; continue;
                }
                var payload = JsonSerializer.Deserialize<CanonicalFinanceInvoice>(record.PayloadJson, Json);
                if (payload is null)
                {
                    await FailAsync(db, audit, outbox, record, record.InvoiceId.ToString(),
                        "Canonical payload is invalid", false, clock.GetUtcNow(), token);
                    processed++; continue;
                }

                record.Status = FinanceStatus.Processing; record.LastAttemptAt = clock.GetUtcNow();
                record.UpdatedAt = record.LastAttemptAt.Value;
                var result = await adapter.SubmitAsync(payload, record.IdempotencyKey, token);
                var completedAt = clock.GetUtcNow();
                outbox.Attempts++; outbox.LastAttemptAt = completedAt; outbox.LockedUntil = null;
                outbox.UpdatedAt = completedAt; record.Attempts = outbox.Attempts;
                record.LastAttemptAt = completedAt; record.ResponseCode = Cut(result.Code, 80);
                record.ResponseMessage = Cut(result.Message, 800); record.UpdatedAt = completedAt;
                if (result.Ok)
                {
                    record.Status = result.Status is FinanceStatus.Accepted or FinanceStatus.Submitted
                        ? result.Status : FinanceStatus.Submitted;
                    record.ExternalReference = Cut(result.ExternalReference, 160);
                    record.NextAttemptAt = null;
                    outbox.Status = OutboxStatus.Completed; outbox.ProcessedAt = completedAt;
                    outbox.LastError = "";
                    audit.Stage(SystemActor(), AuditActions.Update, "billing-finance-record", record.Id.ToString(),
                        payload.InvoiceNumber, "status", FinanceStatus.Processing, record.Status,
                        $"Adapter {adapter.Name} · {record.ResponseCode}", "integration");
                }
                else
                {
                    var next = result.Retryable ? FinanceRetry.Next(outbox.Attempts, completedAt) : null;
                    if (next is null)
                    {
                        record.Status = FinanceStatus.Failed; record.NextAttemptAt = null;
                        outbox.Status = OutboxStatus.Dead;
                    }
                    else
                    {
                        record.Status = FinanceStatus.Retrying; record.NextAttemptAt = next.Value;
                        outbox.Status = OutboxStatus.Retry; outbox.NextAttemptAt = next.Value;
                    }
                    outbox.LastError = Cut(result.Message, 800);
                    audit.Stage(SystemActor(), AuditActions.Update, "billing-finance-record", record.Id.ToString(),
                        payload.InvoiceNumber, "status", FinanceStatus.Processing, record.Status,
                        $"Adapter {adapter.Name} · {record.ResponseCode}", "integration");
                }
                await db.SaveChangesAsync(token);
                processed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another worker acquired or completed this event first.
                foreach (var entry in db.ChangeTracker.Entries().Where(entry => entry.State != EntityState.Unchanged))
                    entry.State = EntityState.Detached;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                log.LogWarning(error, "Finance outbox event {EventId} failed", outbox.Id);
                try
                {
                    var record = await db.BillingFinanceRecords.FirstOrDefaultAsync(row => row.IdempotencyKey == outbox.IdempotencyKey, token);
                    if (record is not null)
                        await FailAsync(db, audit, outbox, record, record.InvoiceId.ToString(),
                            error.GetType().Name, true, clock.GetUtcNow(), token);
                }
                catch (Exception saveError) when (saveError is not OperationCanceledException)
                {
                    log.LogError(saveError, "Finance outbox event {EventId} failure state was not saved", outbox.Id);
                }
                processed++;
            }
        }
        return processed;
    }

    private static async Task FailAsync(ScmosDbContext db, AuditService audit,
        IntegrationOutboxEvent outbox, BillingFinanceRecord record, string label,
        string message, bool retryable, DateTimeOffset now, CancellationToken token)
    {
        var before = record.Status;
        outbox.Attempts++; outbox.LastAttemptAt = now; outbox.LockedUntil = null;
        outbox.LastError = Cut(message, 800); outbox.UpdatedAt = now;
        record.Attempts = outbox.Attempts; record.LastAttemptAt = now;
        record.ResponseCode = "DISPATCH_ERROR"; record.ResponseMessage = Cut(message, 800); record.UpdatedAt = now;
        var next = retryable ? FinanceRetry.Next(outbox.Attempts, now) : null;
        if (next is null)
        {
            outbox.Status = OutboxStatus.Dead; record.Status = FinanceStatus.Failed; record.NextAttemptAt = null;
        }
        else
        {
            outbox.Status = OutboxStatus.Retry; outbox.NextAttemptAt = next.Value;
            record.Status = FinanceStatus.Retrying; record.NextAttemptAt = next.Value;
        }
        audit.Stage(SystemActor(), AuditActions.Update, "billing-finance-record", record.Id.ToString(),
            label, "status", before, record.Status, Cut(message, 400), "integration");
        await db.SaveChangesAsync(token);
    }

    private static AppUser SystemActor() => new("finance-adapter", "", "Finance Adapter",
        "System", "", "system", true);

    private static string Cut(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
