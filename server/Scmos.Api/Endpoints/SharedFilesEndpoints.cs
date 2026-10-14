using Microsoft.AspNetCore.Http.Features;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The shared file space (14 Oct 2026, its own menu) — see <see cref="SharedFilesService"/>. Not on the carrier
/// allow-list (<see cref="CarrierBoundary"/>), so a carrier's account is refused before it gets here; the service
/// refuses it again on its own.
/// </summary>
public static class SharedFilesEndpoints
{
    public sealed record FolderBody(string? Name, long? ParentId);
    public sealed record NameBody(string? Name);
    public sealed record RestoreBody(string? Kind, long Id);

    public static void MapSharedFiles(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/shared-files").WithTags("Shared files");

        group.MapGet("", async (long? folder, HttpContext context, IUserAccessor users, SharedFilesService service,
            CancellationToken token) => await Run(context, users, user => service.ListAsync(user, folder, token)));

        group.MapGet("/trash", async (HttpContext context, IUserAccessor users, SharedFilesService service,
            CancellationToken token) => await Run(context, users, user => service.TrashAsync(user, token)));

        group.MapPost("/folders", async (FolderBody body, HttpContext context, IUserAccessor users, SharedFilesService service,
            CancellationToken token) => await Run(context, users, user => service.CreateFolderAsync(user, body.ParentId, body.Name, token)));

        group.MapPost("/folders/{id:long}/rename", async (long id, NameBody body, HttpContext context, IUserAccessor users,
            SharedFilesService service, CancellationToken token) =>
            await Run(context, users, user => service.RenameFolderAsync(user, id, body.Name, token)));

        group.MapPost("/folders/{id:long}/trash", async (long id, HttpContext context, IUserAccessor users,
            SharedFilesService service, CancellationToken token) =>
            await Run(context, users, user => service.TrashFolderAsync(user, id, token)));

        group.MapPost("/upload", async (HttpContext context, IUserAccessor users, SharedFilesService service,
            CancellationToken token) => await Run(context, users, async user =>
        {
            if (!context.Request.HasFormContentType)
                return new SharedResult(false, StatusCodes.Status415UnsupportedMediaType, "ต้องส่งเป็น multipart form");
            // Kestrel's own ceiling is 30,000,000 bytes, under the 32 MB the space allows; raised for this one route,
            // to the file plus room for the form around it.
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = SharedFiles.MaxBytes + 1024 * 1024;
            var form = await context.Request.ReadFormAsync(token);
            var folderText = form["folderId"].ToString().Trim();
            long? folderId = long.TryParse(folderText, out var parsed) && parsed > 0 ? parsed : null;
            if (folderText.Length > 0 && folderId is null)
                return new SharedResult(false, StatusCodes.Status400BadRequest, "folderId ไม่ถูกต้อง");
            return await service.UploadAsync(user, folderId, form.Files["file"], token);
        }));

        group.MapPost("/{id:long}/rename", async (long id, NameBody body, HttpContext context, IUserAccessor users,
            SharedFilesService service, CancellationToken token) =>
            await Run(context, users, user => service.RenameFileAsync(user, id, body.Name, token)));

        group.MapPost("/{id:long}/trash", async (long id, HttpContext context, IUserAccessor users,
            SharedFilesService service, CancellationToken token) =>
            await Run(context, users, user => service.TrashFileAsync(user, id, token)));

        group.MapPost("/trash/restore", async (RestoreBody body, HttpContext context, IUserAccessor users,
            SharedFilesService service, CancellationToken token) =>
            await Run(context, users, user => service.RestoreAsync(user, body.Kind, body.Id, token)));

        group.MapGet("/{id:long}/content", async (long id, string? inline, HttpContext context, IUserAccessor users,
            SharedFilesService service, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!SharedFilesService.CanSee(user)) return ApiResults.Error("เมนูนี้สำหรับบัญชีของพนักงาน", StatusCodes.Status403Forbidden);
            var (file, content, refusal) = await service.OpenAsync(user, id, token);
            if (refusal is not null) return ApiResults.Error(refusal.Message, refusal.Status);

            // As the document register serves files: never sniffed, and shown inline only as a type chosen from the
            // name, under a policy that runs nothing.
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.CacheControl = "private, no-store";
            var shown = inline is "1" or "true" ? InlineViewing.TypeFor(file!.Name) : null;
            if (shown is not null)
            {
                context.Response.Headers["Content-Security-Policy"] = InlineViewing.Policy;
                return Results.File(content!, shown, enableRangeProcessing: true);
            }
            return Results.File(content!, file!.ContentType, file.Name, enableRangeProcessing: true);
        });
    }

    /// <summary>Signed in, a staff account, then the action — and its answer as JSON with the words to show.</summary>
    private static async Task<IResult> Run(HttpContext context, IUserAccessor users, Func<AppUser, Task<SharedResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        if (!SharedFilesService.CanSee(user)) return ApiResults.Error("เมนูนี้สำหรับบัญชีของพนักงาน", StatusCodes.Status403Forbidden);
        var result = await action(user);
        if (!result.Ok) return ApiResults.Error(result.Message, result.Status);
        return result.Body is null ? Results.Json(new { message = result.Message }) : Results.Json(result.Body, statusCode: result.Status);
    }
}
