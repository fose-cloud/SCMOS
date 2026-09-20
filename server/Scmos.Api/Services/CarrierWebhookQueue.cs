using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Puts an event on the way to every webhook that asked for it. Called
/// from the places the fact happens — a request made, a request
/// withdrawn, a queued event decided — and never lets a webhook's trouble
/// become the caller's: a failure to queue is logged and the operator's
/// save goes through. The sending is <see cref="CarrierWebhookDispatcher"/>'s.
/// </summary>
public class CarrierWebhookQueue(ScmosDbContext db, ILogger<CarrierWebhookQueue> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A carrier was asked for a truck. The carrier is named as the register spells it; every supplier that name resolves to is told.</summary>
    public Task OfferedAsync(string jobKey, string carrier, long requestId, int? quotedPrice, DateTimeOffset requestedAt, string correlationId, CancellationToken token) =>
        ForCarrierAsync(carrier, CarrierWebhooks.Offered, $"request:{requestId}",
            new { jobKey, requestId, quotedPrice, requestedAt }, correlationId, token);

    /// <summary>The request was withdrawn — taken by another carrier, cancelled by the operator, or answered no-response.</summary>
    public Task CancelledAsync(string jobKey, string carrier, long requestId, string reason, string correlationId, CancellationToken token) =>
        ForCarrierAsync(carrier, CarrierWebhooks.Cancelled, $"request:{requestId}",
            new { jobKey, requestId, reason }, correlationId, token);

    /// <summary>A status event the TMS queued was approved or set aside.</summary>
    public async Task EventDecidedAsync(LineEvent row, string state, string to, string by, CancellationToken token)
    {
        var ev = LineReadings.EventOf(row);
        if (ev is null) return;
        await ForSupplierAsync(ev.SupplierId, CarrierWebhooks.EventDecided, $"event:{row.Id}:{state}",
            new { eventId = row.Id, jobKey = ev.JobKey, type = ev.Type, at = ev.At, state, to, decidedAt = DateTimeOffset.UtcNow, decidedBy = by },
            ev.CorrelationId, token);
    }

    /// <summary>A test delivery to one webhook, on request.</summary>
    public async Task<long?> PingAsync(CarrierWebhook hook, string correlationId, CancellationToken token)
    {
        var delivery = Delivery(hook, CarrierWebhooks.Ping, $"ping:{Guid.NewGuid():N}",
            new { message = "SCMOS webhook test", webhookId = hook.Id }, correlationId);
        db.CarrierWebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(token);
        return delivery.Id;
    }

    /// <summary>Every supplier the register's spelling of a carrier resolves to: by name, by code, or by alias.</summary>
    private async Task<List<int>> SupplierIdsOfAsync(string carrier, CancellationToken token)
    {
        var name = carrier.Trim();
        if (name.Length == 0) return [];
        var direct = await db.Suppliers.AsNoTracking()
            .Where(one => one.Name == name || one.Code == name)
            .Select(one => one.Id)
            .ToListAsync(token);
        var aliased = await db.SupplierAliases.AsNoTracking()
            .Where(one => one.Alias == name)
            .Select(one => one.SupplierId)
            .ToListAsync(token);
        return direct.Concat(aliased).Distinct().ToList();
    }

    private async Task ForCarrierAsync(string carrier, string type, string key, object data, string correlationId, CancellationToken token)
    {
        try
        {
            foreach (var supplierId in await SupplierIdsOfAsync(carrier, token))
                await ForSupplierAsync(supplierId, type, key, data, correlationId, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogWarning(error, "Carrier webhook: could not queue {Type} for {Carrier}", type, carrier);
        }
    }

    private async Task ForSupplierAsync(int supplierId, string type, string key, object data, string correlationId, CancellationToken token)
    {
        try
        {
            var hooks = await db.CarrierWebhooks.AsNoTracking()
                .Where(one => one.SupplierId == supplierId && one.Status == CarrierWebhooks.Active)
                .ToListAsync(token);
            var queued = 0;
            foreach (var hook in hooks.Where(hook => CarrierWebhooks.Wants(hook.Events, type)))
            {
                // One event reaches one webhook once, whatever path queued it
                // twice; the unique index is the guard and a duplicate is not
                // an error.
                var exists = await db.CarrierWebhookDeliveries.AsNoTracking()
                    .AnyAsync(one => one.WebhookId == hook.Id && one.EventType == type && one.EventKey == key, token);
                if (exists) continue;
                db.CarrierWebhookDeliveries.Add(Delivery(hook, type, key, data, correlationId));
                queued++;
            }
            if (queued > 0)
            {
                try { await db.SaveChangesAsync(token); }
                catch (DbUpdateException) { /* raced with another queuing of the same event: it is queued */ }
                log.LogInformation("Carrier webhook: {Type} queued to {Count} webhook(s) of supplier {Supplier}", type, queued, supplierId);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogWarning(error, "Carrier webhook: could not queue {Type} for supplier {Supplier}", type, supplierId);
        }
    }

    private static CarrierWebhookDelivery Delivery(CarrierWebhook hook, string type, string key, object data, string correlationId) => new()
    {
        WebhookId = hook.Id,
        EventType = type,
        EventKey = key,
        Payload = JsonSerializer.Serialize(data, Json),
        Attempts = 0,
        NextAttemptAt = DateTimeOffset.UtcNow,
        Status = CarrierWebhooks.Pending,
        CreatedAt = DateTimeOffset.UtcNow,
        CorrelationId = correlationId.Length > 64 ? correlationId[..64] : correlationId,
    };
}
