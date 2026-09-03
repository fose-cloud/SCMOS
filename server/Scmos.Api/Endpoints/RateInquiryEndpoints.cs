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
/// <summary>A block of cells, as a dragged rectangle sends them.</summary>
public record CellsBody(List<CellEdit>? Edits);

/// <summary>One cell inside such a block.</summary>
public record CellEdit(long LaneId, string? Field, string? Value);

/// <summary>One cell of the rate sheet: which field, and what it becomes.</summary>
public record CellBody(string? Field, string? Value);

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

        /*
         * The register laid out as the workbook lays it out — one row per lane,
         * the request's own fields repeated down its rows.
         *
         * Paged here rather than in the browser: three thousand lanes with
         * twenty-eight price columns is a quarter of a million cells.
         */
        group.MapGet("/sheet", async (string? q, string? customer, string? month,
            int? page, int? per, HttpContext context, IUserAccessor users,
            RateInquiryService inquiries, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await inquiries.SheetAsync(
                q ?? "", customer ?? "", month ?? "", page ?? 1, per ?? 50, token));
        });

        // One cell, because that is how a grid is used — and because a
        // whole-row save would overwrite whatever somebody else changed in the
        // same second.
        group.MapPost("/sheet/{laneId:long}", async (long laneId, [FromBody] CellBody body,
            HttpContext context, IUserAccessor users, RateInquiryService inquiries,
            AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง",
                    StatusCodes.Status403Forbidden);

            var result = await inquiries.SaveCellAsync(laneId, body.Field ?? "", body.Value ?? "", token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            // A rate that moves is a rate somebody will ask about later.
            await audit.RecordAsync(user, AuditActions.Update, "rate-inquiry",
                result.Id.ToString(), result.Message, body.Field ?? "", "", body.Value ?? "", "", token);
            return Results.Json(new { message = result.Message });
        });

        // A block of cells at once — what a paste or a Delete over a dragged
        // rectangle sends. One request and one transaction rather than one of
        // each per cell.
        group.MapPost("/sheet/cells", async ([FromBody] CellsBody body, HttpContext context,
            IUserAccessor users, RateInquiryService inquiries, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง",
                    StatusCodes.Status403Forbidden);

            var edits = (body.Edits ?? [])
                .Select(one => (one.LaneId, one.Field ?? "", one.Value ?? ""))
                .ToList();
            if (edits.Count == 0) return Results.Json(new { saved = 0, refused = Array.Empty<string>() });
            if (edits.Count > 500)
                return ApiResults.Error("แก้ได้ครั้งละไม่เกิน 500 ช่อง", StatusCodes.Status413PayloadTooLarge);

            var (saved, refused) = await inquiries.SaveCellsAsync(edits, token);

            // One audit row for the block, naming how many moved. A row per cell
            // would bury the day's real changes under a paste.
            if (saved > 0)
            {
                await audit.RecordAsync(user, AuditActions.Update, "rate-inquiry", "sheet",
                    $"แก้ {saved} ช่อง", "หลายช่อง", "", $"{saved} ช่อง", "", token);
            }
            return Results.Json(new { saved, refused = refused.Take(10) });
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

        // Many at once, for the workbook the team has kept since August 2025.
        // Sent in batches by the browser rather than as one three-megabyte body,
        // so a slow link fails a batch instead of the whole import — and so the
        // screen can say how far it has got.
        //
        // Each one goes through CreateAsync, the same path a typed inquiry
        // takes: the vehicle codes, the dates and the lane rules are checked
        // once, here, and not a second time in the importer where they would
        // drift. A refusal is reported with its row rather than aborting the
        // batch, because one bad lane in 1975 is not a reason to lose 1974.
        group.MapPost("/import", async ([FromBody] List<RateInquiryService.InquiryPost> body,
            HttpContext context, IUserAccessor users, RateInquiryService inquiries,
            AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์นำเข้าใบขอราคา", StatusCodes.Status403Forbidden);

            var added = 0;
            var refused = new List<string>();
            foreach (var post in body ?? [])
            {
                var result = await inquiries.CreateAsync(user, post, token);
                if (result.Ok) added++;
                else refused.Add($"{post.Customer} {post.InquiredOn}: {result.Message}");
            }

            if (added > 0)
            {
                await audit.RecordAsync(user, AuditActions.Register, "rate-inquiry", "import",
                    "นำเข้าใบขอราคาจากไฟล์", "import", "",
                    $"{added} ใบ" + (refused.Count > 0 ? $" · ปฏิเสธ {refused.Count}" : ""), "", token);
            }

            return Results.Json(new { added, refused = refused.Take(20).ToList(), refusedTotal = refused.Count });
        });
    }
}
