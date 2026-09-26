using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Phase 9: POD and online billing through the existing Carrier API v1 door.
/// The parent route group already authenticates, rate-limits and assigns a
/// correlation id. Every supplier id below comes from that authenticated key.
/// </summary>
public static class CarrierBillingApiEndpoints
{
    public record DraftBody(string? InvoiceNumber, string? InvoiceDate, string? Currency,
        decimal Subtotal, decimal TaxAmount);

    private sealed record UploadFingerprint(string FileName, string ContentType, long SizeBytes,
        string Sha256, string Kind, string Note);

    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost("/assignments/{jobKey}/pod", async (string jobKey, HttpContext context,
            CarrierService carriers, DocumentService documents, AuditService audit,
            CancellationToken token) =>
        {
            if (!HasIdempotencyKey(context)) return MissingIdempotencyKey(context);
            var form = await ReadUploadAsync(context, "pod", token);
            if (form.Error is not null) return CarrierApiEndpoints.Refuse(context,
                CarrierApi.Invalid, form.Error);
            var fingerprint = await FingerprintAsync(form.File!, "pod", form.Note, token);
            return await CarrierApiEndpoints.IdempotentAsync(context, "POST", fingerprint, async () =>
            {
                var who = CarrierApiEndpoints.PrincipalOf(context);
                if (!await carriers.OwnsHeldJobAsync(who.Company, jobKey, token))
                    return CarrierApiEndpoints.Problem(context, CarrierApi.NotFound,
                        "No accepted assignment with this id is held by this carrier");
                if (!documents.StorageReady)
                    return CarrierApiEndpoints.Problem(context, CarrierApi.Unavailable,
                        "Document storage is not configured");

                var actor = who.AsUser();
                var result = await documents.AddToJobAsync(jobKey, "POD", "pod", form.Note,
                    form.File!, actor, token);
                if (!result.Ok)
                    return CarrierApiEndpoints.Problem(context, CarrierApi.Invalid, result.Message);
                await audit.RecordAsync(actor, AuditActions.Upload, "job", jobKey, jobKey,
                    "pod", "", result.Document!.Id.ToString(), form.Note, token, EventSource.CarrierApi);
                return CarrierApiEndpoints.Answer(StatusCodes.Status201Created, new
                {
                    jobKey,
                    document = PublicDocument(result.Document),
                    correlationId = context.TraceIdentifier,
                });
            });
        }).DisableAntiforgery();

        v1.MapGet("/billing/eligible", async (int? page, int? pageSize, HttpContext context,
            CarrierBillingService billing, CancellationToken token) =>
        {
            var who = CarrierApiEndpoints.PrincipalOf(context);
            var result = await billing.ListForCarrierAsync(who.Company.Id, token);
            var all = result.Items.Where(row =>
                row.Status == BillingCaseStatus.WaitingCarrierSubmission).ToList();
            var (pageNo, size) = CarrierApi.Page(page, pageSize);
            return Results.Json(new
            {
                items = all.Skip((pageNo - 1) * size).Take(size).Select(PublicBillingCase).ToList(),
                page = pageNo,
                pageSize = size,
                total = all.Count,
                correlationId = context.TraceIdentifier,
            });
        });

        v1.MapPost("/billing/cases/{caseId:long}/draft", async (long caseId, HttpContext context,
            CarrierBillingService billing, CancellationToken token) =>
            await CarrierApiEndpoints.IdempotentAsync(context, "POST", null, async () =>
            {
                var who = CarrierApiEndpoints.PrincipalOf(context);
                var result = await billing.CreateDraftForAsync(who.AsUser(), who.Company.Id, caseId, token);
                if (!result.Ok) return MutationProblem(context, result);
                return CarrierApiEndpoints.Answer(result.Replayed ? StatusCodes.Status200OK : StatusCodes.Status201Created,
                    new { caseId, invoice = result.Invoice, replayed = result.Replayed,
                        correlationId = context.TraceIdentifier });
            }));

        v1.MapPut("/billing/invoices/{invoiceId:long}", async (long invoiceId, [FromBody] DraftBody? body,
            HttpContext context, CarrierBillingService billing, CancellationToken token) =>
            await CarrierApiEndpoints.IdempotentAsync(context, "PUT", body, async () =>
            {
                if (body is null)
                    return CarrierApiEndpoints.Problem(context, CarrierApi.Invalid, "A billing draft body is required");
                var who = CarrierApiEndpoints.PrincipalOf(context);
                var result = await billing.UpdateDraftForAsync(who.AsUser(), who.Company.Id, invoiceId,
                    body.InvoiceNumber ?? "", body.InvoiceDate ?? "", body.Currency ?? "THB",
                    body.Subtotal, body.TaxAmount, token);
                if (!result.Ok) return MutationProblem(context, result);
                return CarrierApiEndpoints.Answer(StatusCodes.Status200OK, new
                {
                    invoice = result.Invoice,
                    correlationId = context.TraceIdentifier,
                });
            }));

        v1.MapPost("/billing/invoices/{invoiceId:long}/documents", async (long invoiceId,
            HttpContext context, CarrierBillingService billing, DocumentService documents,
            CancellationToken token) =>
        {
            if (!HasIdempotencyKey(context)) return MissingIdempotencyKey(context);
            var form = await ReadUploadAsync(context, "invoice", token);
            if (form.Error is not null) return CarrierApiEndpoints.Refuse(context,
                CarrierApi.Invalid, form.Error);
            var fingerprint = await FingerprintAsync(form.File!, form.Kind, form.Note, token);
            return await CarrierApiEndpoints.IdempotentAsync(context, "POST", fingerprint, async () =>
            {
                if (!documents.StorageReady)
                    return CarrierApiEndpoints.Problem(context, CarrierApi.Unavailable,
                        "Document storage is not configured");
                var who = CarrierApiEndpoints.PrincipalOf(context);
                var result = await billing.AddDocumentForAsync(who.AsUser(), who.Company.Id,
                    invoiceId, form.Kind, form.Note, form.File!, token);
                if (!result.Ok) return MutationProblem(context, result);
                return CarrierApiEndpoints.Answer(StatusCodes.Status201Created, new
                {
                    invoiceId,
                    invoice = result.Invoice,
                    correlationId = context.TraceIdentifier,
                });
            });
        }).DisableAntiforgery();

        v1.MapPost("/billing/invoices/{invoiceId:long}/submit", async (long invoiceId,
            HttpContext context, CarrierBillingService billing, CancellationToken token) =>
            await CarrierApiEndpoints.IdempotentAsync(context, "POST", null, async () =>
            {
                var who = CarrierApiEndpoints.PrincipalOf(context);
                var result = await billing.SubmitForAsync(who.AsUser(), who.Company.Id, invoiceId, token);
                if (!result.Ok) return SubmitProblem(context, result);
                return CarrierApiEndpoints.Answer(StatusCodes.Status200OK, new
                {
                    invoiceId,
                    status = result.Status,
                    results = result.Results,
                    correlationId = context.TraceIdentifier,
                });
            }));

        v1.MapGet("/billing/invoices/{invoiceId:long}", async (long invoiceId,
            HttpContext context, CarrierBillingService billing, CancellationToken token) =>
        {
            var who = CarrierApiEndpoints.PrincipalOf(context);
            var item = await billing.FindInvoiceForAsync(who.Company.Id, invoiceId, token);
            return item is null
                ? CarrierApiEndpoints.Refuse(context, CarrierApi.NotFound,
                    "No billing invoice with this id belongs to this carrier")
                : Results.Json(new { item = PublicBillingCase(item), correlationId = context.TraceIdentifier });
        });

        v1.MapGet("/billing/invoices/{invoiceId:long}/validation", async (long invoiceId,
            HttpContext context, CarrierBillingService billing, CancellationToken token) =>
        {
            var who = CarrierApiEndpoints.PrincipalOf(context);
            var item = await billing.FindInvoiceForAsync(who.Company.Id, invoiceId, token);
            if (item?.Invoice is null)
                return CarrierApiEndpoints.Refuse(context, CarrierApi.NotFound,
                    "No billing invoice with this id belongs to this carrier");
            return Results.Json(new
            {
                invoiceId,
                status = item.Invoice.Status,
                results = item.Invoice.ValidationResults,
                correlationId = context.TraceIdentifier,
            });
        });
    }

    private static CarrierApiEndpoints.Written MutationProblem(HttpContext context, BillingMutation result)
    {
        var code = result.Code switch
        {
            "NO_CARRIER" => CarrierApi.Forbidden,
            "NOT_FOUND" => CarrierApi.NotFound,
            "INVALID" or "DOCUMENT_ERROR" => CarrierApi.Invalid,
            "NOT_DRAFT" or "DUPLICATE_INVOICE_NUMBER" => CarrierApi.Conflict,
            _ => CarrierApi.Unavailable,
        };
        return CarrierApiEndpoints.Problem(context, code, result.Message,
            new { reason = result.Code });
    }

    private static CarrierApiEndpoints.Written SubmitProblem(HttpContext context, BillingSubmitResult result)
    {
        var code = result.Code switch
        {
            "NO_CARRIER" => CarrierApi.Forbidden,
            "NOT_FOUND" => CarrierApi.NotFound,
            "INVALID" => CarrierApi.Invalid,
            _ => CarrierApi.Conflict,
        };
        return CarrierApiEndpoints.Problem(context, code, result.Message,
            new { reason = result.Code, status = result.Status, results = result.Results });
    }

    private static bool HasIdempotencyKey(HttpContext context) =>
        CarrierApi.IdempotencyKeyOf(context.Request.Headers[CarrierApi.IdempotencyHeader]) is not null;

    private static IResult MissingIdempotencyKey(HttpContext context) =>
        CarrierApiEndpoints.Refuse(context, CarrierApi.Invalid,
            $"{CarrierApi.IdempotencyHeader} header is required: 1–128 printable characters, unique per request");

    private static async Task<(IFormFile? File, string Kind, string Note, string? Error)> ReadUploadAsync(
        HttpContext context, string defaultKind, CancellationToken token)
    {
        if (!context.Request.HasFormContentType)
            return (null, defaultKind, "", "Content-Type must be multipart/form-data");
        var form = await context.Request.ReadFormAsync(token);
        var file = form.Files["file"];
        var kind = form["kind"].ToString().Trim().ToLowerInvariant();
        if (kind.Length == 0) kind = defaultKind;
        var note = form["note"].ToString().Trim();
        if (file is null) return (null, kind, note, "A file is required in form field 'file'");
        if (file.Length == 0) return (null, kind, note, "The file is empty");
        if (file.Length > DocumentService.MaxBytes)
            return (null, kind, note, $"The file is larger than {DocumentService.MaxBytes} bytes");
        if (kind.Length > 60) return (null, kind, note, "kind is longer than 60 characters");
        if (note.Length > 500) return (null, kind, note, "note is longer than 500 characters");
        return (file, kind, note, null);
    }

    private static async Task<UploadFingerprint> FingerprintAsync(IFormFile file, string kind,
        string note, CancellationToken token)
    {
        await using var stream = file.OpenReadStream();
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
        return new(file.FileName, file.ContentType ?? "", file.Length, hash, kind, note);
    }

    private static object PublicDocument(DocumentView document) => new
    {
        document.Id,
        document.Folder,
        document.Kind,
        document.FileName,
        document.ContentType,
        document.SizeBytes,
        document.Note,
        document.UploadedAt,
    };

    /// <summary>
    /// The internal billing projection intentionally carries storage coordinates
    /// for authenticated SCMOS screens. The TMS contract gets an explicit copy
    /// so a later field added to that projection cannot accidentally expose a
    /// Blob URL or object key through this external boundary.
    /// </summary>
    private static object PublicBillingCase(BillingCaseView item) => new
    {
        item.Id,
        item.JobKey,
        item.JobCode,
        item.Customer,
        item.Category,
        item.SupplierId,
        item.Supplier,
        item.Status,
        item.DeliveryCompletedAt,
        item.SlaRuleCode,
        item.SlaStartDay,
        item.SlaTargetWorkingDays,
        item.SlaStartDate,
        item.SlaDueDate,
        item.SlaState,
        item.DaysRemaining,
        item.SlaIssueCode,
        item.SlaIssue,
        item.Invoice,
        Documents = item.Documents.Select(PublicDocument).ToList(),
    };
}
