using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Uploading and reading the files the operation produces.
///
/// The caller never names a path. It says what the file is attached to and what
/// kind it is; where it lands is decided by <see cref="BlobPaths"/> from the
/// register. Reading goes through here rather than the blob URL, because the
/// container is private and must stay that way — a URL that works without a
/// sign-in is a URL that ends up forwarded.
/// </summary>
public static class DocumentEndpoints
{
    /// <param name="Extend">True to keep the document longer; false to approve its destruction.</param>
    public record RetentionDecision(bool? Extend, string? Reason);

    public static void MapDocuments(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/documents").WithTags("Documents");

        // The folder vocabulary, so a screen offers the agreed list rather than
        // a free-text box that grows a fourth spelling of "POD".
        group.MapGet("/folders", (HttpContext context, IUserAccessor users) =>
            users.Current(context) is null
                ? ApiResults.SignInRequired
                : Results.Json(new { job = BlobPaths.JobFolders, supplier = BlobPaths.SupplierFolders }));

        group.MapGet("", async (string? jobKey, int? supplierId, long? caseId, long? issueId, string? folder,
            HttpContext context, IUserAccessor users, DocumentService documents, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await documents.ListAsync(jobKey, supplierId, caseId, issueId, folder, token));
        });

        group.MapPost("", async (HttpContext context, IUserAccessor users, DocumentService documents,
            AuditService audit, CancellationToken token) =>
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

            var jobKey = Text(form, "jobKey");
            var supplierId = Number(form, "supplierId");
            var caseId = Number(form, "caseId");
            var issueId = Number(form, "issueId");
            var folder = Text(form, "folder");
            var kind = Text(form, "kind");
            var note = Text(form, "note");

            // Exactly one owner. A file attached to a job *and* a supplier would
            // have to be filed in two trees, and picking one silently is how the
            // structure starts drifting from what people believe it is.
            var owners = new[] { jobKey.Length > 0, supplierId > 0, caseId > 0, issueId > 0 }
                .Count(set => set);
            if (owners != 1)
                return ApiResults.Error("ระบุอย่างใดอย่างหนึ่ง: jobKey, supplierId, caseId หรือ issueId",
                    StatusCodes.Status400BadRequest);

            var result = caseId > 0
                ? await documents.AddToCaseAsync(caseId, kind, note, file, user, token)
                : issueId > 0
                    ? await documents.AddToIssueAsync(issueId, kind, note, file, user, token)
                : supplierId > 0
                    ? await documents.AddToSupplierAsync((int)supplierId, folder, kind,
                        Text(form, "expiryDate"), note, file, user, token)
                    : await documents.AddToJobAsync(jobKey, folder, kind, note, file, user, token);

            if (!result.Ok)
                return ApiResults.Error(result.Message,
                    documents.StorageReady ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable);

            // The blob path is the new value on purpose: it is what somebody
            // needs a year later to find the file this row is talking about.
            await audit.RecordAsync(user, AuditActions.Upload, "document",
                result.Document!.Id.ToString(), result.Document.FileName, result.Document.Folder,
                "", result.Document.ObjectKey, note, token);

            return Results.Json(new { message = result.Message, document = result.Document });
        }).DisableAntiforgery();

        /// The ten-year retention review.
        ///
        /// Read-only, and there is no companion route that deletes what it
        /// lists. Approving destruction is recorded as a decision in the audit
        /// trail; carrying it out is a deliberate act somebody performs against
        /// the storage account, not something this API can be talked into.
        group.MapGet("/retention", async (bool? all, HttpContext context, IUserAccessor users,
            DocumentService documents, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDirectory))
                return ApiResults.Error("ดูรายการเก็บรักษาเอกสารได้เฉพาะระดับหัวหน้างานขึ้นไป",
                    StatusCodes.Status403Forbidden);

            var items = await documents.RetentionReviewAsync(all == true, token);
            return Results.Json(new
            {
                items,
                policy = new
                {
                    hotDays = Retention.CoolAfterDays,
                    coolDays = Retention.ArchiveAfterDays,
                    retentionDays = Retention.RetentionDays,
                    reviewWindowDays = Retention.ReviewWindowDays,
                    automaticDeletion = false,
                },
            });
        });

        group.MapPost("/retention/{id:long}", async (long id, RetentionDecision body, HttpContext context,
            IUserAccessor users, DocumentService documents, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ApproveRetention))
                return ApiResults.Error("อนุมัติการเก็บรักษาได้เฉพาะระดับผู้จัดการขึ้นไป",
                    StatusCodes.Status403Forbidden);

            var reason = (body.Reason ?? "").Trim();
            if (reason.Length == 0)
                return ApiResults.Error("ต้องระบุเหตุผลของการตัดสินใจ", StatusCodes.Status400BadRequest);

            var document = await documents.FindAsync(id, token);
            if (document is null) return ApiResults.Error("ไม่พบไฟล์นี้", StatusCodes.Status404NotFound);

            var decision = body.Extend == true ? "extend" : "approve-destruction";
            await audit.RecordAsync(user, AuditActions.RetentionReview, "document", id.ToString(),
                document.FileName, "retention", Retention.StateFor(Retention.AgeDays(document.UploadedAt)),
                decision, reason, token);

            // The decision is recorded; the file is not touched. Destroying it is
            // a separate, deliberate act against the storage account, so that
            // "somebody approved this" and "it is gone" are never the same event.
            return Results.Json(new
            {
                message = decision == "extend"
                    ? "บันทึกการขยายระยะเก็บรักษาแล้ว"
                    : "บันทึกการอนุมัติทำลายแล้ว — ระบบไม่ได้ลบไฟล์ ต้องดำเนินการกับที่เก็บแยกต่างหาก",
                decision,
            });
        });

        // `?inline=1` asks for the file to be shown rather than saved. Whether it
        // may be is InlineViewing's decision, not the caller's and not the
        // uploader's — see that file for why the stored content type is no part
        // of it.
        group.MapGet("/{id:long}/content", async (long id, string? inline, HttpContext context,
            IUserAccessor users, DocumentService documents, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var document = await documents.FindAsync(id, token);
            if (document is null) return ApiResults.Error("ไม่พบไฟล์นี้", StatusCodes.Status404NotFound);
            if (!documents.StorageReady)
                return ApiResults.Error("ยังไม่ได้ตั้งค่าที่เก็บไฟล์", StatusCodes.Status503ServiceUnavailable);

            var stream = await documents.OpenAsync(document, token);
            // The row says the file is there and the container says otherwise.
            // Reported as a 404 with the key, not a 500 — it is a real state
            // somebody has to go and look at, not a crash.
            if (stream is null)
                return ApiResults.Error($"ไฟล์หายจากที่เก็บ: {document.ObjectKey}", StatusCodes.Status404NotFound);

            // Stops a browser deciding for itself that a file is really HTML,
            // on the way out and on the way down alike.
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            var shown = Asked(inline) ? InlineViewing.TypeFor(document.FileName) : null;
            if (shown is not null)
            {
                context.Response.Headers["Content-Security-Policy"] = InlineViewing.Policy;
                // No download name is what makes the browser show it: naming the
                // file here is what sets Content-Disposition to attachment.
                return Results.File(stream, shown, enableRangeProcessing: true);
            }

            return Results.File(stream, document.ContentType, document.FileName, enableRangeProcessing: true);
        });
    }

    /// <summary>Whether `?inline=` asks for the file to be displayed.</summary>
    private static bool Asked(string? value) =>
        value is "1" or "true" or "yes" or "";

    private static string Text(IFormCollection form, string name) => form[name].ToString().Trim();

    private static long Number(IFormCollection form, string name) =>
        long.TryParse(form[name].ToString(), out var value) && value > 0 ? value : 0;
}
