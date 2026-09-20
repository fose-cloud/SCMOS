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
                    one.MessageType,
                    one.ImageReading,
                    one.MatchedRules,
                    one.RawPayload,
                })
                .ToListAsync(token);

            // The room's name, resolved in one query rather than per row.
            var ids = rows.Select(one => one.LineGroupId).Distinct().ToList();
            var names = await db.LineGroups.AsNoTracking()
                .Where(one => ids.Contains(one.LineGroupId))
                .ToDictionaryAsync(one => one.LineGroupId, one => one.GroupName, token);

            return Results.Json(new
            {
                events = rows.Select(one =>
                {
                    // Read again, now, by the parser as it stands — the row
                    // stores the number and the status, and since 16 Sep 2026
                    // the box, the plates and the arrival clock are what a
                    // reviewer needs to see. Deterministic and cheap.
                    var shape = new LineEvent { MessageType = one.MessageType, ImageReading = one.ImageReading, MatchedRules = one.MatchedRules, RawPayload = one.RawPayload, RawText = one.RawText, ReceivedAt = one.ReceivedAt };
                    var read = LineReadings.Of(shape);
                    return new
                    {
                    one.Id, one.ReceivedAt, one.RawText, one.JobNumber, one.ParsedStatus,
                    one.Confidence, one.ProcessingStatus, one.ErrorCode, one.ErrorMessage,
                    one.JobKey, one.RetryCount,
                    container = read.Container ?? "",
                    plates = read.Plates ?? [],
                    arrival = Arrival(read),
                    messageType = one.MessageType,
                    // The room's own id as well as its name: an unbound room
                    // has no name yet, and the id is what the operator binds
                    // it by. It was resolved to a name and then dropped, so
                    // the first real message in (16 Sep 2026) said "ยังไม่ผูก"
                    // and gave nothing to bind.
                    one.LineGroupId,
                    // A TMS row has no room; the supplier and the credential stand in its place.
                    group = LineReadings.EventOf(shape)?.GroupLabel ?? names.GetValueOrDefault(one.LineGroupId, ""),
                    };
                }),
                count = rows.Count,
            });
        });

        /*
         * What is waiting on which job — for the workspace, which draws a
         * mark on the row and lets the job's owner approve from the drawer.
         * Only messages the rule pinned to exactly one job; a message that
         * could mean several is the review screen's to settle.
         */
        group.MapGet("/events/pending", async (HttpContext context, IUserAccessor users,
            ScmosDbContext db, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var rows = await db.LineEvents.AsNoTracking()
                .Where(one => one.ProcessingStatus == LineProcessing.NeedReview && one.JobKey != ""
                    && (one.MessageType == "text" || one.MessageType == CarrierEvent.MessageType))
                .OrderByDescending(one => one.ReceivedAt)
                .Take(300)
                .Select(one => new
                {
                    one.Id, one.JobKey, one.LineGroupId, one.MessageType, one.RawText, one.ReceivedAt,
                    one.ParsedStatus, one.ErrorCode, one.ErrorMessage, one.ImageReading, one.MatchedRules, one.RawPayload,
                })
                .ToListAsync(token);

            var keys = rows.SelectMany(one => LineAuthority.KeysIn(one.ErrorMessage).Append(one.JobKey)).Distinct().ToList();
            var categories = await db.OperationJobs.AsNoTracking()
                .Where(job => keys.Contains(job.Key))
                .Select(job => new { job.Key, job.Cat })
                .ToDictionaryAsync(job => job.Key, job => job.Cat, token);
            var ids = rows.Select(one => one.LineGroupId).Distinct().ToList();
            var names = await db.LineGroups.AsNoTracking()
                .Where(one => ids.Contains(one.LineGroupId))
                .ToDictionaryAsync(one => one.LineGroupId, one => one.GroupName, token);

            return Results.Json(new
            {
                // A message about every row of a job number ("3 ตู้") is one
                // row here and three items: one per job, so each job's drawer
                // shows it. Approving any of them writes all.
                items = rows.SelectMany(one =>
                {
                    var keys = LineAuthority.KeysIn(one.ErrorMessage);
                    var every = one.ErrorCode == "ready-to-apply" && keys.Count > 1 && keys.Contains(one.JobKey);
                    return every ? keys.Select(key => (Row: one, JobKey: key, Every: keys.Count)) : [(Row: one, JobKey: one.JobKey, Every: 0)];
                }).Select(item =>
                {
                    var one = item.Row;
                    var shape = new LineEvent { MessageType = one.MessageType, ImageReading = one.ImageReading, MatchedRules = one.MatchedRules, RawPayload = one.RawPayload, RawText = one.RawText, ReceivedAt = one.ReceivedAt };
                    var read = LineReadings.Of(shape);
                    var category = categories.GetValueOrDefault(item.JobKey, "");
                    return new
                    {
                        one.Id, item.JobKey, one.ReceivedAt, one.ErrorCode,
                        // How many jobs approving writes, when more than this one.
                        every = item.Every,
                        detail = one.ErrorMessage,
                        kind = one.MessageType,
                        text = one.RawText,
                        group = LineReadings.EventOf(shape)?.GroupLabel ?? names.GetValueOrDefault(one.LineGroupId, ""),
                        // What approving would write: the status as this job's
                        // ladder names it, and the arrival clock.
                        to = LineAuthority.ResolveSite(category, one.ParsedStatus),
                        arrival = Arrival(read),
                        // "ประมาณ 10.00 รถถึงโรงงาน": when the truck is expected, for the
                        // owner to see — written nowhere.
                        eta = read.Eta is { } eta ? eta.ToString("HH:mm") : "",
                        // The truck's details the message carries, for the drawer's line.
                        details = string.Join(" · ", new[]
                        {
                            read.Plates is { Count: > 0 } ? $"ทะเบียน {read.Plates[0]}" : "",
                            read.Driver is not null ? $"คนขับ {read.Driver}" : "",
                            read.Phone is not null ? $"เบอร์ {read.Phone}" : "",
                            read.SealNumber is not null ? $"ซีล {read.SealNumber}" : "",
                            // The box the driver's photos gave a text that named none.
                            one.ImageReading.Length > 0 ? $"ตู้ {one.ImageReading} (จากรูป)" : "",
                        }.Where(part => part.Length > 0)),
                        ready = one.ErrorCode == "ready-to-apply",
                    };
                }),
            });
        });

        /* ------------------------------------------- the morning reminder */

        // What today's reminder would say to each room, and when it went.
        group.MapGet("/reminder", async (string? date, HttpContext context, IUserAccessor users,
            LineReminderService reminders, LineChaseService chase, ILineNotifier notifier, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var day = Day(date);
            var rooms = await reminders.PreviewAsync(day, token);
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            // What the chase would ask right now, so the screen can say so.
            var due = date is null ? await chase.DueAsync(DateTimeOffset.UtcNow, token) : [];
            return Results.Json(new
            {
                date = Formats.PlanDate(day),
                remindAt = reminders.RemindAtText,
                summaryAt = reminders.SummaryAtText,
                replies = LineEventWorker.RepliesOn(config),
                // The status chase: minutes before the plan time (0 when off)
                // and the day's rounds ("10:00, 14:00", empty when off).
                chaseBeforeMinutes = chase.BeforeMinutes,
                chaseAt = chase.RoundsText,
                chaseDue = due.Select(room => new
                {
                    room.LineGroupId, room.GroupName, room.Supplier,
                    jobs = room.Jobs.Select(one => new { one.Job.Key, one.Job.Customer, one.Job.Container, one.Job.PlanTime, stage = one.Stage }),
                    message = room.Message,
                }),
                canPush = notifier.Configured,
                pushMessage = notifier.Configured ? "" : notifier.Missing,
                rooms = rooms.Select(room => new
                {
                    room.LineGroupId, room.GroupName, room.Supplier,
                    jobs = room.Jobs.Count,
                    missing = room.Jobs.Select(job => new
                    {
                        job.Key, job.Category, job.Customer, job.JobCode, job.Booking, job.Container,
                        gaps = LineReminder.Missing(job),
                    }),
                    messages = room.Messages,
                    sentAt = room.SentAt,
                    sentBy = room.SentBy,
                    sentSlots = room.SentSlots,
                }),
            });
        });

        // Sends it now, to one room or to every room — a person's decision,
        // by somebody who manages suppliers, and audited under their name.
        group.MapPost("/reminder", async ([FromBody] ReminderBody body, HttpContext context, IUserAccessor users,
            LineReminderService reminders, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("ทำได้เฉพาะผู้ที่ดูแลผู้ขนส่ง", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.ManageSuppliers) is { } stop) return stop;

            var day = Day(body.Date);
            var wanted = (body.LineGroupId ?? "").Trim();
            var rooms = (await reminders.PreviewAsync(day, token))
                .Where(room => wanted.Length == 0 || room.LineGroupId == wanted)
                .ToList();
            if (rooms.Count == 0) return ApiResults.Error("ไม่พบกลุ่มที่ผูกกับผู้ขนส่ง", StatusCodes.Status404NotFound);

            var results = new List<object>();
            var sent = 0;
            foreach (var room in rooms)
            {
                var failure = await reminders.SendAsync(room, user, "ส่งจากหน้าจอ LINE", LineReminderService.ManualSlot, token);
                if (failure.Length == 0) sent++;
                results.Add(new { room.LineGroupId, room.GroupName, room.Supplier, jobs = room.Jobs.Count, ok = failure.Length == 0, failure });
            }
            return Results.Json(new
            {
                message = sent == rooms.Count ? $"ส่งแจ้งเตือนแล้ว {sent} กลุ่ม"
                    : sent == 0 ? (results.Count == 1 ? ((dynamic)results[0]).failure : "ส่งไม่สำเร็จ")
                    : $"ส่งแล้ว {sent} จาก {rooms.Count} กลุ่ม",
                sent,
                results,
            });
        });

        /* ------------------------------------------- the day-before summary */

        // What the next summary would say to each room — tomorrow's, or on a
        // Friday the weekend's and Monday's — or any one day's by ?date=,
        // and whether it has gone.
        group.MapGet("/summary", async (string? date, HttpContext context, IUserAccessor users,
            LineReminderService reminders, ILineNotifier notifier, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var days = SummaryDaysFor(date);
            var rooms = await reminders.PreviewSummaryAsync(days, token);
            var quota = await notifier.QuotaAsync(token);
            return Results.Json(new
            {
                date = LineReminder.SpanLabel(days),
                days = days.Select(Formats.PlanDate),
                summaryAt = reminders.SummaryAtText,
                canPush = notifier.Configured,
                pushMessage = notifier.Configured ? "" : notifier.Missing,
                quotaLimit = quota.Limit,
                quotaUsed = quota.Used,
                quotaExhausted = quota.Exhausted,
                quotaMessage = quota.Problem,
                rooms = rooms.Select(room => new
                {
                    room.LineGroupId, room.GroupName, room.Supplier, room.Jobs,
                    messages = room.Messages,
                    sentAt = room.SentAt,
                    sentBy = room.SentBy,
                }),
            });
        });

        // Sends it now, to one room — a person's decision, audited under their
        // name; the way the department tests a new message before its hour.
        group.MapPost("/summary", async ([FromBody] ReminderBody body, HttpContext context, IUserAccessor users,
            LineReminderService reminders, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("ทำได้เฉพาะผู้ที่ดูแลผู้ขนส่ง", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.ManageSuppliers) is { } stop) return stop;

            var days = SummaryDaysFor(body.Date);
            var wanted = (body.LineGroupId ?? "").Trim();
            var rooms = (await reminders.PreviewSummaryAsync(days, token))
                .Where(room => wanted.Length == 0 || room.LineGroupId == wanted)
                .ToList();
            if (rooms.Count == 0) return ApiResults.Error("ไม่พบกลุ่มที่ผูกกับผู้ขนส่ง", StatusCodes.Status404NotFound);

            var results = new List<object>();
            var sent = 0;
            foreach (var room in rooms)
            {
                var failure = await reminders.SendSummaryAsync(room, user, "ส่งจากหน้าจอ LINE", token);
                if (failure.Length == 0) sent++;
                results.Add(new { room.LineGroupId, room.GroupName, room.Supplier, room.Jobs, ok = failure.Length == 0, failure });
            }
            return Results.Json(new
            {
                message = sent == rooms.Count ? $"ส่งสรุปงานวันที่ {LineReminder.SpanLabel(days)} แล้ว {sent} กลุ่ม"
                    : sent == 0 ? (results.Count == 1 ? ((dynamic)results[0]).failure : "ส่งไม่สำเร็จ")
                    : $"ส่งแล้ว {sent} จาก {rooms.Count} กลุ่ม",
                sent,
                results,
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
            IUserAccessor users, ScmosDbContext db, JobsRepository jobs, DelegationService delegations,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var row = await db.LineEvents.AsNoTracking()
                .FirstOrDefaultAsync(one => one.Id == id, token);
            if (row is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);

            if (row.MessageType == "image")
                return Results.Json(new
                {
                    outcome = LineEventWorker.PhotoReadingRetired, detail = "การอ่านรูปถูกถอดออกจากระบบแล้ว (17 ก.ย. 2026)", canApply = false,
                    from = "", to = "", reference = new { jobNumber = "", container = "", plates = Array.Empty<string>() },
                    arrival = new { date = "", time = "", atSend = false }, options = Array.Empty<object>(), stored = row.ErrorCode,
                });

            /*
             * The decision as it is now, not as it was when the worker looked.
             *
             * This is what the screen opens a row against, so that what an
             * operator is shown and what the approval will do are the same
             * answer to the same question, asked a second apart. Reading the
             * stored verdict here would let the screen offer a button the
             * approval then refuses.
             */
            var read = LineReadings.Of(row);
            var now = await LineReadings.DecideAsync(db, row, read, token);

            // Each offered row with its own status, because when a number covers
            // several the operator is choosing between them and the status is
            // what tells them apart.
            var keys = now.Keys.ToList();
            var offered = await db.OperationJobs.AsNoTracking()
                .Where(job => keys.Contains(job.Key))
                .Select(job => new { job.Key, job.Cat, job.Customer, job.Container, job.Status, job.WorkDate, job.Data })
                .ToListAsync(token);

            return Results.Json(new
            {
                outcome = now.Result,
                detail = now.Detail,
                canApply = now.Applies,
                from = now.From,
                to = now.To,
                reference = new { jobNumber = read.JobNumber ?? "", container = read.Container ?? "", plates = read.Plates ?? [] },
                arrival = Arrival(read),
                options = await Task.WhenAll(offered.Select(async job =>
                {
                    // Whether this particular row could take the move, which the
                    // set-level answer does not say. The carrier is left empty
                    // because Move does not read it — authority was settled when
                    // these keys came out of the decision's own filtered set.
                    var candidate = LineMatching.Candidate(job.Key, job.Cat, "", job.Status, job.Data, job.Customer, job.Container, job.WorkDate);
                    var move = string.IsNullOrWhiteSpace(read.Status) && read.HasDetails
                        ? LineAuthority.Details(candidate, LineAuthority.FillsOf(read))
                        : LineAuthority.Move(candidate, read.Status, read.ArrivalTime is not null);
                    var stamp = ArrivalWrite(read, job.Data);
                    var truck = DetailsWrite(read, job.Data);
                    return new
                    {
                        job.Key, job.Cat, job.Customer, job.Container, job.Status, job.WorkDate,
                        move = new { move.Result, move.Detail, ok = move.Applies, to = move.To },
                        // What approving would write into ARRIVAL DATE / TIME,
                        // or why it would not — and into LICENCE / DRIVER / CONTACT.
                        arrival = string.Join(" · ", new[] { stamp.Note, truck.Note }.Where(one => one.Length > 0)),
                        // Whether this person may approve this row: anybody who
                        // edits every job, or the job's own owner.
                        mayApprove = await MayActOnAsync(user, job.Key, jobs, delegations, token),
                    };
                })),
                stored = row.ErrorCode,
            });
        });

        /* --------------------------------------------------- the approval */

        group.MapPost("/events/{id:long}/apply", async (long id, [FromBody] ApplyBody body,
            HttpContext context, IUserAccessor users, ScmosDbContext db, JobsRepository jobs,
            DelegationService delegations, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            // Since 16 Sep 2026 the job's own owner approves what a haulier
            // said about their job, the way they edit it — "กำหนดให้เจ้าของงานกด
            // Approve เองได้เลย". Which job it is comes out of the decision
            // below; ownership is checked there, on the row that would change.
            if (!user.Can(Capability.EditAnyJob) && !user.Can(Capability.EditOwnJobs))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขข้อมูลงาน", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user,
                    user.Can(Capability.EditAnyJob) ? Capability.EditAnyJob : Capability.EditOwnJobs) is { } stop)
                return stop;

            var row = await db.LineEvents.FirstOrDefaultAsync(one => one.Id == id, token);
            if (row is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);
            if (row.ProcessingStatus != LineProcessing.NeedReview)
                return ApiResults.Error($"ข้อความนี้ถูกจัดการไปแล้ว ({row.ProcessingStatus})",
                    StatusCodes.Status409Conflict);

            // Claimed before anything is written, so a second click on the
            // same button — arriving while the first is still writing, which
            // is what happened on 17 Sep 2026 — finds the row taken and does
            // not write the job and its audit twice. Released if this request
            // ends without settling the row.
            if (!await ClaimAsync(db, row, token))
                return ApiResults.Error("ข้อความนี้กำลังถูกจัดการอยู่", StatusCodes.Status409Conflict);
            var settled = false;
            try
            {
                var result = await ApplyClaimedAsync(row, body, user, db, jobs, delegations, audit, token);
                settled = row.ProcessingStatus != LineProcessing.Processing;
                // A carrier's TMS hears what became of its event (phase 4).
                if (row.ProcessingStatus == LineProcessing.Processed && LineReadings.IsTms(row))
                    await context.RequestServices.GetRequiredService<CarrierWebhookQueue>()
                        .EventDecidedAsync(row, "applied", row.ParsedStatus, user.Signature, token);
                return result;
            }
            finally
            {
                // An early return, or a fault part-way: back in the queue. A
                // row already saved as settled is not touched — the release
                // is conditional on it still being held.
                if (!settled) await ReleaseAsync(db, row, CancellationToken.None);
            }
        });

        group.MapPost("/events/{id:long}/dismiss", async (long id, [FromBody] ApplyBody body,
            HttpContext context, IUserAccessor users, ScmosDbContext db, JobsRepository jobs,
            DelegationService delegations, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditAnyJob) && !user.Can(Capability.EditOwnJobs))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขข้อมูลงาน", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user,
                    user.Can(Capability.EditAnyJob) ? Capability.EditAnyJob : Capability.EditOwnJobs) is { } stop)
                return stop;

            var row = await db.LineEvents.FirstOrDefaultAsync(one => one.Id == id, token);
            if (row is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);
            if (row.ProcessingStatus != LineProcessing.NeedReview)
                return ApiResults.Error($"ข้อความนี้ถูกจัดการไปแล้ว ({row.ProcessingStatus})",
                    StatusCodes.Status409Conflict);

            // An owner sets aside only a message pinned to their own job. A
            // message pinned to nothing is the review screen's to close.
            if (!user.Can(Capability.EditAnyJob)
                && (row.JobKey.Length == 0 || !await MayActOnAsync(user, row.JobKey, jobs, delegations, token)))
                return ApiResults.Error("ปิดได้เฉพาะข้อความของงานตัวเอง หรืองานที่ดูแลแทนอยู่", StatusCodes.Status403Forbidden);

            if (!await ClaimAsync(db, row, token))
                return ApiResults.Error("ข้อความนี้กำลังถูกจัดการอยู่", StatusCodes.Status409Conflict);

            // Set aside, never deleted. The message is what a vendor said, and
            // an operator deciding not to act on it is itself a fact worth
            // keeping — see the retention rules on why nothing here is removed.
            row.ProcessingStatus = LineProcessing.Ignored;
            row.ErrorCode = "dismissed";
            row.ErrorMessage = (body.Reason ?? "").Trim();
            row.ProcessedAt = DateTimeOffset.UtcNow;
            try
            {
                await db.SaveChangesAsync(token);
            }
            catch
            {
                await ReleaseAsync(db, row, CancellationToken.None);
                throw;
            }

            await audit.RecordAsync(user, AuditActions.Reject, "line-event", id.ToString(),
                row.JobNumber, "processing_status", LineProcessing.NeedReview, LineProcessing.Ignored,
                body.Reason ?? "", token, LineReadings.SourceOf(row));
            if (LineReadings.IsTms(row))
                await context.RequestServices.GetRequiredService<CarrierWebhookQueue>()
                    .EventDecidedAsync(row, "dismissed", "", user.Signature, token);

            return Results.Json(new { message = "ปิดข้อความนี้แล้ว" });
        });
    }

    /// <summary>
    /// Takes a queued row for one request: NEED_REVIEW to PROCESSING in a
    /// single statement, so of two requests for the same row only one gets
    /// it. The tracked entity is brought into step so a later save does not
    /// put the old value back.
    /// </summary>
    private static async Task<bool> ClaimAsync(ScmosDbContext db, LineEvent row, CancellationToken token)
    {
        var taken = await db.LineEvents
            .Where(one => one.Id == row.Id && one.ProcessingStatus == LineProcessing.NeedReview)
            .ExecuteUpdateAsync(set => set.SetProperty(one => one.ProcessingStatus, LineProcessing.Processing), token);
        if (taken != 1) return false;
        row.ProcessingStatus = LineProcessing.Processing;
        db.Entry(row).Property(one => one.ProcessingStatus).IsModified = false;
        return true;
    }

    /// <summary>Puts a claimed row back in the queue: the request ended without settling it.</summary>
    private static async Task ReleaseAsync(ScmosDbContext db, LineEvent row, CancellationToken token)
    {
        await db.LineEvents
            .Where(one => one.Id == row.Id && one.ProcessingStatus == LineProcessing.Processing)
            .ExecuteUpdateAsync(set => set.SetProperty(one => one.ProcessingStatus, LineProcessing.NeedReview), token);
        row.ProcessingStatus = LineProcessing.NeedReview;
        db.Entry(row).Property(one => one.ProcessingStatus).IsModified = false;
    }

    /// <summary>The approval proper, on a row this request holds. Every early return leaves the row for the caller to release.</summary>
    private static async Task<IResult> ApplyClaimedAsync(LineEvent row, ApplyBody body, AppUser user,
        ScmosDbContext db, JobsRepository jobs, DelegationService delegations, AuditService audit, CancellationToken token)
    {
        if (row.MessageType == "image")
            return ApiResults.Error("การอ่านรูปถูกถอดออกจากระบบแล้ว — รูปนี้อนุมัติไม่ได้", StatusCodes.Status409Conflict);

        /*
         * Worked out again, now. Not read off the row.
         *
         * The verdict stored on the row was true when the worker looked. In
         * between, somebody may have delivered the job, cancelled it, or
         * moved it to another haulier — and this endpoint is the one that
         * writes, so it is the one that has to be right.
         */
        var read = LineReadings.Of(row);
        var decision = await LineReadings.DecideAsync(db, row, read, token);
        var source = LineReadings.SourceOf(row);

        // "260900760321 3 ตู้ อยู่โรงงาน" for three rows: every row takes it.
        if (decision.Every)
            return await ApplyEveryAsync(row, body, user, read, decision, db, jobs, delegations, audit, token);

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
        if (!await MayActOnAsync(user, chosen, jobs, delegations, token))
            return ApiResults.Error("อนุมัติได้เฉพาะงานของตัวเอง หรืองานที่ดูแลแทนอยู่", StatusCodes.Status403Forbidden);

        /*
         * The key is one this haulier may speak for — it came out of the
         * set the rule filtered. What is still open is whether that one row
         * can make this move: the set-level answer for several rows said
         * nothing about any single row's status, and a number covering two
         * rows can easily have one already delivered and one not.
         */
        var one = await db.OperationJobs.AsNoTracking()
            .Where(job => job.Key == chosen)
            .Select(job => new { job.Key, job.Cat, job.Trucker, job.Status, job.Data })
            .FirstOrDefaultAsync(token);
        if (one is null) return ApiResults.Error("ไม่พบงานนี้แล้ว", StatusCodes.Status409Conflict);

        var candidate = LineMatching.Candidate(one.Key, one.Cat, one.Trucker, one.Status, one.Data);
        var final = string.IsNullOrWhiteSpace(read.Status) && read.HasDetails
            ? LineAuthority.Details(candidate, LineAuthority.FillsOf(read))
            : LineAuthority.Move(candidate, read.Status, read.ArrivalTime is not null);
        if (!final.Applies)
            return ApiResults.Error(
                final.Detail.Length > 0 ? final.Detail : final.Result,
                StatusCodes.Status409Conflict);

        // The arrival the driver reported goes in beside the status, into
        // the cells on-time delivery is measured from — when they are
        // empty. A stamp somebody already keyed is not overwritten from a
        // chat room; the answer says so and the operator can change it on
        // the grid if the driver is right.
        var stamp = ArrivalWrite(read, one.Data);
        var fields = new Dictionary<string, string>();
        if (final.To.Length > 0) fields["status"] = final.To;
        if (stamp.Date is { } date && stamp.Time is { } time)
        {
            fields["arrDate"] = date;
            fields["arrTime"] = time;
        }
        // The truck's details, into the cells that are empty — the answer
        // to the morning reminder, written onto the job the owner is
        // looking at.
        var truck = DetailsWrite(read, one.Data);
        foreach (var (name, value) in truck.Fields) fields[name] = value;
        if (fields.Count == 0)
            return ApiResults.Error("ข้อความนี้ไม่มีอะไรให้บันทึก — งานมีข้อมูลเหล่านี้อยู่แล้ว", StatusCodes.Status409Conflict);

        var wrote = await jobs.PatchAsync(chosen, fields, user.Signature, token);
        if (!wrote) return ApiResults.Error("บันทึกไม่สำเร็จ", StatusCodes.Status409Conflict);

        // Source LINE, not web. Six months from now "who set this job to
        // DELIVERED" should answer with the operator who approved it and
        // the fact that a vendor's message is why.
        var why = body.Reason ?? LineReadings.ReasonOf(row);
        if (final.To.Length > 0)
            await audit.RecordAsync(user, AuditActions.StatusChange, "job", chosen,
                row.JobNumber, "status", final.From, final.To, why, token, source);
        if (stamp.Date is not null)
        {
            await audit.RecordAsync(user, AuditActions.Update, "job", chosen,
                row.JobNumber, "arrDate", stamp.HadDate, stamp.Date, why, token, source);
            await audit.RecordAsync(user, AuditActions.Update, "job", chosen,
                row.JobNumber, "arrTime", stamp.HadTime, stamp.Time!, why, token, source);
        }
        foreach (var (name, value) in truck.Fields)
            await audit.RecordAsync(user, AuditActions.Update, "job", chosen,
                row.JobNumber, name, "", value, why, token, source);

        row.ProcessingStatus = LineProcessing.Processed;
        row.JobKey = chosen;
        row.ErrorCode = "";
        row.ErrorMessage = "";
        row.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        // Nothing is said back into the room on approval. The room was
        // told "รับทราบ" when the message was read; an "อัปเดตแล้ว" for
        // every approval was asked to stop on 17 Sep 2026 — it is the
        // department's record, not the haulier's.
        return Results.Json(new
        {
            message = (final.To.Length > 0 ? $"อัปเดต {chosen} เป็น {final.To} แล้ว" : $"บันทึกลงงาน {chosen} แล้ว")
                + (stamp.Date is not null ? $" · เวลาถึง {stamp.Date} {stamp.Time}" : "")
                + (truck.Fields.Count > 0 ? " · " + truck.Written : ""),
            jobKey = chosen, from = final.From, to = final.To,
            arrival = stamp.Note,
        });
    }

    /// <summary>
    /// A message about every row of a job number, written onto each row
    /// that can take it. Asked for on 17 Sep 2026 with the example
    /// "CATALITE 260900760321 3 ตู้ อยู่โรงงาน": box 1, 2 and 3 each arrived
    /// at 08:36, the time the haulier sent it.
    ///
    /// A row that cannot take the move — already there, closed, held — is
    /// skipped and said, not refused for the rest: two boxes written and one
    /// already delivered is the right outcome. Nothing is written when no
    /// row can take it. The approver must be allowed every row it writes.
    /// </summary>
    private static async Task<IResult> ApplyEveryAsync(LineEvent row, ApplyBody body, AppUser user,
        LineParser.Parsed read, LineAuthority.LineDecision decision,
        ScmosDbContext db, JobsRepository jobs, DelegationService delegations, AuditService audit, CancellationToken token)
    {
        var chosen = (body.JobKey ?? "").Trim();
        if (chosen.Length > 0 && !decision.Keys.Contains(chosen))
            return ApiResults.Error("งานที่เลือกไม่อยู่ในรายการที่ข้อความนี้อ้างถึง", StatusCodes.Status400BadRequest);

        var keys = decision.Keys.ToList();
        var rows = await db.OperationJobs.AsNoTracking()
            .Where(job => keys.Contains(job.Key))
            .Select(job => new { job.Key, job.Cat, job.Trucker, job.Status, job.Data, job.Container })
            .ToListAsync(token);

        var written = new List<string>();
        var skipped = new List<string>();
        var why = body.Reason ?? $"LINE: {row.RawText}";
        foreach (var key in keys)
        {
            var one = rows.FirstOrDefault(job => job.Key == key);
            if (one is null) { skipped.Add($"{key}: ไม่พบงานนี้แล้ว"); continue; }
            if (!await MayActOnAsync(user, key, jobs, delegations, token))
                return ApiResults.Error($"อนุมัติได้เฉพาะงานของตัวเอง หรืองานที่ดูแลแทนอยู่ ({key})", StatusCodes.Status403Forbidden);

            var final = LineAuthority.Move(LineMatching.Candidate(one.Key, one.Cat, one.Trucker, one.Status, one.Data), read.Status, read.ArrivalTime is not null);
            if (!final.Applies) { skipped.Add($"{Name(one.Container, key)}: {(final.Detail.Length > 0 ? final.Detail : final.Result)}"); continue; }

            var stamp = ArrivalWrite(read, one.Data);
            var fields = new Dictionary<string, string>();
            if (final.To.Length > 0) fields["status"] = final.To;
            if (stamp.Date is { } date && stamp.Time is { } time)
            {
                fields["arrDate"] = date;
                fields["arrTime"] = time;
            }
            if (fields.Count == 0) { skipped.Add($"{Name(one.Container, key)}: ไม่มีอะไรให้บันทึก"); continue; }

            if (!await jobs.PatchAsync(key, fields, user.Signature, token)) { skipped.Add($"{Name(one.Container, key)}: บันทึกไม่สำเร็จ"); continue; }

            if (final.To.Length > 0)
                await audit.RecordAsync(user, AuditActions.StatusChange, "job", key,
                    row.JobNumber, "status", final.From, final.To, why, token, EventSource.Line);
            if (stamp.Date is not null)
            {
                await audit.RecordAsync(user, AuditActions.Update, "job", key,
                    row.JobNumber, "arrDate", stamp.HadDate, stamp.Date, why, token, EventSource.Line);
                await audit.RecordAsync(user, AuditActions.Update, "job", key,
                    row.JobNumber, "arrTime", stamp.HadTime, stamp.Time!, why, token, EventSource.Line);
            }
            written.Add($"{Name(one.Container, key)} → {final.To}{(stamp.Date is not null ? $" ถึง {stamp.Time}" : "")}");
        }

        if (written.Count == 0)
            return ApiResults.Error("ไม่มีรายการไหนรับข้อความนี้ได้ — " + string.Join(" · ", skipped), StatusCodes.Status409Conflict);

        row.ProcessingStatus = LineProcessing.Processed;
        row.JobKey = chosen.Length > 0 ? chosen : keys[0];
        row.ErrorCode = "";
        row.ErrorMessage = LineAuthority.KeysNote(
            $"อัปเดต {written.Count} จาก {keys.Count} รายการ" + (skipped.Count > 0 ? $" — ข้าม: {string.Join(" · ", skipped)}" : ""), keys);
        row.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        return Results.Json(new
        {
            message = $"อัปเดต {written.Count} รายการแล้ว: {string.Join(" · ", written)}"
                + (skipped.Count > 0 ? $" · ข้าม {skipped.Count}: {string.Join(" · ", skipped)}" : ""),
            jobKey = row.JobKey, from = "", to = decision.To,
            arrival = read.ArrivalTime is { } at ? $"เวลาถึง {Formats.PlanDate(DateOnly.FromDateTime(at.DateTime))} {at:HH:mm}{(read.ArrivalAtSend ? " (เวลาที่ส่งข้อความ)" : "")}" : "",
        });

        static string Name(string container, string key) => Formats.Clean(container).Length > 0 ? Formats.Clean(container) : key;
    }

    /* ------------------------------------------------------ a photograph */

    /// <summary>
    /// Whether this person may write what a message says onto this job: they
    /// edit every job, or it is their own — or one they are covering for a
    /// colleague on leave. The same rule the jobs endpoint enforces on a save,
    /// asked through the same two services, so the two cannot drift.
    /// </summary>
    private static async Task<bool> MayActOnAsync(AppUser user, string jobKey, JobsRepository jobs,
        DelegationService delegations, CancellationToken token)
    {
        if (user.Can(Capability.EditAnyJob)) return true;
        if (!user.Can(Capability.EditOwnJobs) || jobKey.Length == 0) return false;
        var acting = await delegations.ActingForAsync(user.OperatorId, token);
        var others = await jobs.OthersJobsAsync([jobKey], user.OperatorId, token, acting);
        return others.Count == 0;
    }

    /// <summary>
    /// What approving would write into LICENCE, DRIVER and CONTACT: the plate,
    /// the name and the number the message carries, each into its cell only
    /// when that cell is empty. A cell somebody keyed is not replaced from a
    /// chat room; the note says so, and the grid is where it is changed.
    /// </summary>
    private static (Dictionary<string, string> Fields, string Note, string Written) DetailsWrite(LineParser.Parsed read, string data)
    {
        var fields = new Dictionary<string, string>();
        var notes = new List<string>();
        var written = new List<string>();
        if (!read.HasDetails) return (fields, "", "");

        var had = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(data);
            foreach (var name in new[] { "licence", "driver", "contact", "container", "seal" })
            {
                if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                    had[name] = Formats.Clean(value.GetString());
            }
        }
        catch (System.Text.Json.JsonException) { }

        void Offer(string name, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var current = had.GetValueOrDefault(name, "");
            if (current.Length == 0)
            {
                fields[name] = value;
                written.Add($"{label} {value}");
            }
            else if (!string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"งานมี{label} {current} อยู่แล้ว (ข้อความแจ้ง {value})");
            }
        }
        Offer("licence", "ทะเบียน", read.Plates is { Count: > 0 } ? read.Plates[0] : null);
        Offer("driver", "ชื่อคนขับ", read.Driver);
        Offer("contact", "เบอร์", read.Phone);
        // The box and the seal, for the job the message names by number or
        // booking — an export's, once loaded. A container that is itself how
        // the job was found is already on the job and is offered to nothing.
        if (read.JobNumber is not null || read.References is { Count: > 0 })
            Offer("container", "เลขตู้", read.Container);
        Offer("seal", "เลขซีล", read.SealNumber);

        var note = written.Count > 0 ? "จะบันทึก " + string.Join(" · ", written) : "";
        if (notes.Count > 0) note = string.Join(" · ", new[] { note }.Concat(notes).Where(one => one.Length > 0));
        return (fields, note, string.Join(" · ", written));
    }

    /// <summary>The arrival the message reported, as the register writes it, or empty.</summary>
    private static object Arrival(LineParser.Parsed read) =>
        read.ArrivalTime is { } at
            ? new { date = Formats.PlanDate(DateOnly.FromDateTime(at.DateTime)), time = at.ToString("HH:mm"), atSend = read.ArrivalAtSend }
            : new { date = "", time = "", atSend = false };

    /// <summary>
    /// What approving would write into a job's ARRIVAL DATE / TIME.
    ///
    /// Written only into empty cells. Those two cells are what on-time delivery
    /// is measured from, and a value an operator keyed from the paperwork is
    /// not replaced by a driver's message — the note says which it was, and the
    /// grid is where a person corrects a stamp.
    /// </summary>
    internal static (string? Date, string? Time, string HadDate, string HadTime, string Note) ArrivalWrite(
        LineParser.Parsed read, string data) =>
        read.ArrivalTime is { } at ? StampWrite(at, read.ArrivalAtSend, data) : (null, null, "", "", "");

    /// <summary>The arrival stamp a moment would write — a clock the driver gave, the send time of a message or a photo.</summary>
    private static (string? Date, string? Time, string HadDate, string HadTime, string Note) StampWrite(
        DateTimeOffset at, bool atSend, string data)
    {
        var date = Formats.PlanDate(DateOnly.FromDateTime(at.DateTime));
        var time = at.ToString("HH:mm");
        var (hadDate, hadTime) = ("", "");
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(data);
            if (json.RootElement.TryGetProperty("arrDate", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String)
                hadDate = Formats.Clean(d.GetString());
            if (json.RootElement.TryGetProperty("arrTime", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String)
                hadTime = Formats.Clean(t.GetString());
        }
        catch (System.Text.Json.JsonException) { }

        var basis = atSend ? " (เวลาที่ส่ง — ไม่มีเวลาในข้อความ)" : "";
        if (hadDate.Length == 0 && hadTime.Length == 0)
            return (date, time, hadDate, hadTime, $"จะบันทึกเวลาถึง {date} {time}{basis}");
        if (hadDate == date && hadTime == time)
            return (null, null, hadDate, hadTime, $"เวลาถึง {date} {time} ตรงกับที่บันทึกไว้แล้ว");
        return (null, null, hadDate, hadTime,
            $"งานมีเวลาถึง {hadDate} {hadTime} อยู่แล้ว — ข้อความแจ้ง {date} {time}; แก้ในตารางงานถ้าต้องการ");
    }

    /// <summary>
    /// The days a summary is about: the one day named, or — with none — the
    /// days the next scheduled send covers, which on a Friday are three.
    /// </summary>
    private static IReadOnlyList<DateOnly> SummaryDaysFor(string? date) =>
        date is not null ? [Day(date)] : LineReminder.SummaryDays(Day(null));

    /// <summary>The day a reminder is about, read the way the register writes dates; today in Bangkok otherwise.</summary>
    private static DateOnly Day(string? date)
    {
        var (year, month, day) = Formats.PartsOf(date ?? "");
        if (year.Length > 0 && DateOnly.TryParseExact($"{year}-{month}-{day}", "yyyy-M-d",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var chosen))
            return chosen;
        return DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
    }

    public record ReminderBody(string? LineGroupId, string? Date);

    public record GroupBody(
        string? LineGroupId, string? GroupName, long SupplierId,
        string? GroupType, bool? IsActive, string? Reason);

    public record ApplyBody(string? JobKey, string? Reason);
}
