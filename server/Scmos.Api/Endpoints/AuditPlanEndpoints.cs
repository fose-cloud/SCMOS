using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Audit Planning and a new subcontractor's onboarding (1 Oct 2026). Reading needs a department sign-in — a
/// carrier never reaches these (<see cref="CarrierBoundary"/>); writing needs EditSuppliers, the right the
/// supplier register is kept with.
/// </summary>
public static class AuditPlanEndpoints
{
    public record ImportBody(IReadOnlyList<AuditItemInput>? Rows);
    public record OnboardingBody(string? Status, string? Remark);
    public record VendorAuditBody(string? Date, string? PersonInCharge, string? Target);

    public static void MapAuditPlan(this IEndpointRouteBuilder routes)
    {
        var plan = routes.MapGroup("/api/audit-plan").WithTags("Audit Planning");

        plan.MapGet("", async (int? year, HttpContext context, IUserAccessor users, AuditPlanService plans,
            CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await plans.ReadAsync(year ?? Formats.Now.Year, token));
        });

        plan.MapPut("/{year:int}", async (int year, [FromBody] AuditPlanHeaderInput body, HttpContext context,
            IUserAccessor users, AuditPlanService plans, CancellationToken token) =>
            await WriteAsync(context, users, user => plans.SaveHeaderAsync(user, year, body, token)));

        plan.MapPost("/items", async ([FromBody] AuditItemInput body, HttpContext context, IUserAccessor users,
            AuditPlanService plans, CancellationToken token) =>
            await WriteAsync(context, users, user => plans.AddItemAsync(user, body, token)));

        plan.MapPut("/items/{id:long}", async (long id, [FromBody] AuditItemInput body, HttpContext context,
            IUserAccessor users, AuditPlanService plans, CancellationToken token) =>
            await WriteAsync(context, users, user => plans.UpdateItemAsync(user, id, body, token)));

        plan.MapDelete("/items/{id:long}", async (long id, HttpContext context, IUserAccessor users,
            AuditPlanService plans, CancellationToken token) =>
            await WriteAsync(context, users, user => plans.DeleteItemAsync(user, id, token)));

        plan.MapPost("/import", async ([FromBody] ImportBody body, HttpContext context, IUserAccessor users,
            AuditPlanService plans, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditSuppliers))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขแผน Audit", StatusCodes.Status403Forbidden);
            var rows = body.Rows ?? [];
            if (rows.Count == 0) return ApiResults.Error("ไม่มีรายการสำหรับนำเข้า", StatusCodes.Status400BadRequest);
            if (rows.Count > 500) return ApiResults.Error("นำเข้าได้ครั้งละไม่เกิน 500 รายการ", StatusCodes.Status400BadRequest);
            var (saved, skipped, errors) = await plans.ImportAsync(user, rows, token);
            return Results.Json(new
            {
                message = $"นำเข้า {saved} รายการ" + (skipped > 0 ? $" · ข้ามที่มีอยู่แล้ว {skipped}" : "")
                    + (errors.Count > 0 ? $" · ไม่สำเร็จ {errors.Count}" : ""),
                saved, skipped, errors,
            });
        });

        var vendor = routes.MapGroup("/api/suppliers/{id:int}").WithTags("Suppliers");

        vendor.MapGet("/onboarding", async (int id, HttpContext context, IUserAccessor users, AuditPlanService plans,
            CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var view = await plans.OnboardingAsync(id, token);
            return view is null ? ApiResults.Error("ไม่พบผู้ขนส่งรายนี้", StatusCodes.Status404NotFound) : Results.Json(view);
        });

        vendor.MapPut("/onboarding/{code}", async (int id, string code, [FromBody] OnboardingBody body, HttpContext context,
            IUserAccessor users, AuditPlanService plans, CancellationToken token) =>
            await WriteAsync(context, users, user => plans.SetOnboardingAsync(user, id, code, body.Status, body.Remark, token)));

        vendor.MapPut("/audit-date", async (int id, [FromBody] VendorAuditBody body, HttpContext context,
            IUserAccessor users, AuditPlanService plans, CancellationToken token) =>
            await WriteAsync(context, users, user => plans.SetVendorAuditAsync(user, id, body.Date, body.PersonInCharge, body.Target, token)));
    }

    private static async Task<IResult> WriteAsync(HttpContext context, IUserAccessor users, Func<AppUser, Task<AuditPlanResult>> work)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        if (!user.Can(Capability.EditSuppliers))
            return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขแผน Audit หรือเอกสารผู้ขนส่ง", StatusCodes.Status403Forbidden);
        var result = await work(user);
        return result.Ok ? Results.Json(new { message = result.Message, id = result.Id }) : ApiResults.Error(result.Message, result.Status);
    }
}
