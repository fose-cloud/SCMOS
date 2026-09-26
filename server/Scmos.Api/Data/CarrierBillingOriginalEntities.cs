using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// The physical package for one online invoice. Carrier dispatch information and
/// LESCHACO receipt evidence share one row so they cannot drift into two packages.
/// </summary>
public class OriginalDocumentPackage
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string Status { get; set; } = OriginalDocumentStatus.Pending;
    public DateOnly? SentDate { get; set; }
    public string Courier { get; set; } = "";
    public string TrackingNumber { get; set; } = "";
    public string CarrierPackageReference { get; set; } = "";
    public string CarrierRemark { get; set; } = "";
    public string SentBy { get; set; } = "";
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string ReceivedById { get; set; } = "";
    public string ReceivedByName { get; set; } = "";
    public int? DocumentCount { get; set; }
    public string ReceiptPackageReference { get; set; } = "";
    public string ReceiptRemark { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class CarrierBillingOriginalModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<OriginalDocumentPackage>(entry =>
        {
            entry.ToTable("billing_original_packages", table =>
            {
                table.HasCheckConstraint("billing_original_packages_status_ck", "[status] IN ('PENDING','SENT','RECEIVED')");
                table.HasCheckConstraint("billing_original_packages_count_ck", "[document_count] IS NULL OR [document_count] > 0");
            });
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(row => row.InvoiceId).HasColumnName("invoice_id");
            entry.Property(row => row.Status).HasColumnName("status").HasMaxLength(20);
            entry.Property(row => row.SentDate).HasColumnName("sent_date").HasColumnType("date");
            Text(entry, row => row.Courier, "courier", 120);
            Text(entry, row => row.TrackingNumber, "tracking_number", 160);
            Text(entry, row => row.CarrierPackageReference, "carrier_package_reference", 160);
            Text(entry, row => row.CarrierRemark, "carrier_remark", 800);
            Text(entry, row => row.SentBy, "sent_by", 160);
            entry.Property(row => row.SentAt).HasColumnName("sent_at");
            entry.Property(row => row.ReceivedAt).HasColumnName("received_at");
            Text(entry, row => row.ReceivedById, "received_by_id", 160);
            Text(entry, row => row.ReceivedByName, "received_by_name", 160);
            entry.Property(row => row.DocumentCount).HasColumnName("document_count");
            Text(entry, row => row.ReceiptPackageReference, "receipt_package_reference", 160);
            Text(entry, row => row.ReceiptRemark, "receipt_remark", 800);
            entry.Property(row => row.CreatedAt).HasColumnName("created_at");
            entry.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entry.Property(row => row.RowVersion).HasColumnName("row_version").IsRowVersion();
            entry.HasIndex(row => row.InvoiceId).IsUnique().HasDatabaseName("billing_original_packages_invoice_idx");
            entry.HasIndex(row => new { row.Status, row.ReceivedAt }).HasDatabaseName("billing_original_packages_status_received_idx");
            entry.HasOne<BillingInvoice>().WithMany().HasForeignKey(row => row.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_billing_original_packages_billing_invoices_invoice_id");
        });
    }

    private static void Text(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<OriginalDocumentPackage> entry,
        System.Linq.Expressions.Expression<Func<OriginalDocumentPackage, string>> property, string name, int length) =>
        entry.Property(property).HasColumnName(name).HasMaxLength(length).HasDefaultValue("");
}
