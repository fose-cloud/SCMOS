using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Asking carriers what a journey costs.
///
/// Gated on <see cref="Capability.ViewRates"/> throughout, which Operation User
/// and above carry: raising an inquiry is part of running a job, and the figures
/// that come back are the same commercial-in-confidence numbers the rate book
/// holds. A Viewer sees neither.
/// </summary>
public static class RateInquiryEndpoints
{
    public static void MapRateInquiries(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/rate-inquiries").WithTags("RateInquiry");

        // What the form is allowed to offer. Served rather than duplicated in
        // the browser, so a vehicle on the form is a vehicle the POST accepts.
        group.MapGet("/form", async (HttpContext context, IUserAccessor users,
            RateInquiryService inquiries, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูข้อมูลราคา", StatusCodes.Status403Forbidden);

            return Results.Json(await inquiries.FormAsync(token));
        });

        group.MapGet("", async (bool? mine, string? customer, int? take, HttpContext context,
            IUserAccessor users, RateInquiryService inquiries, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูข้อมูลราคา", StatusCodes.Status403Forbidden);

            var rows = await inquiries.ListAsync(
                mine == true ? user.OperatorId : null, customer, take ?? 50, token);

            return Results.Json(new { inquiries = rows, you = user.OperatorId });
        });

        group.MapPost("", async ([FromBody] RateInquiryService.InquiryPost body, HttpContext context,
            IUserAccessor users, RateInquiryService inquiries, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์บันทึกใบขอราคา", StatusCodes.Status403Forbidden);

            var result = await inquiries.CreateAsync(user, body, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Register, "rate-inquiry",
                result.Id.ToString(), body.Customer ?? "", "inquiry", "",
                $"#{result.Number} · {(body.Lanes?.Count ?? 0)} เส้นทาง", "", token);

            return Results.Json(new { message = result.Message, id = result.Id, number = result.Number });
        });
    }
}
