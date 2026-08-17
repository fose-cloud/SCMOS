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
    public static void MapDocuments(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/documents").WithTags("Documents");

        // The folder vocabulary, so a screen offers the agreed list rather than
        // a free-text box that grows a fourth spelling of "POD".
        group.MapGet("/folders", (HttpContext context, IUserAccessor users) =>
            users.Current(context) is null
                ? ApiResults.SignInRequired
                : Results.Json(new { job = BlobPaths.JobFolders, supplier = BlobPaths.SupplierFolders }));

        group.MapGet("", async (string? jobKey, int? supplierId, long? caseId, string? folder,
            HttpContext context, IUserAccessor users, DocumentService documents, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await documents.ListAsync(jobKey, supplierId, caseId, folder, token));
        });

        group.MapPost("", async (HttpContext context, IUserAccessor users, DocumentService documents,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!context.Request.HasFormContentType)
                return ApiResults.Error("ต้องส่งเป็น multipart form", StatusCodes.Status415UnsupportedMediaType);

            var form = await context.Request.ReadFormAsync(token);
            var file = form.Files["file"];
            if (file is null) return ApiResults.Error("ต้องแนบไฟล์", StatusCodes.Status400BadRequest);

            var jobKey = Text(form, "jobKey");
            var supplierId = Number(form, "supplierId");
            var caseId = Number(form, "caseId");
            var folder = Text(form, "folder");
            var kind = Text(form, "kind");
            var note = Text(form, "note");

            // Exactly one owner. A file attached to a job *and* a supplier would
            // have to be filed in two trees, and picking one silently is how the
            // structure starts drifting from what people believe it is.
            var owners = new[] { jobKey.Length > 0, supplierId > 0, caseId > 0 }.Count(set => set);
            if (owners != 1)
                return ApiResults.Error("ระบุอย่างใดอย่างหนึ่ง: jobKey, supplierId หรือ caseId",
                    StatusCodes.Status400BadRequest);

            var result = caseId > 0
                ? await documents.AddToCaseAsync(caseId, kind, note, file, user, token)
                : supplierId > 0
                    ? await documents.AddToSupplierAsync((int)supplierId, folder, kind,
                        Text(form, "expiryDate"), note, file, user, token)
                    : await documents.AddToJobAsync(jobKey, folder, kind, note, file, user, token);

            return result.Ok
                ? Results.Json(new { message = result.Message, document = result.Document })
                : ApiResults.Error(result.Message,
                    documents.StorageReady ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable);
        }).DisableAntiforgery();

        group.MapGet("/{id:long}/content", async (long id, HttpContext context, IUserAccessor users,
            DocumentService documents, CancellationToken token) =>
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

            return Results.File(stream, document.ContentType, document.FileName, enableRangeProcessing: true);
        });
    }

    private static string Text(IFormCollection form, string name) => form[name].ToString().Trim();

    private static long Number(IFormCollection form, string name) =>
        long.TryParse(form[name].ToString(), out var value) && value > 0 ? value : 0;
}
