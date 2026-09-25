using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class CarrierBillingEndpoints
{
    public record DraftInput(string? InvoiceNumber, string? InvoiceDate, string? Currency,
        decimal Subtotal, decimal TaxAmount);
    public record ChargeInput(string? ChargeType, decimal RequestedAmount, string? Currency,
        string? Reason, long? EvidenceDocumentId);

    public static void MapCarrierBilling(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/carrier-billing").WithTags("Carrier Billing");

        group.MapGet("/cases", async (HttpContext context, IUserAccessor users,
            CarrierBillingService billing, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await billing.ListAsync(user, token);
            return result.Ok
                ? Results.Json(new { items = result.Items })
                : ApiResults.Error(result.Message, StatusCodes.Status403Forbidden);
        });

        group.MapPost("/cases/{caseId:long}/draft", async (long caseId,
            HttpContext context, IUserAccessor users, CarrierBillingService billing,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await billing.CreateDraftAsync(user, caseId, token);
            return Reply(result);
        });

        group.MapPut("/invoices/{invoiceId:long}", async (long invoiceId,
            [FromBody] DraftInput body, HttpContext context, IUserAccessor users,
            CarrierBillingService billing, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await billing.UpdateDraftAsync(user, invoiceId,
                body.InvoiceNumber ?? "", body.InvoiceDate ?? "", body.Currency ?? "THB",
                body.Subtotal, body.TaxAmount, token);
            return Reply(result);
        });

        group.MapPost("/invoices/{invoiceId:long}/documents", async (long invoiceId,
            HttpContext context, IUserAccessor users, CarrierBillingService billing,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.UploadDocuments))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์อัปโหลดเอกสาร", StatusCodes.Status403Forbidden);
            if (!context.Request.HasFormContentType)
                return ApiResults.Error("ต้องส่งเป็น multipart form", StatusCodes.Status415UnsupportedMediaType);
            var form = await context.Request.ReadFormAsync(token);
            var file = form.Files["file"];
            if (file is null) return ApiResults.Error("ต้องแนบไฟล์", StatusCodes.Status400BadRequest);
            var result = await billing.AddDocumentAsync(user, invoiceId,
                form["kind"].ToString(), form["note"].ToString(), file, token);
            return Reply(result);
        }).DisableAntiforgery();

        group.MapPost("/invoices/{invoiceId:long}/submit", async (long invoiceId,
            HttpContext context, IUserAccessor users, CarrierBillingService billing, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await billing.SubmitAsync(user, invoiceId, token);
            return result.Ok ? Results.Json(new { message = result.Message, status = result.Status, results = result.Results })
                : ApiResults.Error(result.Message, result.Code is "NO_CARRIER" ? 403 : result.Code is "NOT_FOUND" ? 404 : 409);
        });

        group.MapPost("/invoices/{invoiceId:long}/charges", async (long invoiceId,
            [FromBody] ChargeInput body, HttpContext context, IUserAccessor users,
            CarrierTenantContext tenants, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context); if (user is null) return ApiResults.SignInRequired;
            var tenant = CarrierTenantContext.IsCarrier(user) ? await tenants.ResolveAsync(user, token) : null;
            if (tenant is null) return ApiResults.Error("บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา", 403);
            var invoice = await db.BillingInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId && x.SupplierId == tenant.SupplierId, token);
            if (invoice is null) return ApiResults.Error("ไม่พบใบวางบิลนี้", 404);
            if (invoice.Status != BillingInvoiceStatus.Draft && invoice.Status != BillingInvoiceStatus.Blocked)
                return ApiResults.Error("เพิ่มค่าใช้จ่ายได้เฉพาะรายการที่ยังไม่ผ่าน Validation", 409);
            var type = (body.ChargeType ?? "").Trim().ToUpperInvariant(); var currency = (body.Currency ?? invoice.Currency).Trim().ToUpperInvariant();
            if (type.Length == 0 || body.RequestedAmount <= 0 || currency.Length != 3)
                return ApiResults.Error("ประเภท ยอดเงิน หรือสกุลเงินไม่ถูกต้อง", 400);
            var row = new BillingAdditionalCharge { InvoiceId = invoiceId, ChargeType = type,
                RequestedAmount = decimal.Round(body.RequestedAmount, 2), Currency = currency,
                Reason = (body.Reason ?? "").Trim(), EvidenceDocumentId = body.EvidenceDocumentId,
                Status = "REQUESTED", RequestedBy = user.Signature, RequestedAt = DateTimeOffset.UtcNow };
            db.BillingAdditionalCharges.Add(row);
            audit.Stage(user, AuditActions.Register, "billing-additional-charge", invoiceId.ToString(),
                type, "requested_amount", "", row.RequestedAmount.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Reason);
            await db.SaveChangesAsync(token);
            return Results.Json(new { message = "บันทึกคำขอค่าใช้จ่ายเพิ่มเติมแล้ว", row });
        });
    }

    private static IResult Reply(BillingMutation result) => result.Ok
        ? Results.Json(new { message = result.Message, replayed = result.Replayed,
            item = result.Case, invoice = result.Invoice })
        : ApiResults.Error(result.Message, result.Code switch
        {
            "NO_CARRIER" => StatusCodes.Status403Forbidden,
            "NOT_FOUND" => StatusCodes.Status404NotFound,
            "NOT_DRAFT" or "DUPLICATE_INVOICE_NUMBER" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });
}
