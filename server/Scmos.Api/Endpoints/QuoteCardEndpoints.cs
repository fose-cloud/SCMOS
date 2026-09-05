using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public record VehicleRateBody(int PerKm, int BaseCharge, decimal Chill, int DangerousGoods);
public record ExtraBody(int Id, string? Label, string? Basis, decimal Rate, bool Active);
public record MarginBody(decimal Percent);

/// <summary>
/// The card a journey is priced from.
///
/// Reading it needs only a signed-in account — anybody quoting has to see what
/// they are quoting from. Changing it needs the same permission as changing a
/// rate anywhere else, because that is what it is: one edit here moves the price
/// of every journey quoted afterwards.
/// </summary>
public static class QuoteCardEndpoints
{
    public static void MapQuoteCard(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/quote-card").WithTags("QuoteCard");

        group.MapPost("/save-to-sheet", async ([FromBody] QuoteSaveBody body,
            HttpContext context, IUserAccessor users, QuoteSheetService sheet, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์บันทึกตารางอัตรา", StatusCodes.Status403Forbidden);
            var result = await sheet.SaveAsync(user, body, token);
            if (result.Status != 200) return ApiResults.Error(result.Message, result.Status);
            var saved = result.Receipt!;
            return Results.Json(new { saved.Id, saved.Number, saved.Date, saved.Count, saved.RouteCount,
                result.Message, result.Replayed });
        });

        group.MapGet("", async (HttpContext context, IUserAccessor users,
            QuoteCardService card, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(new
            {
                card = await card.ReadAsync(token),
                // The vocabulary travels with the card so the screen offers the
                // four ways a charge can apply rather than keeping its own list
                // of them, which would be the same rule written twice.
                bases = QuoteBasis.All.Select(one => new { id = one, label = QuoteBasis.Thai[one] }),
            });
        });

        group.MapPost("/vehicle/{id:int}", async (int id, [FromBody] VehicleRateBody body,
            HttpContext context, IUserAccessor users, QuoteCardService card,
            AuditService audit, CancellationToken token) =>
            await Guarded(context, users, audit, token, "อัตราต่อระยะทาง",
                () => card.SaveVehicleAsync(id, body.PerKm, body.BaseCharge,
                    body.Chill, body.DangerousGoods, token),
                $"{body.PerKm}/กม. + {body.BaseCharge} × {body.Chill} · DG {body.DangerousGoods}"));

        group.MapPost("/extra", async ([FromBody] ExtraBody body, HttpContext context,
            IUserAccessor users, QuoteCardService card, AuditService audit,
            CancellationToken token) =>
            await Guarded(context, users, audit, token, "รายการเพิ่มเติม",
                () => card.SaveExtraAsync(body.Id, body.Label ?? "", body.Basis ?? "",
                    body.Rate, body.Active, token),
                $"{body.Label} · {body.Basis} · {body.Rate}"));

        group.MapPost("/margin", async ([FromBody] MarginBody body, HttpContext context,
            IUserAccessor users, QuoteCardService card, AuditService audit,
            CancellationToken token) =>
            await Guarded(context, users, audit, token, "กำไร",
                () => card.SetMarginAsync(body.Percent, users.Current(context)?.Signature ?? "", token),
                $"{body.Percent}%"));
    }

    private static async Task<IResult> Guarded(HttpContext context, IUserAccessor users,
        AuditService audit, CancellationToken token, string field,
        Func<Task<QuoteCardResult>> action, string newValue)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        if (!user.Can(Capability.EditRates))
            return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง",
                StatusCodes.Status403Forbidden);

        var result = await action();
        if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

        // Every rate that moves is recorded with what it moved to. A quotation
        // somebody queries in six months is answerable only if this row exists.
        await audit.RecordAsync(user, AuditActions.Update, "quote-card", field,
            result.Message, field, "", newValue, "", token);
        return Results.Json(new { message = result.Message });
    }
}
