using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Capacity Planning's fleet tables (30 Sep 2026): each carrier's registered heads, tails and drivers, with the
/// papers each carries.
///
/// The carrier's routes sit under <c>/api/carrier/fleet</c> and answer for the account's company alone — it
/// registers its own trucks and drivers, uploads their papers and retires them. The department's
/// <c>/api/fleet</c> reads every carrier's and writes nothing; <see cref="CarrierBoundary"/> keeps carriers out
/// of it.
/// </summary>
public static class FleetEndpoints
{
    public record ActiveBody(bool Active);

    public static void MapFleet(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/fleet", async (int? supplierId, HttpContext context, IUserAccessor users,
            FleetService fleet, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await fleet.ReadAsync(supplierId, token));
        }).WithTags("Capacity");

        var carrier = routes.MapGroup("/api/carrier/fleet").WithTags("Carrier");

        carrier.MapGet("", async (HttpContext context, IUserAccessor users, CarrierService carriers,
            FleetService fleet, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var company = await carriers.CompanyOfAsync(user, token);
            if (company is null) return NotACarrier();
            return Results.Json(await fleet.ReadAsync(company.Id, token));
        });

        carrier.MapPost("/trucks", async (HttpContext context, IUserAccessor users, CarrierService carriers,
            FleetService fleet, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var company = await carriers.CompanyOfAsync(user, token);
            if (company is null) return NotACarrier();
            if (!context.Request.HasFormContentType)
                return ApiResults.Error("ต้องส่งเป็น multipart form", StatusCodes.Status415UnsupportedMediaType);
            var form = await context.Request.ReadFormAsync(token);
            var result = await fleet.AddTruckAsync(user, company,
                new TruckInput(Text(form, "plate"), Text(form, "kind"), Text(form, "vehicleType"),
                    Text(form, "dgCapable") is "true" or "1" or "on"),
                PapersOf(form, FleetDocuments.Truck), token);
            return Answer(result);
        }).DisableAntiforgery();

        carrier.MapPost("/drivers", async (HttpContext context, IUserAccessor users, CarrierService carriers,
            FleetService fleet, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var company = await carriers.CompanyOfAsync(user, token);
            if (company is null) return NotACarrier();
            if (!context.Request.HasFormContentType)
                return ApiResults.Error("ต้องส่งเป็น multipart form", StatusCodes.Status415UnsupportedMediaType);
            var form = await context.Request.ReadFormAsync(token);
            var result = await fleet.AddDriverAsync(user, company,
                new DriverInput(Text(form, "name"), Text(form, "phone"), Text(form, "licenceNo")),
                PapersOf(form, FleetDocuments.Driver), token);
            return Answer(result);
        }).DisableAntiforgery();

        // A newer file for one paper: form fields `code`, `file` and, for a dated paper, `expiryDate`.
        carrier.MapPost("/{owner}/{id:int}/documents", async (string owner, int id, HttpContext context,
            IUserAccessor users, CarrierService carriers, FleetService fleet, CancellationToken token) =>
        {
            if (owner is not ("trucks" or "drivers")) return Results.NotFound();
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var company = await carriers.CompanyOfAsync(user, token);
            if (company is null) return NotACarrier();
            if (!context.Request.HasFormContentType)
                return ApiResults.Error("ต้องส่งเป็น multipart form", StatusCodes.Status415UnsupportedMediaType);
            var form = await context.Request.ReadFormAsync(token);
            var result = await fleet.ReplacePaperAsync(user, company, owner == "trucks", id,
                new PaperInput(Text(form, "code"), form.Files["file"], Text(form, "expiryDate")), token);
            return Answer(result);
        }).DisableAntiforgery();

        carrier.MapPost("/{owner}/{id:int}/active", async (string owner, int id, [FromBody] ActiveBody body,
            HttpContext context, IUserAccessor users, CarrierService carriers, FleetService fleet,
            CancellationToken token) =>
        {
            if (owner is not ("trucks" or "drivers")) return Results.NotFound();
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var company = await carriers.CompanyOfAsync(user, token);
            if (company is null) return NotACarrier();
            return Answer(await fleet.SetActiveAsync(user, company, owner == "trucks", id, body.Active, token));
        });
    }

    /// <summary>Each required paper's file arrives in the form field named by its code, its date in `{code}-expiry`.</summary>
    private static List<PaperInput> PapersOf(IFormCollection form, IEnumerable<FleetDocuments.Requirement> required) =>
        required.Select(need => new PaperInput(need.Code, form.Files[need.Code], Text(form, need.Code + "-expiry"))).ToList();

    private static IResult Answer(FleetResult result) =>
        result.Ok ? Results.Json(new { message = result.Message, id = result.Id })
            : ApiResults.Error(result.Message, result.Status);

    private static IResult NotACarrier() => ApiResults.Error(
        "บัญชีนี้ไม่ใช่บัญชีผู้รับเหมา หรือยังไม่ได้ผูกกับบริษัท — ให้ผู้ดูแลระบบตั้งค่าให้ก่อน",
        StatusCodes.Status403Forbidden);

    private static string Text(IFormCollection form, string name) => form[name].ToString().Trim();
}
