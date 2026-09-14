using System.Text.Json;
using System.Text.Json.Serialization;
using Scmos.Api.Ai.Operations;
using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Endpoints;

public static class OperationsChangeEndpoints
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Confirmation(string Fingerprint, [property: JsonRequired] bool Approve, string Note);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CommandRequest(string Message);
    public static void MapOperationsChanges(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/ai/operations-changes").WithTags("AI");
        group.MapPost("/interpret", async (HttpContext context, IUserAccessor users, OperationsChangeService service, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!OperationsChangePolicy.CanRequest(user)) return ApiResults.Error("ไม่มีสิทธิ์เสนอแก้งาน", 403);
            var body = await Read<CommandRequest>(context, token);
            if (body is null) return ApiResults.Error("คำสั่งไม่ถูกต้อง", 400);
            try { return Results.Json(await service.InterpretAsync(body.Message, user!, token)); }
            catch (Exception) when (!token.IsCancellationRequested) { return ApiResults.Error("อ่านร่างไม่สำเร็จ", 503); }
        });
        group.MapGet("/attention", async (HttpContext context, IUserAccessor users, OperationsAttentionService attention, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null || Scmos.Api.Ai.AiPermissionPolicy.Scope(user) is null || !user.Can(Capability.ViewDashboard))
                return ApiResults.Error("ไม่มีสิทธิ์อ่านงาน", 403);
            return Results.Json(await attention.ReadAsync(user, token));
        });
        group.MapGet("", async (HttpContext context, IUserAccessor users, OperationsChangeService service, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!OperationsChangePolicy.CanRequest(user)) return ApiResults.Error("ไม่มีสิทธิ์เสนอแก้งาน", 403);
            return Results.Json(new { configured = service.Configured, canApprove = OperationsChangePolicy.CanApprove(user),
                rows = await service.ListAsync(user!, token) });
        });
        group.MapGet("/preview", async (string key, HttpContext context, IUserAccessor users,
            OperationsChangeService service, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!OperationsChangePolicy.CanRequest(user)) return ApiResults.Error("ไม่มีสิทธิ์เสนอแก้งาน", 403);
            try
            {
                var preview = await service.PreviewAsync(key, user!, token);
                return preview is null ? ApiResults.Error("ไม่พบงานในขอบเขตที่แก้ไขได้", 404) : Results.Json(preview);
            }
            catch (Exception) when (!token.IsCancellationRequested) { return ApiResults.Error("อ่านข้อมูลสำหรับยืนยันไม่สำเร็จ", 503); }
        });
        group.MapPost("", async (HttpContext context, IUserAccessor users, OperationsChangeService service, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!OperationsChangePolicy.CanRequest(user)) return ApiResults.Error("ไม่มีสิทธิ์เสนอแก้งาน", 403);
            var body = await Read<OperationsChangeRequest>(context, token);
            if (body is null) return ApiResults.Error("คำขอไม่ถูกต้องหรือเกินขนาด", 400);
            return Reply(await service.ProposeAsync(body, user!, token));
        });
        group.MapPost("/{id:long}/confirm", async (long id, HttpContext context, IUserAccessor users,
            OperationsChangeService service, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!OperationsChangePolicy.CanApprove(user)) return ApiResults.Error("ยืนยันได้เฉพาะ Supervisor ขึ้นไป", 403);
            if (ApiResults.NeedsSecondFactor(users, user!, Capability.ApproveAi) is { } refusal) return refusal;
            var body = await Read<Confirmation>(context, token);
            if (body is null || id <= 0) return ApiResults.Error("คำขอยืนยันไม่ถูกต้อง", 400);
            return Reply(await service.ConfirmAsync(id, body.Fingerprint, body.Approve, body.Note, user!, token));
        });
    }
    private static IResult Reply(OperationsChangeResult result) => Results.Json(result,
        statusCode: result.Code is "pending" or "applied" or "already_applied" or "rejected" ? 200
            : result.Code is "forbidden" or "requester_forbidden" ? 403
            : result.Code is "write_disabled" or "write_unavailable" ? 503 : 409);
    private static async Task<T?> Read<T>(HttpContext context, CancellationToken token) where T : class
    {
        if (!context.Request.HasJsonContentType() || context.Request.Headers["X-SCMOS-AI-Control"] != "1") return null;
        var buffer = new byte[8193];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await context.Request.Body.ReadAsync(buffer.AsMemory(count), token);
            if (read == 0) break;
            count += read;
        }
        if (count > 8192) return null;
        try { return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, count), new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 4 }); }
        catch (JsonException) { return null; }
    }
}
