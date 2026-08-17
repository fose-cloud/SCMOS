using Scmos.Api.Auth;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class AiExtractEndpoints
{
    private const int MaxFiles = 4;
    private const long MaxBytes = 12L * 1024 * 1024;

    public static void MapAiExtract(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/ai-extract", async (HttpContext context, IUserAccessor users,
            IDocumentExtractor extractor, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            if (!context.Request.HasFormContentType)
                return ApiResults.Error("Expected a multipart form", StatusCodes.Status415UnsupportedMediaType);

            var form = await context.Request.ReadFormAsync(token);
            var category = (form["category"].ToString() is { Length: > 0 } value ? value : "IMPORT").ToUpperInvariant();

            var uploaded = form.Files.Where(file => file.Length > 0).Take(MaxFiles).ToList();
            if (uploaded.Count == 0) return ApiResults.Error("Attach at least one file", StatusCodes.Status400BadRequest);

            if (uploaded.Sum(file => file.Length) > MaxBytes)
                return ApiResults.Error("Files are too large — keep the batch under 12 MB", StatusCodes.Status413PayloadTooLarge);

            var parts = new List<DocumentPart>(uploaded.Count);
            foreach (var file in uploaded)
            {
                await using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, token);
                parts.Add(new DocumentPart(file.FileName, file.ContentType ?? "", BinaryData.FromBytes(buffer.ToArray())));
            }

            var result = await extractor.ReadAsync(category, parts, token);
            return result.Fields is null
                ? ApiResults.Error(result.Error ?? "Could not read the file.", result.Status)
                : Results.Json(new { fields = result.Fields });
        }).DisableAntiforgery().WithTags("AI");
    }
}
