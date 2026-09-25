using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class CarrierBillingEndpoints
{
    public record DraftInput(string? InvoiceNumber, string? InvoiceDate, string? Currency,
        decimal Subtotal, decimal TaxAmount);

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
