using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

public class BillingRequirementRule
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string DocumentKind { get; set; } = "";
    public string Customer { get; set; } = "";
    public int? SupplierId { get; set; }
    public string ServiceType { get; set; } = "";
    public string ShipmentType { get; set; } = "";
    public string ChargeType { get; set; } = "";
    public string SpecificRequirement { get; set; } = "";
    public bool Required { get; set; } = true;
    public bool Blocking { get; set; } = true;
    public int Priority { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool Active { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public class BillingRequirementSnapshot
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public long RuleId { get; set; }
    public string RuleCode { get; set; } = "";
    public string DocumentKind { get; set; } = "";
    public bool Required { get; set; }
    public bool Blocking { get; set; }
    public int Priority { get; set; }
    public bool Satisfied { get; set; }
    public long? DocumentId { get; set; }
    public DateTimeOffset SnapshottedAt { get; set; }
}

public class BillingTaxRule
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string TaxType { get; set; } = "";
    public decimal Rate { get; set; }
    public string Customer { get; set; } = "";
    public int? SupplierId { get; set; }
    public string ServiceType { get; set; } = "";
    public string ShipmentType { get; set; } = "";
    public int Priority { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool Active { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public class BillingAdditionalCharge
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string ChargeType { get; set; } = "";
    public decimal RequestedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string Currency { get; set; } = "THB";
    public string Reason { get; set; } = "";
    public long? EvidenceDocumentId { get; set; }
    public string Status { get; set; } = "REQUESTED";
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset? ApprovedAt { get; set; }
}

public class BillingValidationRun
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public int Sequence { get; set; }
    public string Outcome { get; set; } = "";
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}

public class BillingValidationResult
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public long InvoiceId { get; set; }
    public int Sequence { get; set; }
    public string Step { get; set; } = "";
    public string Code { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Blocking { get; set; }
    public string Message { get; set; } = "";
    public decimal? ExpectedAmount { get; set; }
    public decimal? ActualAmount { get; set; }
    public string Currency { get; set; } = "";
    public string EvidenceType { get; set; } = "";
    public string EvidenceId { get; set; } = "";
    public string EvidenceVersion { get; set; } = "";
    public string RuleSource { get; set; } = "";
    public DateOnly? EffectiveDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class BillingReviewEvent
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public int Cycle { get; set; }
    public string Action { get; set; } = "";
    public string FromStatus { get; set; } = "";
    public string ToStatus { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string Remark { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string ActorName { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public static class CarrierBillingValidationModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<BillingRequirementRule>(e => {
            e.ToTable("billing_requirement_rules"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            Text(e, x => x.Code, "code", 80); Text(e, x => x.DocumentKind, "document_kind", 60); Text(e, x => x.Customer, "customer", 160);
            e.Property(x => x.SupplierId).HasColumnName("supplier_id"); Text(e, x => x.ServiceType, "service_type", 60); Text(e, x => x.ShipmentType, "shipment_type", 60);
            Text(e, x => x.ChargeType, "charge_type", 60); Text(e, x => x.SpecificRequirement, "specific_requirement", 240);
            e.Property(x => x.Required).HasColumnName("required"); e.Property(x => x.Blocking).HasColumnName("blocking"); e.Property(x => x.Priority).HasColumnName("priority");
            Date(e, x => x.EffectiveFrom, "effective_from"); Date(e, x => x.EffectiveTo, "effective_to"); e.Property(x => x.Active).HasColumnName("active");
            Text(e, x => x.UpdatedBy, "updated_by", 120); e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.Code, x.EffectiveFrom }).IsUnique().HasDatabaseName("billing_requirement_rule_code_date_idx");
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BillingRequirementSnapshot>(e => {
            e.ToTable("billing_requirement_snapshots"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id"); e.Property(x => x.RuleId).HasColumnName("rule_id"); Text(e, x => x.RuleCode, "rule_code", 80);
            Text(e, x => x.DocumentKind, "document_kind", 60); e.Property(x => x.Required).HasColumnName("required"); e.Property(x => x.Blocking).HasColumnName("blocking");
            e.Property(x => x.Priority).HasColumnName("priority"); e.Property(x => x.Satisfied).HasColumnName("satisfied"); e.Property(x => x.DocumentId).HasColumnName("document_id");
            e.Property(x => x.SnapshottedAt).HasColumnName("snapshotted_at"); e.HasIndex(x => new { x.InvoiceId, x.RuleId }).IsUnique();
            e.HasOne<BillingInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BillingRequirementRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BillingTaxRule>(e => {
            e.ToTable("billing_tax_rules"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            Text(e, x => x.Code, "code", 80); Text(e, x => x.TaxType, "tax_type", 40); e.Property(x => x.Rate).HasColumnName("rate").HasPrecision(9, 6);
            Text(e, x => x.Customer, "customer", 160); e.Property(x => x.SupplierId).HasColumnName("supplier_id"); Text(e, x => x.ServiceType, "service_type", 60);
            Text(e, x => x.ShipmentType, "shipment_type", 60); e.Property(x => x.Priority).HasColumnName("priority"); Date(e, x => x.EffectiveFrom, "effective_from");
            Date(e, x => x.EffectiveTo, "effective_to"); e.Property(x => x.Active).HasColumnName("active"); Text(e, x => x.UpdatedBy, "updated_by", 120); e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.Code, x.EffectiveFrom }).IsUnique();
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BillingAdditionalCharge>(e => {
            e.ToTable("billing_additional_charges"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd(); e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            Text(e, x => x.ChargeType, "charge_type", 60); Money(e, x => x.RequestedAmount, "requested_amount"); Money(e, x => x.ApprovedAmount, "approved_amount"); Text(e, x => x.Currency, "currency", 3);
            Text(e, x => x.Reason, "reason", 500); e.Property(x => x.EvidenceDocumentId).HasColumnName("evidence_document_id"); Text(e, x => x.Status, "status", 30);
            Text(e, x => x.RequestedBy, "requested_by", 120); e.Property(x => x.RequestedAt).HasColumnName("requested_at"); Text(e, x => x.ApprovedBy, "approved_by", 120); e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.HasOne<BillingInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<StoredDocument>().WithMany().HasForeignKey(x => x.EvidenceDocumentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BillingValidationRun>(e => {
            e.ToTable("billing_validation_runs"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd(); e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            e.Property(x => x.Sequence).HasColumnName("sequence"); Text(e, x => x.Outcome, "outcome", 20); Text(e, x => x.SubmittedBy, "submitted_by", 120); e.Property(x => x.StartedAt).HasColumnName("started_at"); e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.HasIndex(x => new { x.InvoiceId, x.Sequence }).IsUnique();
            e.HasOne<BillingInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<BillingValidationResult>(e => {
            e.ToTable("billing_validation_results"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd(); e.Property(x => x.RunId).HasColumnName("run_id"); e.Property(x => x.InvoiceId).HasColumnName("invoice_id"); e.Property(x => x.Sequence).HasColumnName("sequence");
            Text(e, x => x.Step, "step", 40); Text(e, x => x.Code, "code", 80); Text(e, x => x.Category, "category", 20); e.Property(x => x.Blocking).HasColumnName("blocking"); Text(e, x => x.Message, "message", 800);
            Money(e, x => x.ExpectedAmount, "expected_amount"); Money(e, x => x.ActualAmount, "actual_amount"); Text(e, x => x.Currency, "currency", 3); Text(e, x => x.EvidenceType, "evidence_type", 40); Text(e, x => x.EvidenceId, "evidence_id", 120); Text(e, x => x.EvidenceVersion, "evidence_version", 240); Text(e, x => x.RuleSource, "rule_source", 240); Date(e, x => x.EffectiveDate, "effective_date"); e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.InvoiceId, x.RunId, x.Sequence });
            e.HasOne<BillingValidationRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<BillingReviewEvent>(e => {
            e.ToTable("billing_review_events"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id"); e.Property(x => x.Cycle).HasColumnName("cycle");
            Text(e, x => x.Action, "action", 40); Text(e, x => x.FromStatus, "from_status", 40); Text(e, x => x.ToStatus, "to_status", 40);
            Text(e, x => x.ReasonCode, "reason_code", 60); Text(e, x => x.Remark, "remark", 800); Text(e, x => x.ActorId, "actor_id", 160); Text(e, x => x.ActorName, "actor_name", 160);
            e.Property(x => x.At).HasColumnName("at"); e.HasIndex(x => new { x.InvoiceId, x.Id });
            e.HasOne<BillingInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void Text<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T,string>> p, string name, int length) where T:class => e.Property(p).HasColumnName(name).HasMaxLength(length).HasDefaultValue("");
    private static void Date<T,TValue>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T,TValue>> p, string name) where T:class => e.Property(p).HasColumnName(name).HasColumnType("date");
    private static void Money<T,TValue>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T,TValue>> p, string name) where T:class => e.Property(p).HasColumnName(name).HasPrecision(18,2);
}
