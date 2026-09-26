using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record BillingChargeView(long Id, string ChargeType, decimal RequestedAmount,
    decimal? ApprovedAmount, string Currency, string Reason, string Status);
public record BillingInvoiceView(long Id, string InvoiceNumber, string InvoiceDate,
    string Currency, decimal Subtotal, decimal TaxAmount, decimal TotalAmount,
    string Status, DateTimeOffset UpdatedAt, IReadOnlyList<BillingValidationView> ValidationResults,
    IReadOnlyList<BillingChargeView> AdditionalCharges, int ReviewCycle,
    DateTimeOffset? ReviewSubmittedAt, DateTimeOffset? ReviewDecidedAt, DateTimeOffset? OnlineApprovedAt,
    int? ReviewAgeMinutes, int? ReviewDecisionMinutes, IReadOnlyList<BillingReviewEventView> ReviewEvents,
    OriginalPackageView? OriginalPackage);

public record BillingCaseView(long Id, string JobKey, string JobCode, string Customer,
    string Category, int SupplierId, string Supplier, string Status,
    DateTimeOffset DeliveryCompletedAt, string SlaRuleCode, string SlaStartDay,
    int? SlaTargetWorkingDays, string SlaStartDate, string SlaDueDate,
    string SlaState, int? DaysRemaining, string SlaIssueCode, string SlaIssue,
    BillingInvoiceView? Invoice, IReadOnlyList<DocumentView> Documents);

public record BillingMutation(bool Ok, string Code, string Message,
    BillingCaseView? Case = null, BillingInvoiceView? Invoice = null, bool Replayed = false);

/// <summary>
/// Phase 4 application service. Both the carrier portal and internal Billing
/// Control read this service; supplier scope is decided here from identity,
/// never by a supplier id sent from the browser.
/// </summary>
public class CarrierBillingService(ScmosDbContext db, BusinessCalendarService calendar,
    CarrierTenantContext tenants, DocumentService documents, AuditService audit,
    BillingValidationService validation)
{
    public const string DefaultSlaRuleCode = "STANDARD";
    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    /// <summary>
    /// Stages one Billing Case when Delivery Complete is recorded. A missing
    /// SLA configuration is preserved on the case instead of guessing Day 0 or
    /// Day 1 or blocking the transport status change.
    /// </summary>
    public async Task<(BillingCase? Case, bool Created)> EnsureForDeliveryAsync(AppUser actor,
        Supplier supplier, OperationJob job, DateTimeOffset completedAt, CancellationToken token)
    {
        var existing = await db.BillingCases.FirstOrDefaultAsync(row => row.JobKey == job.Key, token);
        var deliveredOn = DateOnly.FromDateTime(completedAt.ToOffset(Thailand).DateTime);
        var resolved = await calendar.CalculateAsync(DefaultSlaRuleCode, deliveredOn, token);
        if (existing is not null)
        {
            // Configuration may have been added after an earlier Delivery
            // Complete. A replay repairs only the missing SLA snapshot.
            if (existing.SlaRuleId is null && resolved.Ok)
            {
                Snapshot(existing, resolved);
                existing.UpdatedBy = actor.Signature;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                audit.Stage(actor, AuditActions.Configure, "billing-case", existing.Id.ToString(),
                    job.JobCode, "sla", existing.SlaIssueCode, resolved.Rule!.Code,
                    "เติม SLA snapshot หลังตั้งค่ากฎ");
            }
            return (existing, false);
        }

        var assignment = await db.SupplierRequests.AsNoTracking()
            .Where(row => row.JobKey == job.Key && row.Outcome == CarrierAssignment.Confirmed
                && row.SupplierId == supplier.Id)
            .OrderByDescending(row => row.Id).FirstOrDefaultAsync(token);
        if (assignment is null) return (null, false);
        var now = DateTimeOffset.UtcNow;
        var record = new BillingCase
        {
            JobKey = job.Key,
            SupplierId = supplier.Id,
            AssignmentId = assignment.Id,
            Status = BillingCaseStatus.WaitingCarrierSubmission,
            DeliveryCompletedAt = completedAt,
            SlaRuleCode = DefaultSlaRuleCode,
            CreatedBy = actor.Signature,
            CreatedAt = now,
            UpdatedBy = actor.Signature,
            UpdatedAt = now,
        };
        if (resolved.Ok) Snapshot(record, resolved);
        else
        {
            record.SlaIssueCode = resolved.Code;
            record.SlaIssue = resolved.Message;
        }
        db.BillingCases.Add(record);
        audit.Stage(actor, AuditActions.Register, "billing-case", job.Key, job.JobCode,
            "status", "", record.Status, "Delivery Complete");
        return (record, true);
    }

    public async Task<(bool Ok, string Message, IReadOnlyList<BillingCaseView> Items)> ListAsync(
        AppUser user, CancellationToken token)
    {
        IQueryable<BillingCase> query = db.BillingCases.AsNoTracking();
        if (CarrierTenantContext.IsCarrier(user))
        {
            var tenant = await tenants.ResolveAsync(user, token);
            if (tenant is null) return (false, "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา", []);
            return await ListForCarrierAsync(tenant.SupplierId, token);
        }
        else if (!user.Can(Capability.ViewRates))
        {
            return (false, "บัญชีนี้ไม่มีสิทธิ์ดูข้อมูลการวางบิล", []);
        }

        var cases = await query.OrderByDescending(row => row.DeliveryCompletedAt).Take(1000).ToListAsync(token);
        return (true, "", await DescribeAsync(cases, token));
    }

    /// <summary>
    /// The carrier-scoped billing projection for a supplier identity already
    /// resolved by a trusted boundary (a staff membership or a Carrier API
    /// key). The supplier id is never accepted from the public request.
    /// </summary>
    public async Task<(bool Ok, string Message, IReadOnlyList<BillingCaseView> Items)> ListForCarrierAsync(
        int supplierId, CancellationToken token)
    {
        var cases = await db.BillingCases.AsNoTracking()
            .Where(row => row.SupplierId == supplierId)
            .OrderByDescending(row => row.DeliveryCompletedAt).Take(1000).ToListAsync(token);
        return (true, "", await DescribeAsync(cases, token));
    }

    public async Task<BillingMutation> CreateDraftAsync(AppUser user, long caseId, CancellationToken token)
    {
        var tenant = await CarrierAsync(user, token);
        if (tenant is null) return Denied();
        return await CreateDraftForAsync(user, tenant.SupplierId, caseId, token);
    }

    public async Task<BillingMutation> CreateDraftForAsync(AppUser user, int supplierId,
        long caseId, CancellationToken token)
    {
        var billingCase = await db.BillingCases.FirstOrDefaultAsync(row =>
            row.Id == caseId && row.SupplierId == supplierId, token);
        if (billingCase is null) return Missing();

        var linked = await db.BillingInvoiceJobLinks.AsNoTracking()
            .FirstOrDefaultAsync(row => row.BillingCaseId == caseId, token);
        if (linked is not null)
        {
            var existing = await db.BillingInvoices.AsNoTracking()
                .FirstAsync(row => row.Id == linked.InvoiceId, token);
            return new(true, "OK", "มีใบวางบิลฉบับร่างสำหรับงานนี้แล้ว",
                Invoice: Describe(existing), Replayed: true);
        }

        var now = DateTimeOffset.UtcNow;
        var invoice = new BillingInvoice
        {
            SupplierId = supplierId,
            Status = BillingInvoiceStatus.Draft,
            Currency = "THB",
            CreatedBy = user.Signature,
            CreatedAt = now,
            UpdatedBy = user.Signature,
            UpdatedAt = now,
        };
        db.BillingInvoices.Add(invoice);
        db.BillingInvoiceJobLinks.Add(new BillingInvoiceJobLink
        {
            Invoice = invoice,
            BillingCase = billingCase,
            BillingCaseId = billingCase.Id,
            LinkedAt = now,
            LinkedBy = user.Signature,
        });
        billingCase.Status = BillingCaseStatus.Draft;
        billingCase.UpdatedBy = user.Signature;
        billingCase.UpdatedAt = now;
        audit.Stage(user, AuditActions.Register, "billing-invoice", billingCase.JobKey,
            billingCase.JobKey, "status", "", BillingInvoiceStatus.Draft, ChannelOf(user), AuditSourceOf(user));
        await db.SaveChangesAsync(token);
        return new(true, "OK", "สร้างใบวางบิลฉบับร่างแล้ว", Invoice: Describe(invoice));
    }

    public async Task<BillingMutation> UpdateDraftAsync(AppUser user, long invoiceId,
        string invoiceNumber, string invoiceDate, string currency, decimal subtotal,
        decimal taxAmount, CancellationToken token)
    {
        var tenant = await CarrierAsync(user, token);
        if (tenant is null) return Denied();
        return await UpdateDraftForAsync(user, tenant.SupplierId, invoiceId, invoiceNumber,
            invoiceDate, currency, subtotal, taxAmount, token);
    }

    public async Task<BillingMutation> UpdateDraftForAsync(AppUser user, int supplierId, long invoiceId,
        string invoiceNumber, string invoiceDate, string currency, decimal subtotal,
        decimal taxAmount, CancellationToken token)
    {
        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(row =>
            row.Id == invoiceId && row.SupplierId == supplierId, token);
        if (invoice is null) return Missing();
        if (invoice.Status != BillingInvoiceStatus.Draft && invoice.Status != BillingInvoiceStatus.Blocked
            && !BillingReviewTransitions.CanResubmit(invoice.Status))
            return new(false, "NOT_DRAFT", "แก้ไขได้เฉพาะใบวางบิลฉบับร่าง");

        var number = (invoiceNumber ?? "").Trim();
        if (number.Length > 80) return Invalid("เลขที่ใบแจ้งหนี้ยาวเกิน 80 ตัวอักษร");
        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(invoiceDate))
        {
            if (!DateOnly.TryParseExact(invoiceDate.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return Invalid("วันที่ใบแจ้งหนี้ต้องเป็น yyyy-MM-dd");
            date = parsed;
        }
        var money = (currency ?? "").Trim().ToUpperInvariant();
        if (money.Length != 3 || money.Any(ch => ch is < 'A' or > 'Z'))
            return Invalid("สกุลเงินต้องเป็นรหัส 3 ตัว เช่น THB");
        if (subtotal < 0 || taxAmount < 0)
            return Invalid("ยอดเงินต้องไม่ติดลบ");
        if (number.Length > 0 && await db.BillingInvoices.AsNoTracking().AnyAsync(row =>
                row.SupplierId == supplierId && row.InvoiceNumber == number && row.Id != invoice.Id, token))
            return new(false, "DUPLICATE_INVOICE_NUMBER", "เลขที่ใบแจ้งหนี้นี้มีอยู่แล้ว");

        var before = $"{invoice.InvoiceNumber}|{invoice.InvoiceDate}|{invoice.Currency}|{invoice.TotalAmount}";
        invoice.InvoiceNumber = number;
        invoice.InvoiceDate = date;
        invoice.Currency = money;
        invoice.Subtotal = decimal.Round(subtotal, 2, MidpointRounding.AwayFromZero);
        invoice.TaxAmount = decimal.Round(taxAmount, 2, MidpointRounding.AwayFromZero);
        invoice.TotalAmount = invoice.Subtotal + invoice.TaxAmount;
        invoice.UpdatedBy = user.Signature;
        invoice.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Stage(user, AuditActions.Update, "billing-invoice", invoice.Id.ToString(),
            invoice.InvoiceNumber, "draft", before,
            $"{invoice.InvoiceNumber}|{invoice.InvoiceDate}|{invoice.Currency}|{invoice.TotalAmount}",
            ChannelOf(user), AuditSourceOf(user));
        await db.SaveChangesAsync(token);
        return new(true, "OK", "บันทึกใบวางบิลฉบับร่างแล้ว", Invoice: Describe(invoice));
    }

    public async Task<BillingMutation> AddDocumentAsync(AppUser user, long invoiceId,
        string kind, string note, IFormFile file, CancellationToken token)
    {
        var tenant = await CarrierAsync(user, token);
        if (tenant is null) return Denied();
        return await AddDocumentForAsync(user, tenant.SupplierId, invoiceId, kind, note, file, token);
    }

    public async Task<BillingMutation> AddDocumentForAsync(AppUser user, int supplierId, long invoiceId,
        string kind, string note, IFormFile file, CancellationToken token)
    {
        var invoice = await db.BillingInvoices.AsNoTracking().FirstOrDefaultAsync(row =>
            row.Id == invoiceId && row.SupplierId == supplierId, token);
        if (invoice is null) return Missing();
        if (invoice.Status != BillingInvoiceStatus.Draft && invoice.Status != BillingInvoiceStatus.Blocked
            && !BillingReviewTransitions.CanResubmit(invoice.Status))
            return new(false, "NOT_DRAFT", "เพิ่มเอกสารได้เฉพาะรายการที่ยังไม่ผ่าน Validation");
        var link = await db.BillingInvoiceJobLinks.AsNoTracking()
            .FirstOrDefaultAsync(row => row.InvoiceId == invoiceId, token);
        if (link is null) return Missing();
        var billingCase = await db.BillingCases.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == link.BillingCaseId && row.SupplierId == supplierId, token);
        if (billingCase is null) return Missing();
        var result = await documents.AddToBillingAsync(billingCase, invoice, kind, note, file, user, token);
        if (!result.Ok) return new(false, "DOCUMENT_ERROR", result.Message);
        await audit.RecordAsync(user, AuditActions.Upload, "billing-invoice", invoice.Id.ToString(),
            invoice.InvoiceNumber, "document", "", result.Document!.ObjectKey, note, token, AuditSourceOf(user));
        return new(true, "OK", result.Message, Invoice: Describe(invoice));
    }

    public async Task<BillingSubmitResult> SubmitAsync(AppUser user, long invoiceId, CancellationToken token)
    {
        var tenant = await CarrierAsync(user, token);
        return tenant is null
            ? new(false, "NO_CARRIER", "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา", "", [])
            : await SubmitForAsync(user, tenant.SupplierId, invoiceId, token);
    }

    public Task<BillingSubmitResult> SubmitForAsync(AppUser user, int supplierId, long invoiceId,
        CancellationToken token) => validation.SubmitAsync(user, supplierId, invoiceId, token);

    public async Task<BillingCaseView?> FindInvoiceForAsync(int supplierId, long invoiceId,
        CancellationToken token)
    {
        var caseId = await db.BillingInvoiceJobLinks.AsNoTracking()
            .Where(row => row.InvoiceId == invoiceId)
            .Select(row => (long?)row.BillingCaseId).FirstOrDefaultAsync(token);
        if (caseId is null) return null;
        var billingCase = await db.BillingCases.AsNoTracking().FirstOrDefaultAsync(row =>
            row.Id == caseId.Value && row.SupplierId == supplierId, token);
        if (billingCase is null) return null;
        return (await DescribeAsync([billingCase], token)).SingleOrDefault();
    }

    private async Task<IReadOnlyList<BillingCaseView>> DescribeAsync(
        IReadOnlyList<BillingCase> cases, CancellationToken token)
    {
        if (cases.Count == 0) return [];
        var ids = cases.Select(row => row.Id).ToList();
        var jobKeys = cases.Select(row => row.JobKey).ToList();
        var supplierIds = cases.Select(row => row.SupplierId).Distinct().ToList();
        var jobs = await db.OperationJobs.AsNoTracking().Where(row => jobKeys.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, token);
        var suppliers = await db.Suppliers.AsNoTracking().Where(row => supplierIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Name, token);
        var links = await db.BillingInvoiceJobLinks.AsNoTracking()
            .Where(row => ids.Contains(row.BillingCaseId)).ToListAsync(token);
        var invoiceIds = links.Select(row => row.InvoiceId).Distinct().ToList();
        var invoices = await db.BillingInvoices.AsNoTracking().Where(row => invoiceIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, token);
        var runs = await db.BillingValidationRuns.AsNoTracking().Where(row => invoiceIds.Contains(row.InvoiceId))
            .OrderByDescending(row => row.Sequence).ToListAsync(token);
        var latestRunIds = runs.GroupBy(row => row.InvoiceId).Select(group => group.First().Id).ToList();
        var validationRows = await db.BillingValidationResults.AsNoTracking().Where(row => latestRunIds.Contains(row.RunId))
            .OrderBy(row => row.Sequence).ToListAsync(token);
        var validations = validationRows.GroupBy(row => row.InvoiceId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<BillingValidationView>)group.Select(row => new BillingValidationView(row.Sequence,
                row.Step, row.Code, row.Category, row.Blocking, row.Message, row.ExpectedAmount,
                row.ActualAmount, row.Currency, row.EvidenceType, row.EvidenceId, row.EvidenceVersion,
                row.RuleSource, row.EffectiveDate?.ToString("yyyy-MM-dd") ?? "")).ToList());
        var chargeRows = await db.BillingAdditionalCharges.AsNoTracking().Where(row => invoiceIds.Contains(row.InvoiceId))
            .OrderBy(row => row.RequestedAt).ToListAsync(token);
        var charges = chargeRows.GroupBy(row => row.InvoiceId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<BillingChargeView>)group.Select(row => new BillingChargeView(row.Id,
                row.ChargeType, row.RequestedAmount, row.ApprovedAmount, row.Currency, row.Reason, row.Status)).ToList());
        var reviewRows = await db.BillingReviewEvents.AsNoTracking().Where(row => invoiceIds.Contains(row.InvoiceId))
            .OrderBy(row => row.Id).ToListAsync(token);
        var reviews = reviewRows.GroupBy(row => row.InvoiceId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<BillingReviewEventView>)group.Select(row => new BillingReviewEventView(row.Id,
                row.Cycle, row.Action, row.FromStatus, row.ToStatus, row.ReasonCode, row.Remark,
                row.ActorName, row.At)).ToList());
        var originalPackages = await db.OriginalDocumentPackages.AsNoTracking()
            .Where(row => invoiceIds.Contains(row.InvoiceId)).ToDictionaryAsync(row => row.InvoiceId, token);
        var held = await db.Documents.AsNoTracking().Where(row => row.BillingCaseId != null
                && ids.Contains(row.BillingCaseId.Value)).OrderBy(row => row.UploadedAt).ToListAsync(token);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Thailand).DateTime);

        return cases.Select(row =>
        {
            jobs.TryGetValue(row.JobKey, out var job);
            var link = links.FirstOrDefault(one => one.BillingCaseId == row.Id);
            var invoice = link is not null && invoices.TryGetValue(link.InvoiceId, out var found)
                ? Describe(found, validations.GetValueOrDefault(found.Id, []), charges.GetValueOrDefault(found.Id, []),
                    reviews.GetValueOrDefault(found.Id, []), originalPackages.GetValueOrDefault(found.Id)) : null;
            var state = BillingSlaState.Of(today, row.SlaDueDate);
            return new BillingCaseView(row.Id, row.JobKey, job?.JobCode ?? row.JobKey,
                job?.Customer ?? "", job?.Cat ?? "", row.SupplierId,
                suppliers.GetValueOrDefault(row.SupplierId, ""), row.Status,
                row.DeliveryCompletedAt, row.SlaRuleCode, row.SlaStartDay,
                row.SlaTargetWorkingDays, row.SlaStartDate?.ToString("yyyy-MM-dd") ?? "",
                row.SlaDueDate?.ToString("yyyy-MM-dd") ?? "", state,
                row.SlaDueDate is null ? null : row.SlaDueDate.Value.DayNumber - today.DayNumber,
                row.SlaIssueCode, row.SlaIssue, invoice,
                held.Where(document => document.BillingCaseId == row.Id)
                    .Select(DocumentService.Describe).ToList());
        }).ToList();
    }

    private async Task<CarrierTenant?> CarrierAsync(AppUser user, CancellationToken token) =>
        CarrierTenantContext.IsCarrier(user) ? await tenants.ResolveAsync(user, token) : null;

    private static string ChannelOf(AppUser user) => user.Source == "carrier-api"
        ? "Carrier API" : "Carrier Portal";

    private static string AuditSourceOf(AppUser user) => user.Source == "carrier-api"
        ? EventSource.CarrierApi : "web";

    private static void Snapshot(BillingCase record, BillingSlaResolution resolved)
    {
        record.SlaRuleCode = resolved.Rule!.Code;
        record.SlaRuleId = resolved.Rule.Id;
        record.SlaStartDay = resolved.Rule.StartDay;
        record.SlaTargetWorkingDays = resolved.Rule.TargetWorkingDays;
        record.SlaStartDate = resolved.Dates!.StartDate;
        record.SlaDueDate = resolved.Dates.DueDate;
        record.SlaIssueCode = "";
        record.SlaIssue = "";
    }

    private static BillingInvoiceView Describe(BillingInvoice row, IReadOnlyList<BillingValidationView>? results = null,
        IReadOnlyList<BillingChargeView>? charges = null, IReadOnlyList<BillingReviewEventView>? reviews = null,
        OriginalDocumentPackage? originalPackage = null) => new(row.Id,
        row.InvoiceNumber, row.InvoiceDate?.ToString("yyyy-MM-dd") ?? "", row.Currency,
        row.Subtotal, row.TaxAmount, row.TotalAmount, row.Status, row.UpdatedAt, results ?? [], charges ?? [],
        row.ReviewCycle, row.ReviewSubmittedAt, row.ReviewDecidedAt, row.OnlineApprovedAt,
        row.ReviewSubmittedAt is null || row.ReviewDecidedAt is not null ? null
            : (int)Math.Max(0, (DateTimeOffset.UtcNow - row.ReviewSubmittedAt.Value).TotalMinutes),
        row.ReviewSubmittedAt is null || row.ReviewDecidedAt is null ? null
            : (int)Math.Max(0, (row.ReviewDecidedAt.Value - row.ReviewSubmittedAt.Value).TotalMinutes), reviews ?? [],
        originalPackage is null ? null : OriginalDocumentService.View(originalPackage));

    private static BillingMutation Denied() =>
        new(false, "NO_CARRIER", "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา");
    private static BillingMutation Missing() =>
        new(false, "NOT_FOUND", "ไม่พบรายการวางบิลนี้");
    private static BillingMutation Invalid(string message) => new(false, "INVALID", message);
}
