using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class UploadsEndpoints
{
    private const long MaxBytes = 32L * 1024 * 1024;

    public static void MapUploads(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/uploads").WithTags("Uploads");

        group.MapGet("", async (HttpContext context, IUserAccessor users, ScmosDbContext db, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var uploads = await db.ReportUploads
                .AsNoTracking()
                .OrderByDescending(upload => upload.UploadedAt)
                .Take(36)
                .Select(upload => new
                {
                    id = upload.Id,
                    period = upload.Period,
                    filename = upload.Filename,
                    row_count = upload.RowCount,
                    issue_count = upload.IssueCount,
                    uploaded_at = upload.UploadedAt,
                })
                .ToListAsync(token);

            return Results.Json(uploads);
        });

        group.MapPost("", async (HttpContext context, IUserAccessor users, ScmosDbContext db, IFileStore files, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            if (!files.Configured)
                return ApiResults.Error(
                    "File storage is unavailable — set Storage:ServiceUri to the Blob endpoint and grant the app's managed identity Storage Blob Data Contributor.",
                    StatusCodes.Status503ServiceUnavailable);

            if (!context.Request.HasFormContentType) return ApiResults.Error("Expected a multipart form", StatusCodes.Status415UnsupportedMediaType);
            var form = await context.Request.ReadFormAsync(token);
            var file = form.Files["file"];
            if (file is null || file.Length == 0) return ApiResults.Error("File is required", StatusCodes.Status400BadRequest);
            if (file.Length > MaxBytes) return ApiResults.Error("File is too large — keep it under 32 MB", StatusCodes.Status413PayloadTooLarge);

            var id = Guid.NewGuid();
            var period = Field(form, "period", "Unspecified");
            var uploadedAt = DateTimeOffset.UtcNow;
            var safeName = SafeName(file.FileName);
            var objectKey = $"monthly/{period.Replace(" ", "-").ToLowerInvariant()}/{id}-{safeName}";

            await using (var stream = file.OpenReadStream())
            {
                await files.PutAsync(objectKey, stream,
                    string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    new Dictionary<string, string> { ["period"] = period, ["originalName"] = safeName },
                    token);
            }

            db.ReportUploads.Add(new ReportUpload
            {
                Id = id,
                Period = period,
                Filename = file.FileName,
                ObjectKey = objectKey,
                RowCount = Number(form, "rows"),
                IssueCount = Number(form, "issues"),
                UploadedAt = uploadedAt,
            });

            var ownerName = Field(form, "owner", "");
            if (ownerName.Length > 0)
            {
                db.OperationUploads.Add(new OperationUpload
                {
                    Id = Guid.NewGuid(),
                    UploadId = id,
                    OwnerName = ownerName,
                    Flow = Field(form, "flow", "Mixed"),
                    SubmittedBy = user.Signature,
                    SubmittedAt = uploadedAt,
                });
            }

            await db.SaveChangesAsync(token);
            return Results.Json(new { id, stored = true, objectKey });
        }).DisableAntiforgery();
    }

    private static string Field(IFormCollection form, string name, string fallback)
    {
        var value = form[name].ToString().Trim();
        return value.Length > 0 ? value : fallback;
    }

    private static int Number(IFormCollection form, string name) =>
        int.TryParse(form[name].ToString(), out var value) && value >= 0 ? value : 0;

    /// <summary>
    /// Blob names accept far more than this, but a key that survives a URL, a
    /// download header and a Windows file system is worth more than a faithful
    /// Thai filename — the original is kept in the row and the blob metadata.
    /// </summary>
    private static string SafeName(string name)
    {
        var cleaned = new string(Path.GetFileName(name)
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_')
            .ToArray());
        return cleaned.Trim('_').Length == 0 ? "upload" : cleaned[..Math.Min(cleaned.Length, 120)];
    }
}
