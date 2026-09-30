using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>
/// A job a carrier keyed in itself, waiting for Leschaco (29 Sep 2026).
///
/// It is a request, never a job: nothing here reaches the register until a
/// person in the department opens it as the add-job form, checks it and saves
/// it through the ordinary path — the one with the owner, the validations and
/// the audit every other job has. Then the request is marked APPROVED with the
/// job it became. Refused with a reason, or withdrawn by the carrier while it
/// still waited, it stays as it was: nothing is deleted.
/// </summary>
public sealed class CarrierJobRequest
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Withdrawn = "WITHDRAWN";
    public static readonly string[] Statuses = [Pending, Approved, Rejected, Withdrawn];

    public long Id { get; set; }
    /// <summary>The carrier, from the account that keyed it — never from the request body.</summary>
    public int SupplierId { get; set; }
    /// <summary>Its name when keyed, so a list reads without a join.</summary>
    public string SupplierName { get; set; } = "";
    /// <summary>IMPORT · EXPORT · DELIVERY — which of the add-job form's layouts the fields belong to.</summary>
    public string Category { get; set; } = "";
    /// <summary>The add-job form's own fields, as a JSON object of name → value; only that layout's names.</summary>
    public string Fields { get; set; } = "{}";
    public string Note { get; set; } = "";
    public string Status { get; set; } = Pending;
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string DecidedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>Why it was refused, or withdrawn; empty when approved.</summary>
    public string DecisionNote { get; set; } = "";
    /// <summary>The register's key of the job it became.</summary>
    public string JobKey { get; set; } = "";
    public int Revision { get; set; }

    public static void Configure(ModelBuilder model)
    {
        model.Entity<CarrierJobRequest>(entry =>
        {
            entry.ToTable("carrier_job_requests", table =>
                table.HasCheckConstraint("carrier_job_requests_status_ck", "[status] IN ('PENDING','APPROVED','REJECTED','WITHDRAWN')"));
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).HasColumnName("id");
            entry.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entry.Property(e => e.SupplierName).HasColumnName("supplier_name").HasMaxLength(200);
            entry.Property(e => e.Category).HasColumnName("category").HasMaxLength(20);
            entry.Property(e => e.Fields).HasColumnName("fields").HasMaxLength(4000);
            entry.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
            entry.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entry.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(160);
            entry.Property(e => e.CreatedAt).HasColumnName("created_at");
            entry.Property(e => e.DecidedBy).HasColumnName("decided_by").HasMaxLength(160);
            entry.Property(e => e.DecidedAt).HasColumnName("decided_at");
            entry.Property(e => e.DecisionNote).HasColumnName("decision_note").HasMaxLength(500);
            entry.Property(e => e.JobKey).HasColumnName("job_key").HasMaxLength(100);
            entry.Property(e => e.Revision).HasColumnName("revision").IsConcurrencyToken();
            entry.HasIndex(e => new { e.Status, e.CreatedAt }).HasDatabaseName("carrier_job_requests_status_idx");
            entry.HasIndex(e => new { e.SupplierId, e.CreatedAt }).HasDatabaseName("carrier_job_requests_supplier_idx");
        });
    }
}
