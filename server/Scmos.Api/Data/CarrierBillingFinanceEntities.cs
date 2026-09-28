using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// The durable SCMOS-side record of one invoice handed to the Finance boundary.
/// PayloadJson is the immutable canonical snapshot that was approved for release;
/// later invoice edits cannot silently change what Finance received.
/// </summary>
public class BillingFinanceRecord
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string Status { get; set; } = FinanceStatus.Queued;
    public string Adapter { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string ExternalReference { get; set; } = "";
    public string ResponseCode { get; set; } = "";
    public string ResponseMessage { get; set; } = "";
    public string PaymentReference { get; set; } = "";
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
    public string ReconciledBy { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// A transactional outbox event. It is inserted in the same database transaction
/// that moves the invoice to FINANCE_PROCESSING, then a background dispatcher
/// delivers it through IFinanceAdapter. The unique idempotency key and adapter
/// key make retries safe on both sides of the boundary.
/// </summary>
public class IntegrationOutboxEvent
{
    public long Id { get; set; }
    public string EventType { get; set; } = "";
    public string AggregateType { get; set; } = "";
    public string AggregateId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string LastError { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class CarrierBillingFinanceModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<BillingFinanceRecord>(entry =>
        {
            entry.ToTable("billing_finance_records", table => table.HasCheckConstraint(
                "billing_finance_records_status_ck",
                "[status] IN ('QUEUED','PROCESSING','RETRYING','SUBMITTED','ACCEPTED','REJECTED','PAID','CLOSED','FAILED')"));
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(row => row.InvoiceId).HasColumnName("invoice_id");
            Text(entry, row => row.Status, "status", 24, FinanceStatus.Queued);
            Text(entry, row => row.Adapter, "adapter", 60);
            Text(entry, row => row.IdempotencyKey, "idempotency_key", 120);
            entry.Property(row => row.PayloadJson).HasColumnName("payload_json").HasColumnType("nvarchar(max)");
            Text(entry, row => row.PayloadHash, "payload_hash", 64);
            entry.Property(row => row.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            entry.Property(row => row.LastAttemptAt).HasColumnName("last_attempt_at");
            entry.Property(row => row.NextAttemptAt).HasColumnName("next_attempt_at");
            Text(entry, row => row.ExternalReference, "external_reference", 160);
            Text(entry, row => row.ResponseCode, "response_code", 80);
            Text(entry, row => row.ResponseMessage, "response_message", 800);
            Text(entry, row => row.PaymentReference, "payment_reference", 160);
            entry.Property(row => row.PaidAt).HasColumnName("paid_at");
            entry.Property(row => row.ReconciledAt).HasColumnName("reconciled_at");
            Text(entry, row => row.ReconciledBy, "reconciled_by", 160);
            Text(entry, row => row.CreatedBy, "created_by", 160);
            entry.Property(row => row.CreatedAt).HasColumnName("created_at");
            entry.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entry.Property(row => row.RowVersion).HasColumnName("row_version").IsRowVersion();
            entry.HasIndex(row => row.InvoiceId).IsUnique().HasDatabaseName("billing_finance_records_invoice_idx");
            entry.HasIndex(row => row.IdempotencyKey).IsUnique().HasDatabaseName("billing_finance_records_idempotency_idx");
            entry.HasIndex(row => new { row.Status, row.NextAttemptAt }).HasDatabaseName("billing_finance_records_status_due_idx");
            entry.HasOne<BillingInvoice>().WithMany().HasForeignKey(row => row.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_billing_finance_records_billing_invoices_invoice_id");
        });

        model.Entity<IntegrationOutboxEvent>(entry =>
        {
            entry.ToTable("integration_outbox", table => table.HasCheckConstraint(
                "integration_outbox_status_ck", "[status] IN ('PENDING','PROCESSING','RETRY','COMPLETED','DEAD')"));
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
            Text(entry, row => row.EventType, "event_type", 80);
            Text(entry, row => row.AggregateType, "aggregate_type", 80);
            Text(entry, row => row.AggregateId, "aggregate_id", 120);
            Text(entry, row => row.IdempotencyKey, "idempotency_key", 120);
            entry.Property(row => row.PayloadJson).HasColumnName("payload_json").HasColumnType("nvarchar(max)");
            Text(entry, row => row.Status, "status", 16, OutboxStatus.Pending);
            entry.Property(row => row.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            entry.Property(row => row.NextAttemptAt).HasColumnName("next_attempt_at");
            entry.Property(row => row.LockedUntil).HasColumnName("locked_until");
            entry.Property(row => row.LastAttemptAt).HasColumnName("last_attempt_at");
            entry.Property(row => row.ProcessedAt).HasColumnName("processed_at");
            Text(entry, row => row.LastError, "last_error", 800);
            Text(entry, row => row.CorrelationId, "correlation_id", 64);
            entry.Property(row => row.CreatedAt).HasColumnName("created_at");
            entry.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entry.Property(row => row.RowVersion).HasColumnName("row_version").IsRowVersion();
            entry.HasIndex(row => row.IdempotencyKey).IsUnique().HasDatabaseName("integration_outbox_idempotency_idx");
            entry.HasIndex(row => new { row.Status, row.NextAttemptAt }).HasDatabaseName("integration_outbox_due_idx");
            entry.HasIndex(row => new { row.AggregateType, row.AggregateId }).HasDatabaseName("integration_outbox_aggregate_idx");
        });
    }

    private static void Text<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entry,
        System.Linq.Expressions.Expression<Func<TEntity, string>> property, string name, int length,
        string defaultValue = "") where TEntity : class =>
        entry.Property(property).HasColumnName(name).HasMaxLength(length).HasDefaultValue(defaultValue);
}
