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
///
/// <para>
/// The billing routes call the services the Carrier Portal calls
/// (<see cref="CarrierBillingService"/>, <see cref="BillingValidationService"/>
/// through it, <see cref="OriginalDocumentService"/>) with the key's supplier —
/// BR-016: one business logic for both doors. What the TMS is shown is an
/// explicit copy of the portal's projection, without storage coordinates and
/// without the names of the LESCHACO people who reviewed or received.
/// </para>
/// </summary>
public static class CarrierBillingApiEndpoints
{
    public record DraftBody(string? InvoiceNumber, string? InvoiceDate, string? Currency,
        decimal Subtotal, decimal TaxAmount);

    /// <summary>The carrier's side of the original-document package: when it went, with whom, and how to trace it.</summary>
    public record OriginalPackageBody(string? SentDate, string? Courier, string? TrackingNumber,
        string? PackageReference, string? Remark);

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

        // The jobs ready to bill: a Billing Case opened at Delivery Complete and not yet drafted.
        v1.MapGet("/billing/eligible", async (int? page, int? pageSize, HttpContext context,
            CarrierBillingService billing, CancellationToken token) =>
        {
            var who = CarrierApiEndpoints.PrincipalOf(context);
            var (pageNo, size) = CarrierApi.Page(page, pageSize);
            var (items, total) = await billing.PageForCarrierAsync(who.Company.Id,
                [BillingCaseStatus.WaitingCarrierSubmission], pageNo, size, token);
            return Results.Json(new
            {
                items = items.Select(PublicBillingCase).ToList(),
                page = pageNo,
                pageSize = size,
                total,
                correlationId = context.TraceIdentifier,
            });
        });

        // Every Billing Case of this carrier's, or those in the statuses asked for (a comma list).
        v1.MapGet("/billing/cases", async (string? status, int? page, int? pageSize, HttpContext context,
            CarrierBillingService billing, CancellationToken token) =>
        {
            var who = CarrierApiEndpoints.PrincipalOf(context);
            var statuses = CarrierApi.BillingStatuses(status);
            if (statuses is null)
                return CarrierApiEndpoints.Refuse(context, CarrierApi.Invalid,
                    "status must be a comma list of: " + string.Join(", ", CarrierApi.BillingCaseStatuses));
            var (pageNo, size) = CarrierApi.Page(page, pageSize);
            var (items, total) = await billing.PageForCarrierAsync(who.Company.Id, statuses, pageNo, size, token);
            return Results.Json(new
            {
                items = items.Select(PublicBillingCase).ToList(),
                page = pageNo,
                pageSize = size,
                total,
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
                    new { caseId, invoice = PublicInvoice(result.Invoice), replayed = result.Replayed,
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
                    invoice = PublicInvoice(result.Invoice),
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
                    kind = form.Kind,
                    invoice = PublicInvoice(result.Invoice),
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

        // The carrier's side of the original documents (Phase 7): sent on this date, with this courier, traceable so.
        v1.MapPut("/billing/invoices/{invoiceId:long}/original-package", async (long invoiceId,
            [FromBody] OriginalPackageBody? body, HttpContext context, OriginalDocumentService originals,
            CancellationToken token) =>
            await CarrierApiEndpoints.IdempotentAsync(context, "PUT", body, async () =>
            {
                if (body is null)
                    return CarrierApiEndpoints.Problem(context, CarrierApi.Invalid, "An original-package body is required");
                var who = CarrierApiEndpoints.PrincipalOf(context);
                var result = await originals.SaveCarrierPackageForAsync(who.AsUser(), who.Company.Id, invoiceId,
                    body.SentDate ?? "", body.Courier ?? "", body.TrackingNumber ?? "",
                    body.PackageReference ?? "", body.Remark ?? "", token);
                if (!result.Ok)
                {
                    var code = result.Code switch
                    {
                        "NOT_FOUND" => CarrierApi.NotFound,
                        "INVALID_STATUS" or "ALREADY_RECEIVED" => CarrierApi.Conflict,
                        "FORBIDDEN" => CarrierApi.Forbidden,
                        _ => CarrierApi.Invalid,
                    };
                    return CarrierApiEndpoints.Problem(context, code, result.Message, new { reason = result.Code });
                }
                return CarrierApiEndpoints.Answer(StatusCodes.Status200OK, new
                {
                    invoiceId,
                    status = result.InvoiceStatus,
                    package = PublicPackage(result.Package),
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

        // Where the invoice stands, in one small answer: the invoice and case statuses, the SLA,
        // the review, the originals and whether it is ready for Finance. No payment details:
        // what a carrier is told about payment is a business decision still to be made.
        v1.MapGet("/billing/invoices/{invoiceId:long}/status", async (long invoiceId,
            HttpContext context, CarrierBillingService billing, CancellationToken token) =>
        {
            var who = CarrierApiEndpoints.PrincipalOf(context);
            var item = await billing.FindInvoiceForAsync(who.Company.Id, invoiceId, token);
            if (item?.Invoice is not { } invoice)
                return CarrierApiEndpoints.Refuse(context, CarrierApi.NotFound,
                    "No billing invoice with this id belongs to this carrier");
            var last = invoice.ReviewEvents.OrderByDescending(one => one.At).FirstOrDefault();
            return Results.Json(new
            {
                invoiceId,
                invoiceNumber = invoice.InvoiceNumber,
                status = invoice.Status,
                caseId = item.Id,
                caseStatus = item.Status,
                jobKey = item.JobKey,
                jobCode = item.JobCode,
                sla = new
                {
                    state = item.SlaState, startDate = item.SlaStartDate, dueDate = item.SlaDueDate,
                    daysRemaining = item.DaysRemaining, targetWorkingDays = item.SlaTargetWorkingDays,
                    issueCode = item.SlaIssueCode,
                },
                review = new
                {
                    cycle = invoice.ReviewCycle,
                    submittedAt = invoice.ReviewSubmittedAt,
                    decidedAt = invoice.ReviewDecidedAt,
                    onlineApprovedAt = invoice.OnlineApprovedAt,
                    last = last is null ? null : new { last.Action, last.ReasonCode, last.Remark, last.At },
                },
                blockingResults = invoice.ValidationResults.Count(one => one.Blocking),
                original = PublicPackage(invoice.OriginalPackage),
                readyForFinance = invoice.Status == BillingInvoiceStatus.ReadyForFinance,
                correlationId = context.TraceIdentifier,
            });
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

    /// <summary>
    /// The multipart upload, held to the door's standard before anything is
    /// stored: one file, not empty, within the size the document service
    /// keeps, a declared type on <see cref="CarrierApi.UploadTypes"/> whose
    /// first bytes agree, and a kind of the shape the billing rules name.
    /// </summary>
    private static async Task<(IFormFile? File, string Kind, string Note, string? Error)> ReadUploadAsync(
        HttpContext context, string defaultKind, CancellationToken token)
    {
        if (!context.Request.HasFormContentType)
            return (null, defaultKind, "", "Content-Type must be multipart/form-data");
        var form = await context.Request.ReadFormAsync(token);
        var file = form.Files["file"];
        var kind = CarrierApi.DocumentKind(form["kind"].ToString(), defaultKind);
        var note = form["note"].ToString().Trim();
        if (kind is null) return (null, defaultKind, note, "kind is lower-case letters, digits, '_' and '-', at most 60");
        if (file is null) return (null, kind, note, "A file is required in form field 'file'");
        if (file.Length == 0) return (null, kind, note, "The file is empty");
        if (file.Length > DocumentService.MaxBytes)
            return (null, kind, note, $"The file is larger than {DocumentService.MaxBytes} bytes");
        if (note.Length > 500) return (null, kind, note, "note is longer than 500 characters");
        var head = new byte[16];
        await using (var stream = file.OpenReadStream())
        {
            var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, token);
            if (CarrierApi.UploadProblem(file.ContentType, head.AsSpan(0, read)) is { } problem)
                return (null, kind, note, problem);
        }
        return (file, kind, note, null);
    }

    private static async Task<UploadFingerprint> FingerprintAsync(IFormFile file, string kind,
        string note, CancellationToken token)
    {
        await using var stream = file.OpenReadStream();
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
        return new(file.FileName, file.ContentType ?? "", file.Length, hash, kind, note);
    }

    /// <summary>A stored document as the TMS sees it — never its Blob URL or object key.</summary>
    internal static object PublicDocument(DocumentView document) => new
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
    /// The invoice as the TMS sees it: amounts, validation, charges, the
    /// review's actions and reasons — without the LESCHACO reviewer's or
    /// receiver's name, which the portal does not show a carrier either.
    /// </summary>
    internal static object? PublicInvoice(BillingInvoiceView? invoice) => invoice is null ? null : new
    {
        invoice.Id,
        invoice.InvoiceNumber,
        invoice.InvoiceDate,
        invoice.Currency,
        invoice.Subtotal,
        invoice.TaxAmount,
        invoice.TotalAmount,
        invoice.Status,
        invoice.UpdatedAt,
        invoice.ValidationResults,
        invoice.AdditionalCharges,
        invoice.ReviewCycle,
        invoice.ReviewSubmittedAt,
        invoice.ReviewDecidedAt,
        invoice.OnlineApprovedAt,
        ReviewEvents = invoice.ReviewEvents.Select(one => new
        {
            one.Id, one.Cycle, one.Action, one.FromStatus, one.ToStatus, one.ReasonCode, one.Remark, one.At,
        }).ToList(),
        OriginalPackage = PublicPackage(invoice.OriginalPackage),
    };

    /// <summary>The original-document package, the carrier's fields and whether (and when) it arrived — not who signed for it.</summary>
    internal static object? PublicPackage(OriginalPackageView? package) => package is null ? null : new
    {
        package.Status,
        package.SentDate,
        package.Courier,
        package.TrackingNumber,
        PackageReference = package.CarrierPackageReference,
        Remark = package.CarrierRemark,
        package.SentAt,
        package.ReceivedAt,
        package.DocumentCount,
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
        Invoice = PublicInvoice(item.Invoice),
        Documents = item.Documents.Select(PublicDocument).ToList(),
    };
}
