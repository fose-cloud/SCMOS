using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record BillingReviewEventView(long Id, int Cycle, string Action, string FromStatus,
    string ToStatus, string ReasonCode, string Remark, string ActorName, DateTimeOffset At);
public record BillingReviewMutation(bool Ok, string Code, string Message, string Status,
    IReadOnlyList<BillingReviewEventView> Events);

public class BillingReviewService(ScmosDbContext db, AuditService audit)
{
    public async Task<BillingReviewMutation> ActAsync(AppUser actor, long invoiceId, string action,
        string reasonCode, string remark, CancellationToken token)
    {
        if (CarrierTenantContext.IsCarrier(actor) || !actor.Can(Capability.ReviewBilling))
            return Fail("FORBIDDEN", "บัญชีนี้ไม่มีสิทธิ์ตรวจใบวางบิล");
        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId, token);
        if (invoice is null) return Fail("NOT_FOUND", "ไม่พบใบวางบิลนี้");
        if (!BillingReviewTransitions.CanAct(invoice.Status))
            return Fail("INVALID_STATUS", "รายการนี้ไม่ได้อยู่ในคิว Subcontract Management Review");
        var link = await db.BillingInvoiceJobLinks.AsNoTracking().FirstAsync(x => x.InvoiceId == invoiceId, token);
        var billingCase = await db.BillingCases.FirstAsync(x => x.Id == link.BillingCaseId, token);
        var latestRun = await db.BillingValidationRuns.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(token);
        if (latestRun is null || await db.BillingValidationResults.AsNoTracking().AnyAsync(x => x.RunId == latestRun.Id && x.Blocking, token))
            return Fail("BLOCKING_VALIDATION", "ยังมี Validation ที่บล็อกการ Review");

        var cleanAction = (action ?? "").Trim().ToUpperInvariant();
        var cleanReason = (reasonCode ?? "").Trim().ToUpperInvariant();
        var cleanRemark = (remark ?? "").Trim();
        var to = BillingReviewTransitions.NextStatus(cleanAction);
        if (to.Length == 0) return Fail("INVALID_ACTION", "คำสั่ง Review ไม่ถูกต้อง");
        if (cleanAction != BillingReviewAction.ApproveOnline
            && BillingReturnReason.Problem(cleanReason, cleanRemark) is { } problem)
            return Fail("INVALID_REASON", problem);

        var now = DateTimeOffset.UtcNow; var from = invoice.Status;
        invoice.Status = to; invoice.ReviewDecidedAt = now; invoice.UpdatedBy = actor.Signature; invoice.UpdatedAt = now;
        if (cleanAction == BillingReviewAction.ApproveOnline) invoice.OnlineApprovedAt = now;
        billingCase.Status = to; billingCase.UpdatedBy = actor.Signature; billingCase.UpdatedAt = now;
        db.BillingReviewEvents.Add(new BillingReviewEvent { InvoiceId = invoice.Id, Cycle = invoice.ReviewCycle,
            Action = cleanAction, FromStatus = from, ToStatus = to, ReasonCode = cleanReason,
            Remark = cleanRemark, ActorId = actor.UserId, ActorName = actor.Signature, At = now });
        audit.Stage(actor, cleanAction == BillingReviewAction.ApproveOnline ? AuditActions.Approve : AuditActions.Reject,
            "billing-invoice", invoice.Id.ToString(), invoice.InvoiceNumber, "status", from, to,
            cleanReason.Length == 0 ? cleanRemark : $"{cleanReason}: {cleanRemark}");
        await db.SaveChangesAsync(token);
        return new(true, "OK", cleanAction switch {
            BillingReviewAction.ApproveOnline => "อนุมัติ Online Billing แล้ว และรอเอกสารต้นฉบับ",
            BillingReviewAction.ReturnToCarrier => "คืนรายการให้ผู้ขนส่งแก้ไขแล้ว",
            _ => "เปิดข้อพิพาทแล้ว",
        }, to, await EventsAsync(invoice.Id, token));
    }

    public async Task<IReadOnlyList<BillingReviewEventView>> EventsAsync(long invoiceId, CancellationToken token) =>
        await db.BillingReviewEvents.AsNoTracking().Where(x => x.InvoiceId == invoiceId).OrderBy(x => x.Id)
            .Select(x => new BillingReviewEventView(x.Id, x.Cycle, x.Action, x.FromStatus, x.ToStatus,
                x.ReasonCode, x.Remark, x.ActorName, x.At)).ToListAsync(token);
    private static BillingReviewMutation Fail(string code, string message) => new(false, code, message, "", []);
}
