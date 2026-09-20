using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// A URL a carrier's TMS asked SCMOS to call, and the events it asked for.
/// Belongs to the supplier (any of the supplier's keys may manage it) and
/// remembers which key registered it. The secret signs every delivery; it
/// is shown once and never logged. Disabled, never deleted: the deliveries
/// keep pointing at it.
/// </summary>
public class CarrierWebhook
{
    public long Id { get; set; }
    public int SupplierId { get; set; }
    public long ClientRowId { get; set; }
    public string Url { get; set; } = "";
    public string Secret { get; set; } = "";
    /// <summary>Comma-separated — see <see cref="CarrierWebhooks.SplitEvents"/>.</summary>
    public string Events { get; set; } = "";
    public string Status { get; set; } = CarrierWebhooks.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? DisabledAt { get; set; }
    public string DisabledBy { get; set; } = "";
    public DateTimeOffset? LastDeliveryAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string LastError { get; set; } = "";
    /// <summary>Deliveries that failed since the last one that got through — what the screen shows as a webhook in trouble.</summary>
    public int FailedInARow { get; set; }

    public static void Configure(ModelBuilder model)
    {
        model.Entity<CarrierWebhook>(entry =>
        {
            entry.ToTable("carrier_webhooks");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entry.Property(e => e.ClientRowId).HasColumnName("client_row_id");
            entry.Property(e => e.Url).HasColumnName("url").HasMaxLength(500);
            entry.Property(e => e.Secret).HasColumnName("secret").HasMaxLength(64);
            entry.Property(e => e.Events).HasColumnName("events").HasMaxLength(200).HasDefaultValue("");
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue(CarrierWebhooks.Active);
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.DisabledAt).HasColumnName("disabled_at");
            entry.Property(e => e.DisabledBy).HasColumnName("disabled_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(e => e.LastDeliveryAt).HasColumnName("last_delivery_at");
            entry.Property(e => e.LastStatusCode).HasColumnName("last_status_code");
            entry.Property(e => e.LastError).HasColumnName("last_error").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.FailedInARow).HasColumnName("failed_in_a_row").HasDefaultValue(0);
            entry.HasIndex(e => e.SupplierId).HasDatabaseName("carrier_webhooks_supplier_idx");
        });
    }
}

/// <summary>
/// One event on its way to one webhook: the body as it will be sent, how
/// many times it has been tried, when it is next due, and what the TMS
/// last answered. Delivered or dead, it stays — the ledger is how a
/// carrier and the department see what was sent.
/// </summary>
public class CarrierWebhookDelivery
{
    public long Id { get; set; }
    public long WebhookId { get; set; }
    public string EventType { get; set; } = "";
    /// <summary>What the event is about — "request:123", "event:10043", "ping:…" — so one event reaches one webhook once.</summary>
    public string EventKey { get; set; } = "";
    public string Payload { get; set; } = "";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string Status { get; set; } = CarrierWebhooks.Pending;
    public int? LastStatusCode { get; set; }
    public string LastError { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string CorrelationId { get; set; } = "";

    public static void Configure(ModelBuilder model)
    {
        model.Entity<CarrierWebhookDelivery>(entry =>
        {
            entry.ToTable("carrier_webhook_deliveries");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(e => e.WebhookId).HasColumnName("webhook_id");
            entry.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(40);
            entry.Property(e => e.EventKey).HasColumnName("event_key").HasMaxLength(80);
            entry.Property(e => e.Payload).HasColumnName("payload").HasDefaultValue("");
            entry.Property(e => e.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            entry.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at");
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue(CarrierWebhooks.Pending);
            entry.Property(e => e.LastStatusCode).HasColumnName("last_status_code");
            entry.Property(e => e.LastError).HasColumnName("last_error").HasMaxLength(400).HasDefaultValue("");
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.DeliveredAt).HasColumnName("delivered_at");
            entry.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64).HasDefaultValue("");
            entry.HasIndex(e => new { e.WebhookId, e.EventType, e.EventKey }).IsUnique().HasDatabaseName("carrier_webhook_deliveries_event_idx");
            entry.HasIndex(e => new { e.Status, e.NextAttemptAt }).HasDatabaseName("carrier_webhook_deliveries_due_idx");
        });
    }
}
