using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public sealed class FinanceIntegrationOptions
{
    public const string Section = "CarrierBilling:FinanceIntegration";
    public bool Enabled { get; set; }
    public string Adapter { get; set; } = "None";
    public string[] ReleaseRoles { get; set; } = [];
    public string ReleaseRoleList { get; set; } = "";
    public int PollSeconds { get; set; } = 20;
    public int BatchSize { get; set; } = 20;

    public IReadOnlyList<string> Roles() => ReleaseRoles
        .Concat(ReleaseRoleList.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        .Select(value => value.Trim()).Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public bool Valid => PollSeconds is >= 5 and <= 300 && BatchSize is >= 1 and <= 100;
}

public sealed class FinanceReleasePolicy(IOptions<FinanceIntegrationOptions> options)
{
    private FinanceIntegrationOptions Settings => options.Value;
    public bool IsConfigured => Settings.Roles().Count > 0;
    public bool CanRelease(AppUser user) => !CarrierTenantContext.IsCarrier(user)
        && user.Can(Capability.ReviewBilling)
        && Settings.Roles().Contains(user.Role, StringComparer.OrdinalIgnoreCase);
}

public record CanonicalFinanceJob(long BillingCaseId, string JobKey, string JobCode,
    string Customer, string CostAllocationCode);
public record CanonicalFinanceCharge(long Id, string Type, decimal Amount, string Currency,
    string ApprovalReference);
public record CanonicalFinanceInvoice(string SchemaVersion, long InvoiceId, int SupplierId,
    string CarrierCode, string CarrierName, string InvoiceNumber, string InvoiceDate,
    string Currency, IReadOnlyList<CanonicalFinanceJob> Jobs, decimal BaseCost,
    IReadOnlyList<CanonicalFinanceCharge> ApprovedAdditionalCharges, decimal Tax,
    decimal GrandTotal, DateTimeOffset OnlineApprovedAt, DateTimeOffset OriginalReceivedAt,
    string ApprovalReference);

public record FinanceAdapterResult(bool Ok, string Status, string ExternalReference,
    string Code, string Message, bool Retryable);

/// <summary>
/// Boundary to the still-unknown Finance/ERP platform. Vendor-specific DTOs,
/// credentials and endpoints belong behind this interface when supplied.
/// </summary>
public interface IFinanceAdapter
{
    string Name { get; }
    bool Configured { get; }
    Task<FinanceAdapterResult> SubmitAsync(CanonicalFinanceInvoice invoice,
        string idempotencyKey, CancellationToken token);
}

public sealed class UnconfiguredFinanceAdapter : IFinanceAdapter
{
    public string Name => "UNCONFIGURED";
    public bool Configured => false;
    public Task<FinanceAdapterResult> SubmitAsync(CanonicalFinanceInvoice invoice,
        string idempotencyKey, CancellationToken token) => Task.FromResult(
            new FinanceAdapterResult(false, FinanceStatus.Failed, "", "ADAPTER_NOT_CONFIGURED",
                "ยังไม่ได้กำหนด Finance/ERP adapter", false));
}

/// <summary>Offline-only adapter for local development and deterministic tests.</summary>
public sealed class MockFinanceAdapter : IFinanceAdapter
{
    public string Name => "MOCK";
    public bool Configured => true;
    public Task<FinanceAdapterResult> SubmitAsync(CanonicalFinanceInvoice invoice,
        string idempotencyKey, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reference = $"MOCK-FIN-{invoice.InvoiceId}";
        return Task.FromResult(new FinanceAdapterResult(true, FinanceStatus.Accepted,
            reference, "MOCK_ACCEPTED", "Mock Finance accepted the canonical invoice", false));
    }
}

/// <summary>
/// Production-safe manual handoff. The canonical snapshot is already durable
/// in SCMOS; this adapter marks it submitted to the internal Finance queue and
/// performs no outbound network call. Finance staff reconcile the response in
/// Billing Control until an approved ERP contract replaces this adapter.
/// </summary>
public sealed class InternalFinanceQueueAdapter : IFinanceAdapter
{
    public string Name => "SCMOS_INTERNAL";
    public bool Configured => true;
    public Task<FinanceAdapterResult> SubmitAsync(CanonicalFinanceInvoice invoice,
        string idempotencyKey, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new FinanceAdapterResult(true, FinanceStatus.Submitted,
            $"SCMOS-FIN-{invoice.InvoiceId}", "INTERNAL_QUEUE",
            "Submitted to the SCMOS internal Finance queue", false));
    }
}

/// <summary>
/// Mock can never be selected in Production. The real adapter remains an
/// explicit extension point rather than a guessed ERP integration.
/// </summary>
public sealed class FinanceAdapterResolver(IOptions<FinanceIntegrationOptions> options,
    IWebHostEnvironment environment, InternalFinanceQueueAdapter internalQueue,
    MockFinanceAdapter mock, UnconfiguredFinanceAdapter none)
{
    public IFinanceAdapter Current => options.Value.Adapter.Trim().ToUpperInvariant() switch
    {
        "SCMOS_INTERNAL" => internalQueue,
        "MOCK" when environment.IsDevelopment() || environment.IsEnvironment("Test") => mock,
        _ => none,
    };
}

public record FinanceRecordView(long Id, long InvoiceId, string Status, string Adapter,
    string IdempotencyKey, int Attempts, DateTimeOffset? LastAttemptAt, DateTimeOffset? NextAttemptAt,
    string ExternalReference, string ResponseCode, string ResponseMessage, string PaymentReference,
    DateTimeOffset? PaidAt, DateTimeOffset? ReconciledAt, string ReconciledBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record FinanceAvailability(bool Enabled, bool AdapterConfigured, string Adapter,
    bool ReleaseConfigured, bool CanRelease);
public record FinanceMutation(bool Ok, string Code, string Message, bool Replayed,
    FinanceRecordView? Record);
public record FinanceOutboxEnvelope(long FinanceRecordId);

public class FinanceIntegrationService(ScmosDbContext db, FinanceReleasePolicy policy,
    FinanceAdapterResolver adapters, IOptions<FinanceIntegrationOptions> options,
    AuditService audit, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private FinanceIntegrationOptions Settings => options.Value;

    public FinanceAvailability Availability(AppUser user) => CarrierTenantContext.IsCarrier(user)
        ? new(false, false, "", false, false)
        : new(Settings.Enabled, adapters.Current.Configured, adapters.Current.Name,
            policy.IsConfigured, policy.CanRelease(user));

    public async Task<IReadOnlyList<FinanceRecordView>?> ListInternalAsync(AppUser actor, CancellationToken token)
    {
        if (CarrierTenantContext.IsCarrier(actor) || !actor.Can(Capability.ViewRates)) return null;
        var rows = await db.BillingFinanceRecords.AsNoTracking().OrderByDescending(row => row.UpdatedAt)
            .Take(1000).ToListAsync(token);
        return rows.Select(View).ToList();
    }

    public async Task<FinanceMutation> QueueAsync(AppUser actor, long invoiceId,
        string requestKey, string correlationId, CancellationToken token)
    {
        if (!policy.CanRelease(actor)) return Fail("FORBIDDEN", policy.IsConfigured
            ? "บัญชีนี้ไม่มีสิทธิ์ส่งรายการเข้า Finance" : "ยังไม่ได้กำหนดบทบาทผู้ส่ง Finance ในระบบ");
        if (!Settings.Enabled) return Fail("DISABLED", "Finance Integration ยังไม่เปิดใช้งาน");
        if (!Settings.Valid) return Fail("INVALID_CONFIGURATION", "การตั้งค่า Finance Integration ไม่ถูกต้อง");
        var adapter = adapters.Current;
        if (!adapter.Configured) return Fail("ADAPTER_NOT_CONFIGURED", "ยังไม่ได้กำหนด Finance/ERP adapter");

        var cleanRequest = (requestKey ?? "").Trim();
        if (cleanRequest.Length is < 8 or > 80) return Fail("INVALID_IDEMPOTENCY_KEY", "Idempotency key ต้องยาว 8–80 ตัวอักษร");
        if (cleanRequest.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not ':'))
            return Fail("INVALID_IDEMPOTENCY_KEY", "Idempotency key มีอักขระที่ไม่รองรับ");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var existingByKey = await db.BillingFinanceRecords.FirstOrDefaultAsync(row => row.IdempotencyKey == cleanRequest, token);
            if (existingByKey is not null)
            {
                await transaction.CommitAsync(token);
                return existingByKey.InvoiceId == invoiceId
                    ? Success("คำขอส่ง Finance นี้ถูกบันทึกไว้แล้ว", true, existingByKey)
                    : Fail("IDEMPOTENCY_CONFLICT", "Idempotency key นี้ถูกใช้กับใบวางบิลอื่นแล้ว");
            }
            var existing = await db.BillingFinanceRecords.FirstOrDefaultAsync(row => row.InvoiceId == invoiceId, token);
            if (existing is not null)
            {
                await transaction.CommitAsync(token);
                return Success("ใบวางบิลนี้มี Finance record แล้ว", true, existing);
            }

            var built = await BuildCanonicalAsync(invoiceId, token);
            if (!built.Ok || built.Invoice is null || built.BillingCase is null)
                return Fail(built.Code, built.Message);

            var now = clock.GetUtcNow();
            var payload = JsonSerializer.Serialize(built.Invoice, Json);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            var row = new BillingFinanceRecord
            {
                InvoiceId = invoiceId, Status = FinanceStatus.Queued, Adapter = adapter.Name,
                IdempotencyKey = cleanRequest, PayloadJson = payload, PayloadHash = hash,
                NextAttemptAt = now, CreatedBy = actor.Signature, CreatedAt = now, UpdatedAt = now,
            };
            db.BillingFinanceRecords.Add(row);
            await db.SaveChangesAsync(token);

            db.IntegrationOutbox.Add(new IntegrationOutboxEvent
            {
                EventType = FinanceEvents.SubmissionRequested, AggregateType = "billing-finance-record",
                AggregateId = row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                IdempotencyKey = cleanRequest, PayloadJson = JsonSerializer.Serialize(new FinanceOutboxEnvelope(row.Id), Json),
                Status = OutboxStatus.Pending, NextAttemptAt = now,
                CorrelationId = Trim(correlationId, 64), CreatedAt = now, UpdatedAt = now,
            });
            var from = built.SourceInvoice.Status;
            built.SourceInvoice.Status = BillingInvoiceStatus.FinanceProcessing;
            built.SourceInvoice.UpdatedBy = actor.Signature; built.SourceInvoice.UpdatedAt = now;
            built.BillingCase.Status = BillingCaseStatus.FinanceProcessing;
            built.BillingCase.UpdatedBy = actor.Signature; built.BillingCase.UpdatedAt = now;
            audit.Stage(actor, AuditActions.Update, "billing-finance-record", row.Id.ToString(),
                built.SourceInvoice.InvoiceNumber, "status", from, BillingInvoiceStatus.FinanceProcessing,
                $"Finance release queued · adapter {adapter.Name} · correlation {Trim(correlationId, 64)}");
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return Success("จัดคิวส่ง Finance แล้ว", false, row);
        });
    }

    public async Task<FinanceMutation> RetryAsync(AppUser actor, long recordId,
        string correlationId, CancellationToken token)
    {
        if (!policy.CanRelease(actor)) return Fail("FORBIDDEN", "บัญชีนี้ไม่มีสิทธิ์ Retry Finance");
        if (!Settings.Enabled || !adapters.Current.Configured) return Fail("DISABLED", "Finance Integration ยังไม่พร้อมใช้งาน");
        var row = await db.BillingFinanceRecords.FirstOrDefaultAsync(value => value.Id == recordId, token);
        if (row is null) return Fail("NOT_FOUND", "ไม่พบ Finance record นี้");
        if (row.Status != FinanceStatus.Failed) return Fail("INVALID_STATUS", "Retry ได้เฉพาะรายการที่ส่งไม่สำเร็จครบจำนวนครั้งแล้ว");
        var outbox = await db.IntegrationOutbox.FirstOrDefaultAsync(value => value.IdempotencyKey == row.IdempotencyKey, token);
        if (outbox is null) return Fail("OUTBOX_NOT_FOUND", "ไม่พบ Outbox event ของรายการนี้");
        var now = clock.GetUtcNow();
        row.Status = FinanceStatus.Queued; row.NextAttemptAt = now; row.ResponseCode = "";
        row.ResponseMessage = ""; row.UpdatedAt = now;
        outbox.Status = OutboxStatus.Pending; outbox.Attempts = 0; outbox.NextAttemptAt = now;
        outbox.LockedUntil = null; outbox.LastError = ""; outbox.ProcessedAt = null;
        outbox.CorrelationId = Trim(correlationId, 64); outbox.UpdatedAt = now;
        audit.Stage(actor, AuditActions.Update, "billing-finance-record", row.Id.ToString(),
            row.InvoiceId.ToString(), "status", FinanceStatus.Failed, FinanceStatus.Queued, "Manual retry requested");
        await db.SaveChangesAsync(token);
        return Success("จัดคิว Retry Finance แล้ว", false, row);
    }

    public async Task<FinanceMutation> ReconcileAsync(AppUser actor, long recordId, string requestedStatus,
        string externalReference, string paymentReference, DateTimeOffset? paidAt, string reason,
        CancellationToken token)
    {
        if (!policy.CanRelease(actor)) return Fail("FORBIDDEN", "บัญชีนี้ไม่มีสิทธิ์บันทึกผล Finance");
        if (!Settings.Enabled) return Fail("DISABLED", "Finance Integration ยังไม่เปิดใช้งาน");
        var next = (requestedStatus ?? "").Trim().ToUpperInvariant();
        if (!FinanceStatus.ReconciliationStates.Contains(next, StringComparer.Ordinal))
            return Fail("INVALID_STATUS", "Finance status ไม่ถูกต้อง");
        var row = await db.BillingFinanceRecords.FirstOrDefaultAsync(value => value.Id == recordId, token);
        if (row is null) return Fail("NOT_FOUND", "ไม่พบ Finance record นี้");
        if (row.Status == next && row.ExternalReference == Trim(externalReference, 160)
            && row.PaymentReference == Trim(paymentReference, 160))
            return Success("ผล Finance นี้ถูกบันทึกไว้แล้ว", true, row);
        if (!FinanceReconciliation.CanApply(row.Status, next))
            return Fail("INVALID_TRANSITION", $"เปลี่ยน Finance status จาก {row.Status} เป็น {next} ไม่ได้");
        if (next is FinanceStatus.Accepted or FinanceStatus.Paid or FinanceStatus.Closed
            && string.IsNullOrWhiteSpace(externalReference) && string.IsNullOrWhiteSpace(row.ExternalReference))
            return Fail("REFERENCE_REQUIRED", "ต้องระบุ Finance reference");
        if (next == FinanceStatus.Paid && paidAt is null)
            return Fail("PAID_AT_REQUIRED", "ต้องระบุวันเวลาที่ชำระเงิน");

        var invoice = await db.BillingInvoices.FirstAsync(value => value.Id == row.InvoiceId, token);
        var link = await db.BillingInvoiceJobLinks.AsNoTracking().FirstAsync(value => value.InvoiceId == row.InvoiceId, token);
        var billingCase = await db.BillingCases.FirstAsync(value => value.Id == link.BillingCaseId, token);
        var now = clock.GetUtcNow(); var before = row.Status;
        row.Status = next; row.ExternalReference = First(externalReference, row.ExternalReference, 160);
        row.PaymentReference = First(paymentReference, row.PaymentReference, 160);
        row.PaidAt = next == FinanceStatus.Paid ? paidAt : row.PaidAt;
        row.ReconciledAt = now; row.ReconciledBy = actor.Signature; row.ResponseCode = next;
        row.ResponseMessage = Trim(reason, 800); row.UpdatedAt = now;
        var invoiceStatus = FinanceReconciliation.InvoiceStatus(next);
        invoice.Status = invoiceStatus; invoice.UpdatedBy = actor.Signature; invoice.UpdatedAt = now;
        billingCase.Status = invoiceStatus; billingCase.UpdatedBy = actor.Signature; billingCase.UpdatedAt = now;
        audit.Stage(actor, AuditActions.Update, "billing-finance-record", row.Id.ToString(),
            invoice.InvoiceNumber, "status", before, next, Trim(reason, 400));
        await db.SaveChangesAsync(token);
        return Success("บันทึกผล Finance reconciliation แล้ว", false, row);
    }

    public async Task<FinanceRecordView?> FindAsync(long invoiceId, CancellationToken token)
    {
        var row = await db.BillingFinanceRecords.AsNoTracking()
            .FirstOrDefaultAsync(value => value.InvoiceId == invoiceId, token);
        return row is null ? null : View(row);
    }

    private async Task<CanonicalBuild> BuildCanonicalAsync(long invoiceId, CancellationToken token)
    {
        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(row => row.Id == invoiceId, token);
        if (invoice is null) return CanonicalBuild.Fail("NOT_FOUND", "ไม่พบใบวางบิลนี้");
        if (invoice.Status != BillingInvoiceStatus.ReadyForFinance)
            return CanonicalBuild.Fail("NOT_READY", "ใบวางบิลยังไม่อยู่ในสถานะ READY_FOR_FINANCE");
        var package = await db.OriginalDocumentPackages.AsNoTracking().FirstOrDefaultAsync(row => row.InvoiceId == invoiceId, token);
        var latestRun = await db.BillingValidationRuns.AsNoTracking().Where(row => row.InvoiceId == invoiceId)
            .OrderByDescending(row => row.Sequence).FirstOrDefaultAsync(token);
        var blocking = latestRun is null || await db.BillingValidationResults.AsNoTracking()
            .AnyAsync(row => row.RunId == latestRun.Id && row.Blocking, token);
        var readiness = BillingFinanceReadiness.Evaluate(invoice.OnlineApprovedAt is not null,
            package?.ReceivedAt is not null, blocking);
        if (!readiness.Ready) return CanonicalBuild.Fail(readiness.Code, "Finance readiness ไม่ผ่าน");

        var links = await db.BillingInvoiceJobLinks.AsNoTracking().Where(row => row.InvoiceId == invoiceId)
            .OrderBy(row => row.BillingCaseId).ToListAsync(token);
        if (links.Count == 0) return CanonicalBuild.Fail("JOB_NOT_LINKED", "ใบวางบิลไม่มี Billing Case");
        var caseIds = links.Select(row => row.BillingCaseId).ToList();
        var cases = await db.BillingCases.Where(row => caseIds.Contains(row.Id)).OrderBy(row => row.Id).ToListAsync(token);
        if (cases.Count != links.Count) return CanonicalBuild.Fail("BILLING_CASE_NOT_FOUND", "พบ Billing Case ไม่ครบ");
        var jobKeys = cases.Select(row => row.JobKey).ToList();
        var jobs = await db.OperationJobs.AsNoTracking().Where(row => jobKeys.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, token);
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(row => row.Id == invoice.SupplierId, token);
        var charges = await db.BillingAdditionalCharges.AsNoTracking().Where(row => row.InvoiceId == invoiceId
                && (row.Status == "APPROVED" || row.Status == "PARTIALLY_APPROVED") && row.ApprovedAmount != null)
            .OrderBy(row => row.Id).ToListAsync(token);
        var review = await db.BillingReviewEvents.AsNoTracking().Where(row => row.InvoiceId == invoiceId
                && row.Action == BillingReviewAction.ApproveOnline)
            .OrderByDescending(row => row.Id).FirstOrDefaultAsync(token);
        if (review is null) return CanonicalBuild.Fail("APPROVAL_NOT_FOUND", "ไม่พบ Online approval reference");
        var approvedCharges = charges.Sum(row => row.ApprovedAmount ?? 0m);
        var canonicalJobs = cases.Select(row =>
        {
            jobs.TryGetValue(row.JobKey, out var job);
            return new CanonicalFinanceJob(row.Id, row.JobKey, job?.JobCode ?? row.JobKey,
                job?.Customer ?? "", "");
        }).ToList();
        var canonicalCharges = charges.Select(row => new CanonicalFinanceCharge(row.Id, row.ChargeType,
            row.ApprovedAmount!.Value, row.Currency, $"additional-charge:{row.Id}:{row.Status}")).ToList();
        var canonical = new CanonicalFinanceInvoice("scmos.finance.invoice.v1", invoice.Id, supplier.Id,
            supplier.Code, supplier.Name, invoice.InvoiceNumber, invoice.InvoiceDate?.ToString("yyyy-MM-dd") ?? "",
            invoice.Currency, canonicalJobs, invoice.Subtotal - approvedCharges, canonicalCharges,
            invoice.TaxAmount, invoice.TotalAmount, invoice.OnlineApprovedAt!.Value, package!.ReceivedAt!.Value,
            $"billing-review:{review.Id}:cycle:{review.Cycle}");
        return new(true, "OK", "", canonical, invoice, cases[0]);
    }

    public static FinanceRecordView View(BillingFinanceRecord row) => new(row.Id, row.InvoiceId,
        row.Status, row.Adapter, row.IdempotencyKey, row.Attempts, row.LastAttemptAt,
        row.NextAttemptAt, row.ExternalReference, row.ResponseCode, row.ResponseMessage,
        row.PaymentReference, row.PaidAt, row.ReconciledAt, row.ReconciledBy, row.CreatedAt, row.UpdatedAt);
    private static FinanceMutation Success(string message, bool replayed, BillingFinanceRecord row) =>
        new(true, "OK", message, replayed, View(row));
    private static FinanceMutation Fail(string code, string message) => new(false, code, message, false, null);
    private static string Trim(string? value, int max) { var text = (value ?? "").Trim(); return text.Length <= max ? text : text[..max]; }
    private static string First(string? preferred, string fallback, int max) => Trim(string.IsNullOrWhiteSpace(preferred) ? fallback : preferred, max);
    private record CanonicalBuild(bool Ok, string Code, string Message, CanonicalFinanceInvoice? Invoice,
        BillingInvoice SourceInvoice, BillingCase? BillingCase)
    {
        public static CanonicalBuild Fail(string code, string message) => new(false, code, message, null, null!, null);
    }
}
