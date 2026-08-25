using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// A customer's own price card, and the cargo receipt they sign for.
///
/// The card is gated exactly like the subcontractor book — <see
/// cref="Capability.ViewRates"/> to read, <see cref="Capability.EditRates"/> to
/// change — because it is the same kind of secret. It is a negotiated price, it
/// is what an invoice gets checked against, and a Subcontractor account has no
/// business reading what this customer pays anybody.
///
/// The form templates are not secret in that way: they are the column headings
/// on a document the customer already receives. Reading them needs nothing more
/// than being signed in; replacing the set needs the right to upload documents,
/// which is the nearest thing to "you are allowed to change what the paperwork
/// looks like" that the role table has.
/// </summary>
public static class CustomerDocumentEndpoints
{
    public static void MapCustomerDocuments(this IEndpointRouteBuilder routes)
    {
        var rates = routes.MapGroup("/api/customer-rates").WithTags("CustomerRates");

        rates.MapGet("", async (string? customer, HttpContext context, IUserAccessor users,
            CustomerDocumentService service, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูตารางราคา", StatusCodes.Status403Forbidden);

            var wanted = (customer ?? "").Trim();
            if (wanted.Length == 0)
                return Results.Json(await service.CustomersWithCardsAsync(token));

            return Results.Json(await service.ReadCardAsync(wanted, token));
        });

        rates.MapPut("", async ([FromBody] CustomerCardInput body, HttpContext context,
            IUserAccessor users, CustomerDocumentService service, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ตารางราคา", StatusCodes.Status403Forbidden);

            if (body.Lanes.Count == 0)
                return ApiResults.Error("ไม่มีเส้นทางในการ์ดราคานี้", StatusCodes.Status400BadRequest);

            var saved = await service.SaveCardAsync(body, token);

            // Written down because it is a rate change, and a rate change is
            // one of the things somebody has to be able to point at afterwards.
            await audit.RecordAsync(user, "save", "customer-rate", body.Customer,
                $"การ์ดราคา {body.Customer} · {body.Carrier}",
                "", "", $"{body.Lanes.Count} เส้นทาง · {saved} ราคา", "", token);

            return Results.Json(new { lanes = body.Lanes.Count, prices = saved });
        });

        var forms = routes.MapGroup("/api/cargo-forms").WithTags("CargoForms");

        forms.MapGet("", async (HttpContext context, IUserAccessor users,
            CustomerDocumentService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await service.ReadTemplatesAsync(token));
        });

        forms.MapPut("", async ([FromBody] List<CargoTemplateInput> body, HttpContext context,
            IUserAccessor users, CustomerDocumentService service, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.UploadDocuments))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้แบบฟอร์ม", StatusCodes.Status403Forbidden);

            var saved = await service.SaveTemplatesAsync(body, token);
            if (saved == 0)
                return ApiResults.Error("ไม่มีแบบฟอร์มที่อ่านได้ในชุดนี้", StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, "save", "cargo-form", "all",
                "แบบฟอร์มใบรับ-ส่งสินค้า", "", "", $"{saved} ลูกค้า", "", token);

            return Results.Json(new { customers = saved });
        });
    }
}
