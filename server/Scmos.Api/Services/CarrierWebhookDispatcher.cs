using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Sends the deliveries that are due, a few at a time, every twenty
/// seconds. Each goes as one POST: the envelope as JSON, signed under the
/// webhook's secret with the timestamp inside the signature, the event's
/// type, the delivery's id and the correlation id in headers. A 2xx is a
/// delivery; anything else, a timeout or a refused connection is a
/// failure, tried again on <see cref="CarrierWebhooks.Delays"/> and then
/// left dead in the ledger. An "assignment.offered" is filled out with
/// the assignment as the TMS would read it, at the moment of sending, so
/// a retry an hour later carries the job as it is then.
///
/// <para>
/// The connection itself is made by <see cref="ConnectAsync"/>: the host
/// is resolved at that moment and a private address is refused, whatever
/// the URL looked like at registration; a redirect is not followed. A
/// webhook that fails <see cref="CarrierWebhooks.RetiresAfter"/> times in
/// a row is disabled by SCMOS and its row says so.
/// </para>
/// </summary>
public class CarrierWebhookDispatcher(IServiceProvider services, IHttpClientFactory clients, ILogger<CarrierWebhookDispatcher> log) : BackgroundService
{
    public const string ClientName = "carrier-webhook";
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);
    private const int Batch = 25;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stopping); }
        catch (OperationCanceledException) { return; }

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                var sent = await SendDueAsync(stopping);
                if (sent > 0) log.LogInformation("Carrier webhooks: {Count} delivery attempt(s)", sent);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception problem)
            {
                log.LogError(problem, "Carrier webhook pass failed");
            }
            try { await Task.Delay(Tick, stopping); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass: every due delivery to an active webhook, oldest first. Returns how many were attempted.</summary>
    public async Task<int> SendDueAsync(CancellationToken token)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        var carriers = scope.ServiceProvider.GetRequiredService<CarrierService>();
        var now = DateTimeOffset.UtcNow;

        var due = await db.CarrierWebhookDeliveries
            .Where(one => one.Status == CarrierWebhooks.Pending && one.NextAttemptAt <= now)
            .OrderBy(one => one.NextAttemptAt)
            .Take(Batch)
            .ToListAsync(token);
        if (due.Count == 0) return 0;

        var hookIds = due.Select(one => one.WebhookId).Distinct().ToList();
        var hooks = await db.CarrierWebhooks.Where(one => hookIds.Contains(one.Id)).ToDictionaryAsync(one => one.Id, token);
        var supplierIds = hooks.Values.Select(one => one.SupplierId).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(one => supplierIds.Contains(one.Id)).ToDictionaryAsync(one => one.Id, token);
        var portals = new Dictionary<int, CarrierService.Portal>();
        var attempted = 0;

        foreach (var delivery in due)
        {
            if (!hooks.TryGetValue(delivery.WebhookId, out var hook) || hook.Status != CarrierWebhooks.Active)
            {
                // A disabled webhook takes nothing more; what was queued is closed out, not retried.
                delivery.Status = CarrierWebhooks.Dead;
                delivery.LastError = "webhook disabled";
                continue;
            }

            var body = await EnvelopeAsync(delivery, hook, suppliers.GetValueOrDefault(hook.SupplierId), carriers, portals, token);
            var (status, error) = await PostAsync(hook, delivery, body, token);
            attempted++;
            delivery.Attempts++;
            delivery.LastStatusCode = status;
            delivery.LastError = error;
            hook.LastDeliveryAt = DateTimeOffset.UtcNow;
            hook.LastStatusCode = status;
            hook.LastError = error;

            if (status is { } code && CarrierWebhooks.IsDelivered(code))
            {
                delivery.Status = CarrierWebhooks.Delivered;
                delivery.DeliveredAt = DateTimeOffset.UtcNow;
                hook.FailedInARow = 0;
            }
            else
            {
                hook.FailedInARow++;
                var next = CarrierWebhooks.NextAttempt(delivery.Attempts, DateTimeOffset.UtcNow);
                if (next is { } at) delivery.NextAttemptAt = at;
                else delivery.Status = CarrierWebhooks.Dead;
                log.LogWarning("Carrier webhook {Hook}: delivery {Delivery} ({Type}) failed, attempt {Attempt}: {Status} {Error}",
                    hook.Id, delivery.Id, delivery.EventType, delivery.Attempts, status?.ToString() ?? "-", error);
                if (CarrierWebhooks.Retires(hook.FailedInARow))
                {
                    // Nothing has got through for at least fifteen hours: the
                    // receiver is gone. What is still queued to it is closed out
                    // on the next pass; the carrier registers again when mended.
                    hook.Status = CarrierWebhooks.Disabled;
                    hook.DisabledAt = DateTimeOffset.UtcNow;
                    hook.DisabledBy = "scmos";
                    hook.LastError = CarrierWebhooks.RetiredReason(hook.FailedInARow);
                    log.LogWarning("Carrier webhook {Hook} ({Url}) disabled: {Count} deliveries failed in a row", hook.Id, hook.Url, hook.FailedInARow);
                }
            }
        }
        await db.SaveChangesAsync(token);
        return attempted;
    }

    /// <summary>
    /// The handler behind the webhook client: no redirects (a 3xx is a
    /// failure like any other answer, so a receiver cannot send SCMOS on
    /// to an address the rules would refuse), no cookies, and every
    /// connection through <see cref="ConnectAsync"/>.
    /// </summary>
    public static SocketsHttpHandler Handler(bool allowLoopback) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = (context, token) => ConnectAsync(context.DnsEndPoint, allowLoopback, token),
    };

    /// <summary>
    /// Resolves the host here and now and refuses to connect when any of
    /// its addresses is private (<see cref="CarrierWebhooks.AddressProblem"/>);
    /// the refusal surfaces as the delivery's error, and is retried on the
    /// schedule like a refused connection, since a name can change.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, bool allowLoopback, CancellationToken token)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(endpoint.Host, out var literal)) addresses = [literal];
        else
        {
            try { addresses = await Dns.GetHostAddressesAsync(endpoint.Host, token); }
            catch (SocketException error) { throw new HttpRequestException($"host could not be resolved: {error.SocketErrorCode}"); }
        }
        if (CarrierWebhooks.AddressProblem(addresses, allowLoopback) is { } problem) throw new HttpRequestException(problem);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The body as sent: id, type, when, the correlation id, and the data —
    /// an offer's data carrying the assignment as the TMS would read it
    /// from the API, when the job is still in the carrier's lists.
    /// </summary>
    private static async Task<string> EnvelopeAsync(CarrierWebhookDelivery delivery, CarrierWebhook hook, Supplier? supplier,
        CarrierService carriers, Dictionary<int, CarrierService.Portal> portals, CancellationToken token)
    {
        JsonElement data;
        try { data = JsonDocument.Parse(delivery.Payload.Length > 0 ? delivery.Payload : "{}").RootElement.Clone(); }
        catch (JsonException) { data = JsonDocument.Parse("{}").RootElement.Clone(); }

        object? assignment = null;
        if (delivery.EventType == CarrierWebhooks.Offered && supplier is not null
            && data.TryGetProperty("jobKey", out var keyElement) && keyElement.ValueKind == JsonValueKind.String)
        {
            try
            {
                if (!portals.TryGetValue(hook.SupplierId, out var portal))
                {
                    portal = await carriers.ReadForAsync(supplier, token);
                    portals[hook.SupplierId] = portal;
                }
                var key = keyElement.GetString() ?? "";
                assignment = Endpoints.CarrierApiEndpoints.Assignments(portal).FirstOrDefault(one => one.Id == key);
            }
            catch (Exception) { assignment = null; }
        }

        return JsonSerializer.Serialize(new
        {
            id = delivery.Id,
            type = delivery.EventType,
            at = delivery.CreatedAt,
            attempt = delivery.Attempts + 1,
            correlationId = delivery.CorrelationId,
            data,
            assignment,
        }, Json);
    }

    /// <summary>One POST, signed; the status it got, or null with the reason when it got none.</summary>
    private async Task<(int? Status, string Error)> PostAsync(CarrierWebhook hook, CarrierWebhookDelivery delivery, string body, CancellationToken token)
    {
        var client = clients.CreateClient(ClientName);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, hook.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Scmos-Event", delivery.EventType);
        request.Headers.TryAddWithoutValidation("X-Scmos-Delivery", delivery.Id.ToString());
        request.Headers.TryAddWithoutValidation("X-Scmos-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Scmos-Signature", CarrierWebhooks.Signature(hook.Secret, timestamp, body));
        if (delivery.CorrelationId.Length > 0) request.Headers.TryAddWithoutValidation(CarrierApi.CorrelationHeader, delivery.CorrelationId);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SCMOS-Webhook", "1.0"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(CarrierWebhooks.Timeout);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var code = (int)response.StatusCode;
            return (code, CarrierWebhooks.IsDelivered(code) ? "" : $"HTTP {code}");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return (null, $"no answer within {CarrierWebhooks.Timeout.TotalSeconds:0}s");
        }
        catch (HttpRequestException error)
        {
            // The message names the host and the failure, never the secret.
            var reason = error.Message.Length > 300 ? error.Message[..300] : error.Message;
            return (null, reason);
        }
    }
}
