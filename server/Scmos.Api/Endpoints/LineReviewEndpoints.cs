using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The queue an operator works, and the mapping that makes it work at all.
///
/// <para>
/// Separate from the webhook. That endpoint is open to the internet and
/// authenticated by a signature; these are behind the ordinary sign-in and ask
/// for a capability. Putting them in one file would put one <c>MapGroup</c>
/// around two completely different trust levels.
/// </para>
///
/// <para>
/// <b>Nothing the worker decided is trusted here.</b> The queue can sit for
/// hours, and in that time the job it points at can be delivered by somebody
/// else, cancelled, or reassigned to another haulier. So the apply endpoint
/// works the decision out again from the register as it stands now, and applies
/// only what it decides for itself. The stored verdict is for showing an
/// operator what to look at; it is never the authority for a write.
/// </para>
/// </summary>
public static class LineReviewEndpoints
{
    /// <summary>
    /// Hung off the webhook's own group so the route prefix stays in one place
    /// and nothing new has to be mapped in Program.
    /// </summary>
    public static void MapLineReview(this RouteGroupBuilder group)
    {
        /* ------------------------------------------------------ the queue */

        group.MapGet("/events", async (string? status, int? limit, HttpContext context,
            IUserAccessor users, ScmosDbContext db, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            // Default to what a person is meant to act on. Everything else is
            // reachable by asking for it, which is what the screen's filter does.
            var wanted = string.IsNullOrWhiteSpace(status) ? LineProcessing.NeedReview : status.Trim();
            var take = Math.Clamp(limit ?? 100, 1, 500);

            var rows = await db.LineEvents.AsNoTracking()
                .Where(one => wanted == "ALL" || one.ProcessingStatus == wanted)
                .OrderByDescending(one => one.ReceivedAt)
                .Take(take)
                .Select(one => new
                {
                    one.Id,
                    one.ReceivedAt,
                    one.LineGroupId,
                    one.RawText,
                    one.JobNumber,
                    one.ParsedStatus,
                    one.Confidence,
                    one.ProcessingStatus,
                    one.ErrorCode,
                    one.ErrorMessage,
                    one.JobKey,
                    one.RetryCount,
                })
                .ToListAsync(token);

            // The room's name, resolved in one query rather than per row.
            var ids = rows.Select(one => one.LineGroupId).Distinct().ToList();
            var names = await db.LineGroups.AsNoTracking()
                .Where(one => ids.Contains(one.LineGroupId))
                .ToDictionaryAsync(one => one.LineGroupId, one => one.GroupName, token);

            return Results.Json(new
            {
                events = rows.Select(one => new
                {
                    one.Id, one.ReceivedAt, one.RawText, one.JobNumber, one.ParsedStatus,
                    one.Confidence, one.ProcessingStatus, one.ErrorCode, one.ErrorMessage,
                    one.JobKey, one.RetryCount,
                    group = names.GetValueOrDefault(one.LineGroupId, ""),
                }),
                count = rows.Count,
            });
        });

        /* --------------------------------------------------- the mapping */

        group.MapGet("/groups", async (HttpContext context, IUserAccessor users,
            ScmosDbContext db, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var groups = await db.LineGroups.AsNoTracking()
                .OrderBy(one => one.GroupName)
                .ToListAsync(token);

            var supplierIds = groups.Select(one => (int)one.SupplierId).Distinct().ToList();
            var suppliers = await db.Suppliers.AsNoTracking()
                .Where(one => supplierIds.Contains(one.Id))
                .ToDictionaryAsync(one => one.Id, one => one.Name, token);

            return Results.Json(new
            {
                groups = groups.Select(one => new
                {
                    one.Id, one.LineGroupId, one.GroupName, one.SupplierId,
                    one.GroupType, one.IsActive,
                    // Empty when the supplier row has gone. The screen shows the
                    // gap rather than the id, because a mapping pointing at
                    // nothing is the thing somebody needs to fix.
                    supplier = suppliers.GetValueOrDefault((int)one.SupplierId, ""),
                }),
            });
        });

        group.MapPost("/groups", async ([FromBody] GroupBody body, HttpContext context,
            IUserAccessor users, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("ทำได้เฉพาะระดับหัวหน้างานขึ้นไป", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.ManageSuppliers) is { } stop) return stop;

            var lineGroupId = (body.LineGroupId ?? "").Trim();
            if (lineGroupId.Length == 0)
                return ApiResults.Error("ต้องระบุรหัสกลุ่ม LINE", StatusCodes.Status400BadRequest);

            var type = (body.GroupType ?? LineGroupType.Vendor).Trim().ToUpperInvariant();
            if (type is not (LineGroupType.Vendor or LineGroupType.Internal
                or LineGroupType.Customer or LineGroupType.Other))
                return ApiResults.Error("ประเภทกลุ่มไม่ถูกต้อง", StatusCodes.Status400BadRequest);

            // A mapping to a supplier that does not exist is how a room ends up
            // able to speak for nobody, which reads as a refusal nobody can
            // explain. Refuse it here, where the message can say why.
            var supplier = await db.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(one => one.Id == body.SupplierId, token);
            if (supplier is null)
                return ApiResults.Error("ไม่พบผู้ขนส่งรายนี้", StatusCodes.Status400BadRequest);

            var now = DateTimeOffset.UtcNow;
            var row = await db.LineGroups.FirstOrDefaultAsync(one => one.LineGroupId == lineGroupId, token);
            var before = row is null ? "" : $"{row.SupplierId}/{row.GroupType}/{(row.IsActive ? "on" : "off")}";

            if (row is null)
            {
                row = new LineGroup { LineGroupId = lineGroupId, CreatedAt = now };
                db.LineGroups.Add(row);
            }

            row.GroupName = (body.GroupName ?? "").Trim();
            row.SupplierId = body.SupplierId;
            row.GroupType = type;
            row.IsActive = body.IsActive ?? true;
            row.UpdatedAt = now;
            await db.SaveChangesAsync(token);

            await audit.RecordAsync(user, AuditActions.Update, "line-group", lineGroupId,
                row.GroupName, "supplier", before,
                $"{row.SupplierId}/{row.GroupType}/{(row.IsActive ? "on" : "off")}",
                body.Reason ?? "", token, EventSource.Line);

            return Results.Json(new { message = $"ผูกกลุ่มกับ {supplier.Name} แล้ว", id = row.Id });
        });

        /* ------------------------------------------ what it would do now */

        group.MapGet("/events/{id:long}/options", async (long id, HttpContext context,
            IUserAccessor users, ScmosDbContext db, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var row = await db.LineEvents.AsNoTracking()
                .FirstOrDefaultAsync(one => one.Id == id, token);
            if (row is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);

            /*
             * The decision as it is now, not as it was when the worker looked.
             *
             * This is what the screen opens a row against, so that what an
             * operator is shown and what the approval will do are the same
             * answer to the same question, asked a second apart. Reading the
             * stored verdict here would let the screen offer a button the
             * approval then refuses.
             */
            var now = await LineMatching.DecideAsync(
                db, row.LineGroupId, row.JobNumber, row.ParsedStatus, token);

            // Each offered row with its own status, because when a number covers
            // several the operator is choosing between them and the status is
            // what tells them apart.
            var keys = now.Keys.ToList();
            var offered = await db.OperationJobs.AsNoTracking()
                .Where(job => keys.Contains(job.Key))
                .Select(job => new { job.Key, job.Cat, job.Customer, job.Container, job.Status, job.WorkDate })
                .ToListAsync(token);

            return Results.Json(new
            {
                outcome = now.Result,
                detail = now.Detail,
                canApply = now.Applies,
                from = now.From,
                to = now.To,
                options = offered.Select(job =>
                {
                    // Whether this particular row could take the move, which the
                    // set-level answer does not say. The carrier is left empty
                    // because Move does not read it — authority was settled when
                    // these keys came out of the decision's own filtered set.
                    var move = LineAuthority.Move(
                        new LineAuthority.JobCandidate(job.Key, job.Cat, "", job.Status),
                        row.ParsedStatus);
                    return new
                    {
                        job.Key, job.Cat, job.Customer, job.Container, job.Status, job.WorkDate,
                        move = new { move.Result, move.Detail, ok = move.Applies },
                    };
                }),
                stored = row.ErrorCode,
            });
        });

        /* --------------------------------------------------- the approval */

        group.MapPost("/events/{id:long}/apply", async (long id, [FromBody] ApplyBody body,
            HttpContext context, IUserAccessor users, ScmosDbContext db, JobsRepository jobs,
            AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditAnyJob))
                return ApiResults.Error("ทำได้เฉพาะผู้ที่แก้ไขงานได้ทุกงาน", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.EditAnyJob) is { } stop) return stop;

            var row = await db.LineEvents.FirstOrDefaultAsync(one => one.Id == id, token);
            if (row is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);
            if (row.ProcessingStatus != LineProcessing.NeedReview)
                return ApiResults.Error($"ข้อความนี้ถูกจัดการไปแล้ว ({row.ProcessingStatus})",
                    StatusCodes.Status409Conflict);

            /*
             * Worked out again, now. Not read off the row.
             *
             * The verdict stored on the row was true when the worker looked. In
             * between, somebody may have delivered the job, cancelled it, or
             * moved it to another haulier — and this endpoint is the one that
             * writes, so it is the one that has to be right.
             */
            var decision = await LineMatching.DecideAsync(
                db, row.LineGroupId, row.JobNumber, row.ParsedStatus, token);

            // The one place an operator's choice is allowed in: when a number
            // covers several of the haulier's own rows, they may say which. It
            // must be one the decision itself offered — never an arbitrary key,
            // or this endpoint would become a way to set any job to any status.
            var chosen = (body.JobKey ?? "").Trim();
            if (chosen.Length > 0)
            {
                if (!decision.Keys.Contains(chosen))
                    return ApiResults.Error("งานที่เลือกไม่อยู่ในรายการที่ข้อความนี้อ้างถึง",
                        StatusCodes.Status400BadRequest);
            }
            else if (decision.Applies)
            {
                chosen = decision.Keys[0];
            }

            if (chosen.Length == 0)
                return ApiResults.Error(
                    decision.Detail.Length > 0 ? decision.Detail : decision.Result,
                    StatusCodes.Status409Conflict);

            /*
             * The key is one this haulier may speak for — it came out of the
             * set the rule filtered. What is still open is whether that one row
             * can make this move: the set-level answer for several rows said
             * nothing about any single row's status, and a number covering two
             * rows can easily have one already delivered and one not.
             */
            var one = await db.OperationJobs.AsNoTracking()
                .Where(job => job.Key == chosen)
                .Select(job => new LineAuthority.JobCandidate(job.Key, job.Cat, job.Trucker, job.Status))
                .FirstOrDefaultAsync(token);
            if (one is null) return ApiResults.Error("ไม่พบงานนี้แล้ว", StatusCodes.Status409Conflict);

            var final = LineAuthority.Move(one, row.ParsedStatus);
            if (!final.Applies)
                return ApiResults.Error(
                    final.Detail.Length > 0 ? final.Detail : final.Result,
                    StatusCodes.Status409Conflict);

            var wrote = await jobs.PatchAsync(chosen,
                new Dictionary<string, string> { ["status"] = final.To }, user.Signature, token);
            if (!wrote) return ApiResults.Error("บันทึกไม่สำเร็จ", StatusCodes.Status409Conflict);

            // Source LINE, not web. Six months from now "who set this job to
            // DELIVERED" should answer with the operator who approved it and
            // the fact that a vendor's message is why.
            await audit.RecordAsync(user, AuditActions.StatusChange, "job", chosen,
                row.JobNumber, "status", final.From, final.To,
                body.Reason ?? $"LINE: {row.RawText}", token, EventSource.Line);

            row.ProcessingStatus = LineProcessing.Processed;
            row.JobKey = chosen;
            row.ErrorCode = "";
            row.ErrorMessage = "";
            row.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);

            return Results.Json(new
            {
                message = $"อัปเดต {chosen} เป็น {final.To} แล้ว",
                jobKey = chosen, from = final.From, to = final.To,
            });
        });

        group.MapPost("/events/{id:long}/dismiss", async (long id, [FromBody] ApplyBody body,
            HttpContext context, IUserAccessor users, ScmosDbContext db,
            AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditAnyJob))
                return ApiResults.Error("ทำได้เฉพาะผู้ที่แก้ไขงานได้ทุกงาน", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.EditAnyJob) is { } stop) return stop;

            var row = await db.LineEvents.FirstOrDefaultAsync(one => one.Id == id, token);
            if (row is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);
            if (row.ProcessingStatus != LineProcessing.NeedReview)
                return ApiResults.Error($"ข้อความนี้ถูกจัดการไปแล้ว ({row.ProcessingStatus})",
                    StatusCodes.Status409Conflict);

            // Set aside, never deleted. The message is what a vendor said, and
            // an operator deciding not to act on it is itself a fact worth
            // keeping — see the retention rules on why nothing here is removed.
            row.ProcessingStatus = LineProcessing.Ignored;
            row.ErrorCode = "dismissed";
            row.ErrorMessage = (body.Reason ?? "").Trim();
            row.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);

            await audit.RecordAsync(user, AuditActions.Reject, "line-event", id.ToString(),
                row.JobNumber, "processing_status", LineProcessing.NeedReview, LineProcessing.Ignored,
                body.Reason ?? "", token, EventSource.Line);

            return Results.Json(new { message = "ปิดข้อความนี้แล้ว" });
        });
    }

    public record GroupBody(
        string? LineGroupId, string? GroupName, long SupplierId,
        string? GroupType, bool? IsActive, string? Reason);

    public record ApplyBody(string? JobKey, string? Reason);
}
