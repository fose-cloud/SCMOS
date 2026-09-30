using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// One billing lifecycle for one SCMOS job. The unique JobKey is the database
/// half of Delivery Complete idempotency; retries find this row instead of
/// opening another financial process.
/// </summary>
public class BillingCase
{
    public long Id { get; set; }
    public string JobKey { get; set; } = "";
    public int SupplierId { get; set; }
    public long AssignmentId { get; set; }
    public string Status { get; set; } = BillingCaseStatus.WaitingCarrierSubmission;
    public DateTimeOffset DeliveryCompletedAt { get; set; }
    public string SlaRuleCode { get; set; } = "";
    public long? SlaRuleId { get; set; }
    public string SlaStartDay { get; set; } = "";
    public int? SlaTargetWorkingDays { get; set; }
    public DateOnly? SlaStartDate { get; set; }
    public DateOnly? SlaDueDate { get; set; }
    public string SlaIssueCode { get; set; } = "";
    public string SlaIssue { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// A carrier's online pre-billing header. Phase 4 creates and edits Draft only;
/// the wider status vocabulary is declared now so later phases can advance the
/// same row instead of replacing the table.
/// </summary>
public class BillingInvoice
{
    public long Id { get; set; }
    public int SupplierId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public DateOnly? InvoiceDate { get; set; }
    public string Currency { get; set; } = "THB";
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = BillingInvoiceStatus.Draft;
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public DateTimeOffset? ReviewSubmittedAt { get; set; }
    public DateTimeOffset? ReviewDecidedAt { get; set; }
    public DateTimeOffset? OnlineApprovedAt { get; set; }
    public int ReviewCycle { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // The invoice form's own fields (30 Sep 2026, laid out as the department lead's sample ใบแจ้งหนี้).
    /// <summary>Days the department has to pay, counted from the invoice date.</summary>
    public int CreditTermDays { get; set; } = 30;
    public DateOnly? DueDate { get; set; }
    /// <summary>The customer's P/O number, when there is one.</summary>
    public string PoNumber { get; set; } = "";
    /// <summary>Leschaco's job number, as the invoice's JOB NO. box names it.</summary>
    public string JobNo { get; set; } = "";
    /// <summary>How the carrier asks to be paid — the cheque and bank lines at the foot of its form.</summary>
    public string PaymentNote { get; set; } = "";
    /// <summary>Who at the carrier prepared the invoice.</summary>
    public string PreparedBy { get; set; } = "";
    /// <summary>Withholding tax deducted: 1% of the transportation charge (<see cref="Rules.InvoiceLines"/>).</summary>
    public decimal WithholdingAmount { get; set; }
    /// <summary>What the carrier is paid: the total less the withholding tax.</summary>
    public decimal NetAmount { get; set; }
}

/// <summary>
/// One line of a carrier's invoice (30 Sep 2026): a code from <see cref="Rules.InvoiceLines"/>, how many, at what.
/// The amount is kept as worked out when saved, so the paper and the row always read the same.
/// </summary>
public class BillingInvoiceLine
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string Code { get; set; } = "";
    public int Position { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    /// <summary>1.1's route line, as printed under the trucking order.</summary>
    public string Description { get; set; } = "";
    /// <summary>1.1's container line: size, container number, plate.</summary>
    public string Detail { get; set; } = "";
}

/// <summary>
/// The explicit invoice-to-job bridge. Phase 4 links one eligible case when a
/// draft is created; the shape supports a future configured multi-job invoice
/// without changing the job or invoice identity.
/// </summary>
public class BillingInvoiceJobLink
{
    public long InvoiceId { get; set; }
    public long BillingCaseId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public string LinkedBy { get; set; } = "";
    public BillingInvoice Invoice { get; set; } = null!;
    public BillingCase BillingCase { get; set; } = null!;
}

public static class CarrierBillingModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<BillingCase>(entry =>
        {
            entry.ToTable("billing_cases", table => table.HasCheckConstraint(
                "billing_cases_status_ck", "[status] IN ('WAITING_CARRIER_SUBMISSION','DRAFT','VALIDATED','BLOCKED','SUBCON_REVIEW','RETURNED','DISPUTED','AWAITING_ORIGINAL','ORIGINAL_RECEIVED','READY_FOR_FINANCE','FINANCE_PROCESSING','FINANCE_REJECTED','PAID','CLOSED')"));
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(row => row.JobKey).HasColumnName("job_key").HasMaxLength(80);
            entry.Property(row => row.SupplierId).HasColumnName("supplier_id");
            entry.Property(row => row.AssignmentId).HasColumnName("assignment_id");
            entry.Property(row => row.Status).HasColumnName("status").HasMaxLength(40);
            entry.Property(row => row.DeliveryCompletedAt).HasColumnName("delivery_completed_at");
            entry.Property(row => row.SlaRuleCode).HasColumnName("sla_rule_code").HasMaxLength(40).HasDefaultValue("");
            entry.Property(row => row.SlaRuleId).HasColumnName("sla_rule_id");
            entry.Property(row => row.SlaStartDay).HasColumnName("sla_start_day").HasMaxLength(4).HasDefaultValue("");
            entry.Property(row => row.SlaTargetWorkingDays).HasColumnName("sla_target_working_days");
            entry.Property(row => row.SlaStartDate).HasColumnName("sla_start_date").HasColumnType("date");
            entry.Property(row => row.SlaDueDate).HasColumnName("sla_due_date").HasColumnType("date");
            entry.Property(row => row.SlaIssueCode).HasColumnName("sla_issue_code").HasMaxLength(60).HasDefaultValue("");
            entry.Property(row => row.SlaIssue).HasColumnName("sla_issue").HasMaxLength(500).HasDefaultValue("");
            entry.Property(row => row.CreatedBy).HasColumnName("created_by").HasMaxLength(120);
            entry.Property(row => row.CreatedAt).HasColumnName("created_at");
            entry.Property(row => row.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120);
            entry.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entry.Property(row => row.RowVersion).HasColumnName("row_version").IsRowVersion();
            entry.HasIndex(row => row.JobKey).IsUnique().HasDatabaseName("billing_cases_job_idx");
            entry.HasIndex(row => new { row.SupplierId, row.Status }).HasDatabaseName("billing_cases_supplier_status_idx");
            entry.HasIndex(row => new { row.SlaDueDate, row.Status }).HasDatabaseName("billing_cases_due_status_idx");
            entry.HasOne<OperationJob>().WithMany().HasForeignKey(row => row.JobKey)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_billing_cases_operation_jobs_job_key");
            entry.HasOne<Supplier>().WithMany().HasForeignKey(row => row.SupplierId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_billing_cases_suppliers_supplier_id");
            entry.HasOne<SupplierRequest>().WithMany().HasForeignKey(row => row.AssignmentId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_billing_cases_supplier_requests_assignment_id");
            entry.HasOne<BillingSlaRule>().WithMany().HasForeignKey(row => row.SlaRuleId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_billing_cases_billing_sla_rules_sla_rule_id");
        });

        model.Entity<BillingInvoice>(entry =>
        {
            entry.ToTable("billing_invoices", table => table.HasCheckConstraint(
                "billing_invoices_amounts_ck", "[subtotal] >= 0 AND [tax_amount] >= 0 AND [total_amount] >= 0"));
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(row => row.SupplierId).HasColumnName("supplier_id");
            entry.Property(row => row.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(80).HasDefaultValue("");
            entry.Property(row => row.InvoiceDate).HasColumnName("invoice_date").HasColumnType("date");
            entry.Property(row => row.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("THB");
            entry.Property(row => row.Subtotal).HasColumnName("subtotal").HasPrecision(18, 2);
            entry.Property(row => row.TaxAmount).HasColumnName("tax_amount").HasPrecision(18, 2);
            entry.Property(row => row.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 2);
            entry.Property(row => row.Status).HasColumnName("status").HasMaxLength(40);
            entry.Property(row => row.CreatedBy).HasColumnName("created_by").HasMaxLength(120);
            entry.Property(row => row.CreatedAt).HasColumnName("created_at");
            entry.Property(row => row.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120);
            entry.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entry.Property(row => row.SubmittedAt).HasColumnName("submitted_at");
            entry.Property(row => row.ValidatedAt).HasColumnName("validated_at");
            entry.Property(row => row.ReviewSubmittedAt).HasColumnName("review_submitted_at");
            entry.Property(row => row.ReviewDecidedAt).HasColumnName("review_decided_at");
            entry.Property(row => row.OnlineApprovedAt).HasColumnName("online_approved_at");
            entry.Property(row => row.ReviewCycle).HasColumnName("review_cycle").HasDefaultValue(0);
            entry.Property(row => row.RowVersion).HasColumnName("row_version").IsRowVersion();
            entry.Property(row => row.CreditTermDays).HasColumnName("credit_term_days").HasDefaultValue(30);
            entry.Property(row => row.DueDate).HasColumnName("due_date").HasColumnType("date");
            entry.Property(row => row.PoNumber).HasColumnName("po_number").HasMaxLength(80).HasDefaultValue("");
            entry.Property(row => row.JobNo).HasColumnName("job_no").HasMaxLength(80).HasDefaultValue("");
            entry.Property(row => row.PaymentNote).HasColumnName("payment_note").HasMaxLength(500).HasDefaultValue("");
            entry.Property(row => row.PreparedBy).HasColumnName("prepared_by").HasMaxLength(120).HasDefaultValue("");
            entry.Property(row => row.WithholdingAmount).HasColumnName("withholding_amount").HasPrecision(18, 2).HasDefaultValue(0m);
            entry.Property(row => row.NetAmount).HasColumnName("net_amount").HasPrecision(18, 2).HasDefaultValue(0m);
            entry.HasIndex(row => new { row.SupplierId, row.Status }).HasDatabaseName("billing_invoices_supplier_status_idx");
            entry.HasIndex(row => new { row.SupplierId, row.InvoiceNumber }).IsUnique()
                .HasFilter("[invoice_number] <> ''").HasDatabaseName("billing_invoices_supplier_number_idx");
            entry.HasOne<Supplier>().WithMany().HasForeignKey(row => row.SupplierId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_billing_invoices_suppliers_supplier_id");
        });

        model.Entity<BillingInvoiceLine>(entry =>
        {
            entry.ToTable("billing_invoice_lines", table => table.HasCheckConstraint(
                "billing_invoice_lines_amounts_ck", "[quantity] >= 0 AND [unit_price] >= 0 AND [amount] >= 0"));
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(row => row.InvoiceId).HasColumnName("invoice_id");
            entry.Property(row => row.Code).HasColumnName("code").HasMaxLength(40);
            entry.Property(row => row.Position).HasColumnName("position");
            entry.Property(row => row.Quantity).HasColumnName("quantity").HasPrecision(18, 2);
            entry.Property(row => row.UnitPrice).HasColumnName("unit_price").HasPrecision(18, 2);
            entry.Property(row => row.Amount).HasColumnName("amount").HasPrecision(18, 2);
            entry.Property(row => row.Description).HasColumnName("description").HasMaxLength(300).HasDefaultValue("");
            entry.Property(row => row.Detail).HasColumnName("detail").HasMaxLength(300).HasDefaultValue("");
            entry.HasIndex(row => new { row.InvoiceId, row.Code }).IsUnique().HasDatabaseName("billing_invoice_lines_invoice_code_idx");
            entry.HasOne<BillingInvoice>().WithMany().HasForeignKey(row => row.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_billing_invoice_lines_invoices_invoice_id");
        });

        model.Entity<BillingInvoiceJobLink>(entry =>
        {
            entry.ToTable("billing_invoice_job_links");
            entry.HasKey(row => new { row.InvoiceId, row.BillingCaseId });
            entry.Property(row => row.InvoiceId).HasColumnName("invoice_id");
            entry.Property(row => row.BillingCaseId).HasColumnName("billing_case_id");
            entry.Property(row => row.LinkedAt).HasColumnName("linked_at");
            entry.Property(row => row.LinkedBy).HasColumnName("linked_by").HasMaxLength(120);
            entry.HasIndex(row => row.BillingCaseId).IsUnique().HasDatabaseName("billing_invoice_job_links_case_idx");
            entry.HasOne(row => row.Invoice).WithMany().HasForeignKey(row => row.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_billing_invoice_job_links_invoices_invoice_id");
            entry.HasOne(row => row.BillingCase).WithMany().HasForeignKey(row => row.BillingCaseId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_billing_invoice_job_links_cases_billing_case_id");
        });
    }
}
