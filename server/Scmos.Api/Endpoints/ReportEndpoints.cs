using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The Report Centre's data, as one document per report.
///
/// <para>
/// A report is assembled here and not in the browser. That is the same rule the
/// KPI screen follows and it matters more here: this document is what leaves
/// the building as a PDF, and a figure computed in the browser would depend on
/// which build of the front end a viewer happened to have loaded. What the
/// screen receives is finished — every count, every rate, and every base.
/// </para>
///
/// <para>
/// Reading needs <see cref="Capability.ViewDashboard"/> and nothing more. A
/// performance report is the operation's own record of itself; it is not the
/// rate book, and it names no price.
/// </para>
/// </summary>
public static class ReportEndpoints
{
    public static void MapReports(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/reports").WithTags("Reports");

        // What the pickers may offer, with the coverage attached to each name so
        // the screen can warn before a report is generated rather than after.
        group.MapGet("/choices", async (HttpContext context, IUserAccessor users,
            MonthlyReportService reports, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงาน", StatusCodes.Status403Forbidden);

            return Results.Json(await reports.ChoicesAsync(token));
        });

        group.MapGet("/monthly", async (string? customer, string? month,
            HttpContext context, IUserAccessor users, MonthlyReportService reports,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงาน", StatusCodes.Status403Forbidden);

            var wanted = (customer ?? "").Trim();
            if (wanted.Length == 0) return ApiResults.Error("ต้องเลือกลูกค้า", StatusCodes.Status400BadRequest);
            if (wanted.Length > 200)
                return ApiResults.Error("ชื่อลูกค้ายาวเกินไป", StatusCodes.Status400BadRequest);

            // MM/yyyy, as the month is written everywhere else in this system.
            var period = (month ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(period, @"^(0[1-9]|1[0-2])/[0-9]{4}$"))
                return ApiResults.Error("เดือนต้องอยู่ในรูปแบบ MM/yyyy", StatusCodes.Status400BadRequest);

            var report = await reports.BuildAsync(wanted, period, token);

            // The one document shape — the archive stores exactly this, and the
            // same screen renders both.
            return Results.Json(MonthlyReportService.Document(
                report, user.DisplayName, MonthlyReportService.Stamp()));
        });

        /*
         * A drafted management summary, on request and never automatically.
         *
         * A POST rather than a GET because it is not a read: it sends this
         * customer's figures — trip counts, on-time counts, carrier names — to
         * OpenAI. No price and no personal detail travels (ReportCommentary
         * builds the payload, and --check-report asserts that), but it is still
         * data leaving the building, so it happens when somebody presses the
         * button and not a moment before.
         *
         * Guarded on ViewDashboard like the report itself. A person who may
         * read the figures may ask for a paragraph about them.
         */
        group.MapPost("/commentary", async (string? customer, string? month,
            HttpContext context, IUserAccessor users, MonthlyReportService reports,
            ReportWriterService writer, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงาน", StatusCodes.Status403Forbidden);

            var wanted = (customer ?? "").Trim();
            var period = (month ?? "").Trim();
            if (wanted.Length == 0 || wanted.Length > 200)
                return ApiResults.Error("ต้องเลือกลูกค้า", StatusCodes.Status400BadRequest);
            if (!System.Text.RegularExpressions.Regex.IsMatch(period, @"^(0[1-9]|1[0-2])/[0-9]{4}$"))
                return ApiResults.Error("เดือนต้องอยู่ในรูปแบบ MM/yyyy", StatusCodes.Status400BadRequest);

            var report = await reports.BuildAsync(wanted, period, token);
            var result = await writer.DraftAsync(report, token);
            if (result.Text is null)
                return ApiResults.Error(result.Error ?? "ขอบทสรุปไม่สำเร็จ", result.Status);

            // Recorded because it is an outward call carrying a customer's
            // figures. Who asked, for whom, and when — the text itself is not
            // kept, being a draft nobody has agreed to yet.
            await audit.RecordAsync(user, AuditActions.Update, "report-commentary",
                $"{wanted}|{period}", $"บทสรุป AI · {wanted} · {period}",
                "commentary", "", "drafted", "", token);

            return Results.Json(new { text = result.Text });
        });

        /* ------------------------------------------------------------ archive */

        // What was taken, without the documents. The list is read far more often
        // than any single report is opened.
        group.MapGet("/archive", async (string? month, HttpContext context, IUserAccessor users,
            ReportArchiveService archive, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงาน", StatusCodes.Status403Forbidden);

            return Results.Json(await archive.ListAsync(month, token));
        });

        /*
         * One stored report, exactly as it was answered on the day.
         *
         * Returned as the stored JSON rather than rebuilt, which is the whole
         * point of the archive: arrival times are keyed in late, so the same
         * month measured again reads differently, and the figure a customer is
         * holding is the one that has to be recoverable.
         */
        group.MapGet("/archive/{id:long}", async (long id, HttpContext context, IUserAccessor users,
            ReportArchiveService archive, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงาน", StatusCodes.Status403Forbidden);

            var document = await archive.ReadAsync(id, token);
            if (document is null) return ApiResults.Error("ไม่พบรายงานที่เก็บไว้", StatusCodes.Status404NotFound);
            return Results.Text(document, "application/json");
        });

        /*
         * Take a snapshot now, rather than waiting for the month to turn.
         *
         * Refuses to overwrite one that exists — see ReportArchiveService.TakeAsync.
         * Needs EditOwnJobs rather than only ViewDashboard: reading a report is
         * for anybody, but writing a row that will be quoted back later is an
         * act, and the trail records who performed it.
         */
        group.MapPost("/archive", async (string? customer, string? month,
            HttpContext context, IUserAccessor users, ReportArchiveService archive,
            AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditOwnJobs))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์เก็บรายงาน", StatusCodes.Status403Forbidden);

            var wanted = (customer ?? "").Trim();
            var period = (month ?? "").Trim();
            if (wanted.Length == 0 || wanted.Length > 200)
                return ApiResults.Error("ต้องเลือกลูกค้า", StatusCodes.Status400BadRequest);
            if (!System.Text.RegularExpressions.Regex.IsMatch(period, @"^(0[1-9]|1[0-2])/[0-9]{4}$"))
                return ApiResults.Error("เดือนต้องอยู่ในรูปแบบ MM/yyyy", StatusCodes.Status400BadRequest);

            var took = await archive.TakeAsync(wanted, period, user.Signature, token);
            if (took)
            {
                await audit.RecordAsync(user, AuditActions.Register, "report-archive",
                    $"{wanted}|{period}", $"เก็บรายงาน · {wanted} · {period}",
                    "archive", "", "taken", "", token);
            }

            return Results.Json(new
            {
                stored = took,
                message = took
                    ? $"เก็บรายงาน {wanted} · {period} แล้ว"
                    : $"{wanted} · {period} ถูกเก็บไว้แล้วก่อนหน้านี้ — ของเดิมไม่ถูกเขียนทับ",
            });
        });
    }
}
