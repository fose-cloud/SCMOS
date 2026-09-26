using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record OriginalPackageView(long Id, string Status, string SentDate, string Courier,
    string TrackingNumber, string CarrierPackageReference, string CarrierRemark, string SentBy,
    DateTimeOffset? SentAt, DateTimeOffset? ReceivedAt, string ReceivedByName, int? DocumentCount,
    string ReceiptPackageReference, string ReceiptRemark);

public record OriginalDocumentMutation(bool Ok, string Code, string Message, string InvoiceStatus,
    bool FinanceReady, bool Replayed, OriginalPackageView? Package);

/// <summary>
/// The business permission that was left TBD. It is deliberately deployment
/// configuration, with no guessed role and a fail-closed empty default.
/// </summary>
public class OriginalReceiptPolicy(IConfiguration configuration)
{
    public const string RolesKey = "CarrierBilling:OriginalReceiptRoles";
    public const string RoleListKey = "CarrierBilling:OriginalReceiptRoleList";

    public IReadOnlyList<string> Roles()
    {
        var configured = configuration.GetSection(RolesKey).Get<string[]>() ?? [];
        var hostList = (configuration[RoleListKey] ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);
        return configured.Concat(hostList).Select(role => role.Trim()).Where(role => role.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool IsConfigured => Roles().Count > 0;
    public bool CanReceive(AppUser user) => !CarrierTenantContext.IsCarrier(user)
        && Roles().Contains(user.Role, StringComparer.OrdinalIgnoreCase);
}

public class OriginalDocumentService(ScmosDbContext db, CarrierTenantContext tenants,
    OriginalReceiptPolicy receiptPolicy, AuditService audit)
{
    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    public async Task<OriginalDocumentMutation> SaveCarrierPackageAsync(AppUser actor, long invoiceId,
        string sentDate, string courier, string trackingNumber, string packageReference,
        string remark, CancellationToken token)
    {
        if (!CarrierTenantContext.IsCarrier(actor)) return Fail("FORBIDDEN", "เฉพาะบัญชีผู้ขนส่งเท่านั้นที่แจ้งการส่งต้นฉบับได้");
        var tenant = await tenants.ResolveAsync(actor, token);
        if (tenant is null) return Fail("FORBIDDEN", "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา");
        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(row => row.Id == invoiceId
            && row.SupplierId == tenant.SupplierId, token);
        if (invoice is null) return Fail("NOT_FOUND", "ไม่พบใบวางบิลนี้");
        if (invoice.Status != BillingInvoiceStatus.AwaitingOriginal)
            return Fail("INVALID_STATUS", "แจ้งพัสดุต้นฉบับได้หลัง Online Approval และก่อน LESCHACO รับเอกสารเท่านั้น");
        if (!DateOnly.TryParseExact((sentDate ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var sent) || sent > DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Thailand).DateTime))
            return Fail("INVALID_SENT_DATE", "วันที่ส่งต้นฉบับไม่ถูกต้องหรือเป็นวันที่ในอนาคต");
        var cleanCourier = (courier ?? "").Trim(); var cleanTracking = (trackingNumber ?? "").Trim();
        var cleanReference = (packageReference ?? "").Trim(); var cleanRemark = (remark ?? "").Trim();
        if (cleanCourier.Length == 0 || (cleanTracking.Length == 0 && cleanReference.Length == 0))
            return Fail("PACKAGE_INCOMPLETE", "กรุณาระบุบริษัทขนส่งและ Tracking Number หรือ Package Reference");
        if (cleanCourier.Length > 120 || cleanTracking.Length > 160 || cleanReference.Length > 160 || cleanRemark.Length > 800)
            return Fail("PACKAGE_TOO_LONG", "ข้อมูลพัสดุยาวเกินกว่าที่ระบบรองรับ");

        var now = DateTimeOffset.UtcNow;
        var package = await db.OriginalDocumentPackages.FirstOrDefaultAsync(row => row.InvoiceId == invoiceId, token);
        if (package?.ReceivedAt is not null) return Fail("ALREADY_RECEIVED", "LESCHACO รับเอกสารต้นฉบับชุดนี้แล้ว");
        var fromPackageStatus = package?.Status ?? OriginalDocumentStatus.Pending;
        package ??= new OriginalDocumentPackage { InvoiceId = invoiceId, CreatedAt = now };
        if (package.Id == 0) db.OriginalDocumentPackages.Add(package);
        package.Status = OriginalDocumentStatus.Sent; package.SentDate = sent; package.Courier = cleanCourier;
        package.TrackingNumber = cleanTracking; package.CarrierPackageReference = cleanReference;
        package.CarrierRemark = cleanRemark; package.SentBy = actor.Signature; package.SentAt = now; package.UpdatedAt = now;
        audit.Stage(actor, AuditActions.Update, "billing-original-package", invoiceId.ToString(), invoice.InvoiceNumber,
            "status", fromPackageStatus, OriginalDocumentStatus.Sent,
            $"{cleanCourier} · {(cleanTracking.Length > 0 ? cleanTracking : cleanReference)}");
        await db.SaveChangesAsync(token);
        return Success("บันทึกข้อมูลจัดส่งเอกสารต้นฉบับแล้ว", invoice.Status, false, false, package);
    }

    public async Task<OriginalDocumentMutation> ReceiveAsync(AppUser actor, long invoiceId,
        string receivedAt, int documentCount, string packageReference, string remark, CancellationToken token)
    {
        if (!receiptPolicy.CanReceive(actor)) return Fail("FORBIDDEN", receiptPolicy.IsConfigured
            ? "บัญชีนี้ไม่มีสิทธิ์รับเอกสารต้นฉบับ" : "ยังไม่ได้กำหนดบทบาทผู้รับเอกสารต้นฉบับในระบบ");
        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(row => row.Id == invoiceId, token);
        if (invoice is null) return Fail("NOT_FOUND", "ไม่พบใบวางบิลนี้");
        var package = await db.OriginalDocumentPackages.FirstOrDefaultAsync(row => row.InvoiceId == invoiceId, token);
        if (package?.ReceivedAt is not null)
            return Success("เอกสารต้นฉบับชุดนี้ถูกรับไว้แล้ว", invoice.Status,
                invoice.Status == BillingInvoiceStatus.ReadyForFinance, true, package);
        if (invoice.Status != BillingInvoiceStatus.AwaitingOriginal)
            return Fail("INVALID_STATUS", "รับต้นฉบับได้เฉพาะใบวางบิลที่อนุมัติ Online และกำลังรอเอกสาร");
        if (!DateTimeOffset.TryParse((receivedAt ?? "").Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var received) || received > DateTimeOffset.UtcNow.AddMinutes(5))
            return Fail("INVALID_RECEIVED_AT", "วันเวลาที่รับเอกสารไม่ถูกต้องหรือเป็นเวลาในอนาคต");
        if (documentCount <= 0) return Fail("INVALID_DOCUMENT_COUNT", "จำนวนเอกสารต้องมากกว่า 0");
        var cleanReference = (packageReference ?? "").Trim(); var cleanRemark = (remark ?? "").Trim();
        if (cleanReference.Length > 160 || cleanRemark.Length > 800)
            return Fail("RECEIPT_TOO_LONG", "ข้อมูลรับเอกสารยาวเกินกว่าที่ระบบรองรับ");

        var latestRun = await db.BillingValidationRuns.AsNoTracking().Where(row => row.InvoiceId == invoiceId)
            .OrderByDescending(row => row.Sequence).FirstOrDefaultAsync(token);
        var blocking = latestRun is null || await db.BillingValidationResults.AsNoTracking()
            .AnyAsync(row => row.RunId == latestRun.Id && row.Blocking, token);
        var readiness = BillingFinanceReadiness.Evaluate(invoice.OnlineApprovedAt is not null, true, blocking);
        var link = await db.BillingInvoiceJobLinks.AsNoTracking().FirstOrDefaultAsync(row => row.InvoiceId == invoiceId, token);
        if (link is null) return Fail("BILLING_CASE_NOT_FOUND", "ไม่พบ Billing Case ที่ผูกกับใบวางบิลนี้");
        var billingCase = await db.BillingCases.FirstAsync(row => row.Id == link.BillingCaseId, token);
        var now = DateTimeOffset.UtcNow;
        package ??= new OriginalDocumentPackage { InvoiceId = invoiceId, CreatedAt = now };
        if (package.Id == 0) db.OriginalDocumentPackages.Add(package);
        package.Status = OriginalDocumentStatus.Received; package.ReceivedAt = received;
        package.ReceivedById = actor.UserId; package.ReceivedByName = actor.Signature;
        package.DocumentCount = documentCount; package.ReceiptPackageReference = cleanReference;
        package.ReceiptRemark = cleanRemark; package.UpdatedAt = now;
        var from = invoice.Status; invoice.Status = readiness.InvoiceStatus; invoice.UpdatedBy = actor.Signature; invoice.UpdatedAt = now;
        billingCase.Status = readiness.InvoiceStatus; billingCase.UpdatedBy = actor.Signature; billingCase.UpdatedAt = now;
        audit.Stage(actor, AuditActions.Update, "billing-original-package", invoiceId.ToString(), invoice.InvoiceNumber,
            "original_status", from, readiness.InvoiceStatus,
            $"Original received · {documentCount} document(s) · {readiness.Code}");
        await db.SaveChangesAsync(token);
        return Success(readiness.Ready ? "รับต้นฉบับแล้ว และรายการพร้อมส่ง Finance" :
            "รับต้นฉบับแล้ว แต่รายการยังไม่ผ่าน Finance readiness", readiness.InvoiceStatus,
            readiness.Ready, false, package);
    }

    public static OriginalPackageView View(OriginalDocumentPackage row) => new(row.Id, row.Status,
        row.SentDate?.ToString("yyyy-MM-dd") ?? "", row.Courier, row.TrackingNumber,
        row.CarrierPackageReference, row.CarrierRemark, row.SentBy, row.SentAt, row.ReceivedAt,
        row.ReceivedByName, row.DocumentCount, row.ReceiptPackageReference, row.ReceiptRemark);

    private static OriginalDocumentMutation Success(string message, string invoiceStatus,
        bool ready, bool replayed, OriginalDocumentPackage package) =>
        new(true, "OK", message, invoiceStatus, ready, replayed, View(package));
    private static OriginalDocumentMutation Fail(string code, string message) =>
        new(false, code, message, "", false, false, null);
}
