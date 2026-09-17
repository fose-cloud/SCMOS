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
                    one.ImageKey,
                    one.ImageReading,
                    one.ImageNote,
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
                    var read = LineParser.Parse(one.RawText, one.ReceivedAt);
                    return new
                    {
                    one.Id, one.ReceivedAt, one.RawText, one.JobNumber, one.ParsedStatus,
                    one.Confidence, one.ProcessingStatus, one.ErrorCode, one.ErrorMessage,
                    one.JobKey, one.RetryCount,
                    container = one.MessageType == "image" ? one.ImageReading : read.Container ?? "",
                    plates = read.Plates ?? [],
                    arrival = Arrival(read),
                    // A photo: what the model read, its sentence on the photo,
                    // and whether the photo itself was kept to be shown.
                    messageType = one.MessageType,
                    imageReading = one.ImageReading,
                    imageNote = one.ImageNote,
                    hasImage = one.ImageKey.Length > 0,
                    // The room's own id as well as its name: an unbound room
                    // has no name yet, and the id is what the operator binds
                    // it by. It was resolved to a name and then dropped, so
                    // the first real message in (16 Sep 2026) said "ยังไม่ผูก"
                    // and gave nothing to bind.
                    one.LineGroupId,
                    group = names.GetValueOrDefault(one.LineGroupId, ""),
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
                .Where(one => one.ProcessingStatus == LineProcessing.NeedReview && one.JobKey != "")
                .OrderByDescending(one => one.ReceivedAt)
                .Take(300)
                .Select(one => new
                {
                    one.Id, one.JobKey, one.LineGroupId, one.MessageType, one.RawText, one.ReceivedAt,
                    one.ParsedStatus, one.ErrorCode, one.ErrorMessage, one.ImageReading, one.ImageKey,
                })
                .ToListAsync(token);

            var keys = rows.Select(one => one.JobKey).Distinct().ToList();
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
                items = rows.Select(one =>
                {
                    var read = one.MessageType == "image" ? null : LineParser.Parse(one.RawText, one.ReceivedAt);
                    var category = categories.GetValueOrDefault(one.JobKey, "");
                    return new
                    {
                        one.Id, one.JobKey, one.ReceivedAt, one.ErrorCode,
                        detail = one.ErrorMessage,
                        kind = one.MessageType,
                        text = one.RawText,
                        reading = one.ImageReading,
                        hasImage = one.ImageKey.Length > 0,
                        group = names.GetValueOrDefault(one.LineGroupId, ""),
                        // What approving would write: the status as this job's
                        // ladder names it, the arrival clock, or the box.
                        to = one.MessageType == "image" ? one.ImageReading : LineAuthority.ResolveSite(category, one.ParsedStatus),
                        arrival = read is null ? new { date = "", time = "", atSend = false } : Arrival(read),
                        // The truck's details the message carries, for the drawer's line.
                        details = read is null ? "" : string.Join(" · ", new[]
                        {
                            read.Plates is { Count: > 0 } ? $"ทะเบียน {read.Plates[0]}" : "",
                            read.Driver is not null ? $"คนขับ {read.Driver}" : "",
                            read.Phone is not null ? $"เบอร์ {read.Phone}" : "",
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
            // What the chase would ask right now, so the screen can say so.
            var due = date is null ? await chase.DueAsync(DateTimeOffset.UtcNow, token) : [];
            return Results.Json(new
            {
                date = Formats.PlanDate(day),
                remindAt = reminders.RemindAtText,
                chaseMinutes = chase.Minutes,
                chaseEveryHours = chase.RepeatHours,
                chaseBeforeMinutes = chase.BeforeMinutes,
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

        /*
         * The photo a driver posted, streamed from the private files container
         * through the API — the same door the job documents go out of, for the
         * same reason: a URL that works without a sign-in ends up in an email.
         */
        group.MapGet("/events/{id:long}/image", async (long id, HttpContext context,
            IUserAccessor users, ScmosDbContext db, IFileStore files, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var key = await db.LineEvents.AsNoTracking()
                .Where(one => one.Id == id)
                .Select(one => one.ImageKey)
                .FirstOrDefaultAsync(token);
            if (string.IsNullOrEmpty(key)) return ApiResults.Error("ไม่มีรูปสำหรับข้อความนี้", StatusCodes.Status404NotFound);
            if (!files.Configured) return ApiResults.Error("ยังไม่ได้ตั้งค่าที่เก็บไฟล์", StatusCodes.Status503ServiceUnavailable);

            var stream = await files.OpenAsync(key, token);
            if (stream is null) return ApiResults.Error($"รูปหายจากที่เก็บ: {key}", StatusCodes.Status404NotFound);

            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Cache-Control"] = "private, max-age=3600";
            var type = key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            return Results.File(stream, type);
        });

        group.MapGet("/events/{id:long}/options", async (long id, HttpContext context,
            IUserAccessor users, ScmosDbContext db, JobsRepository jobs, DelegationService delegations,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var row = await db.LineEvents.AsNoTracking()
                .FirstOrDefaultAsync(one => one.Id == id, token);
            if (row is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);

            if (row.MessageType == "image") return await PhotoOptionsAsync(row, db, user, jobs, delegations, token);

            /*
             * The decision as it is now, not as it was when the worker looked.
             *
             * This is what the screen opens a row against, so that what an
             * operator is shown and what the approval will do are the same
             * answer to the same question, asked a second apart. Reading the
             * stored verdict here would let the screen offer a button the
             * approval then refuses.
             */
            var read = LineParser.Parse(row.RawText, row.ReceivedAt);
            var now = await LineMatching.DecideAsync(db, row.LineGroupId, read, row.ReceivedAt, token);

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
                    var candidate = new LineAuthority.JobCandidate(job.Key, job.Cat, "", job.Status);
                    var move = string.IsNullOrWhiteSpace(read.Status) && read.HasDetails
                        ? LineAuthority.Details(candidate)
                        : LineAuthority.Move(candidate, read.Status);
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
            DelegationService delegations, AuditService audit, ILineNotifier notifier, IConfiguration config,
            ILoggerFactory logs, CancellationToken token) =>
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

            if (row.MessageType == "image")
                return await ApplyPhotoAsync(row, body, user, db, jobs, delegations, audit, notifier, config, logs, token);

            /*
             * Worked out again, now. Not read off the row.
             *
             * The verdict stored on the row was true when the worker looked. In
             * between, somebody may have delivered the job, cancelled it, or
             * moved it to another haulier — and this endpoint is the one that
             * writes, so it is the one that has to be right.
             */
            var read = LineParser.Parse(row.RawText, row.ReceivedAt);
            var decision = await LineMatching.DecideAsync(db, row.LineGroupId, read, row.ReceivedAt, token);

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

            var candidate = new LineAuthority.JobCandidate(one.Key, one.Cat, one.Trucker, one.Status);
            var final = string.IsNullOrWhiteSpace(read.Status) && read.HasDetails
                ? LineAuthority.Details(candidate)
                : LineAuthority.Move(candidate, read.Status);
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
            var why = body.Reason ?? $"LINE: {row.RawText}";
            if (final.To.Length > 0)
                await audit.RecordAsync(user, AuditActions.StatusChange, "job", chosen,
                    row.JobNumber, "status", final.From, final.To, why, token, EventSource.Line);
            if (stamp.Date is not null)
            {
                await audit.RecordAsync(user, AuditActions.Update, "job", chosen,
                    row.JobNumber, "arrDate", stamp.HadDate, stamp.Date, why, token, EventSource.Line);
                await audit.RecordAsync(user, AuditActions.Update, "job", chosen,
                    row.JobNumber, "arrTime", stamp.HadTime, stamp.Time!, why, token, EventSource.Line);
            }
            foreach (var (name, value) in truck.Fields)
                await audit.RecordAsync(user, AuditActions.Update, "job", chosen,
                    row.JobNumber, name, "", value, why, token, EventSource.Line);

            row.ProcessingStatus = LineProcessing.Processed;
            row.JobKey = chosen;
            row.ErrorCode = "";
            row.ErrorMessage = "";
            row.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);

            // The room hears that it was written — the one message a driver
            // actually waits for. A push: the reply token is long gone.
            var wroteWhat = string.Join(" · ", new[]
            {
                final.To.Length > 0 ? final.To : "",
                stamp.Date is not null ? $"ถึง {stamp.Time}" : "",
                truck.Written,
            }.Where(part => part.Length > 0));
            await TellRoomAsync(db, row, LineReply.ForApproval(LineReply.Reference(read), wroteWhat), notifier, config, logs, token);

            return Results.Json(new
            {
                message = (final.To.Length > 0 ? $"อัปเดต {chosen} เป็น {final.To} แล้ว" : $"บันทึกลงงาน {chosen} แล้ว")
                    + (stamp.Date is not null ? $" · เวลาถึง {stamp.Date} {stamp.Time}" : "")
                    + (truck.Fields.Count > 0 ? " · " + truck.Written : ""),
                jobKey = chosen, from = final.From, to = final.To,
                arrival = stamp.Note,
            });
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

    /* ------------------------------------------------------ a photograph */

    /// <summary>
    /// What a photo's container would be written into, right now — the same
    /// question the worker asked, asked again at the moment a person looks.
    /// </summary>
    private static async Task<IResult> PhotoOptionsAsync(LineEvent row, ScmosDbContext db, AppUser user,
        JobsRepository jobs, DelegationService delegations, CancellationToken token)
    {
        var numbers = await PhotoNumbersAsync(row, db, token);
        var container = numbers.Length == 1 ? numbers[0] : "";
        var now = container.Length > 0
            ? await LineMatching.DecideContainerAsync(db, row.LineGroupId, container, row.ReceivedAt, token)
            : new LineAuthority.LineDecision(
                numbers.Length > 1 ? "many-containers" : row.ErrorCode, [], "", "",
                numbers.Length > 1 ? $"อ่านได้ {numbers.Length} ตู้ — บันทึกทีละงานในตารางงาน" : row.ImageNote);

        var keys = now.Keys.ToList();
        var offered = await db.OperationJobs.AsNoTracking()
            .Where(job => keys.Contains(job.Key))
            .Select(job => new { job.Key, job.Cat, job.Customer, job.Container, job.Status, job.WorkDate })
            .ToListAsync(token);

        return Results.Json(new
        {
            kind = "image",
            outcome = now.Result,
            detail = now.Detail,
            canApply = now.Applies,
            from = now.From,
            to = now.To,
            reference = new { jobNumber = "", container, plates = Array.Empty<string>() },
            arrival = new { date = "", time = "" },
            imageReading = row.ImageReading,
            imageNote = row.ImageNote,
            hasImage = row.ImageKey.Length > 0,
            options = await Task.WhenAll(offered.Select(async job => new
            {
                job.Key, job.Cat, job.Customer, job.Container, job.Status, job.WorkDate,
                // Every offered row is one the rule found waiting for a number;
                // the choice is which, and each may take it.
                move = new { Result = LineAuthority.Outcome.Ok, Detail = "", ok = now.Keys.Contains(job.Key), to = container },
                arrival = "",
                mayApprove = await MayActOnAsync(user, job.Key, jobs, delegations, token),
            })),
            stored = row.ErrorCode,
        });
    }

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
    /// Writes the photographed container into the chosen job — one the rule
    /// offered, never an arbitrary key — and files the photo as done.
    /// </summary>
    private static async Task<IResult> ApplyPhotoAsync(LineEvent row, ApplyBody body, AppUser user,
        ScmosDbContext db, JobsRepository jobs, DelegationService delegations, AuditService audit, ILineNotifier notifier,
        IConfiguration config, ILoggerFactory logs, CancellationToken token)
    {
        var numbers = await PhotoNumbersAsync(row, db, token);
        if (numbers.Length != 1)
            return ApiResults.Error(numbers.Length == 0 ? "รูปนี้ไม่มีเลขตู้ที่อ่านได้" : "รูปนี้มีหลายตู้ — บันทึกในตารางงานเอง",
                StatusCodes.Status409Conflict);
        var container = numbers[0];

        var decision = await LineMatching.DecideContainerAsync(db, row.LineGroupId, container, row.ReceivedAt, token);
        var chosen = (body.JobKey ?? "").Trim();
        if (chosen.Length > 0)
        {
            if (!decision.Keys.Contains(chosen) || decision.Result == LineAuthority.Outcome.AlreadyThere)
                return ApiResults.Error("งานที่เลือกไม่อยู่ในรายการที่รูปนี้เขียนลงได้", StatusCodes.Status400BadRequest);
        }
        else if (decision.Applies)
        {
            chosen = decision.Keys[0];
        }
        if (chosen.Length == 0)
            return ApiResults.Error(decision.Detail.Length > 0 ? decision.Detail : decision.Result,
                StatusCodes.Status409Conflict);
        if (!await MayActOnAsync(user, chosen, jobs, delegations, token))
            return ApiResults.Error("อนุมัติได้เฉพาะงานของตัวเอง หรืองานที่ดูแลแทนอยู่", StatusCodes.Status403Forbidden);

        // Read once more at the moment of writing: the row must still be
        // empty. Somebody keying the number on the grid a second earlier wins.
        var had = await db.OperationJobs.AsNoTracking()
            .Where(job => job.Key == chosen)
            .Select(job => job.Container)
            .FirstOrDefaultAsync(token);
        if (had is null) return ApiResults.Error("ไม่พบงานนี้แล้ว", StatusCodes.Status409Conflict);
        if (Formats.Clean(had).Length > 0)
            return ApiResults.Error($"งานนี้มีเลขตู้ {had} แล้ว", StatusCodes.Status409Conflict);

        var wrote = await jobs.PatchAsync(chosen,
            new Dictionary<string, string> { ["container"] = container }, user.Signature, token);
        if (!wrote) return ApiResults.Error("บันทึกไม่สำเร็จ", StatusCodes.Status409Conflict);

        await audit.RecordAsync(user, AuditActions.Update, "job", chosen,
            container, "container", "", container,
            body.Reason ?? $"LINE: รูปตู้ ({row.ImageNote})", token, EventSource.Line);

        row.ProcessingStatus = LineProcessing.Processed;
        row.JobKey = chosen;
        row.ErrorCode = "";
        row.ErrorMessage = "";
        // A number vouched for after the photo was read is written onto the
        // row now, so the queue says what was applied.
        row.ImageReading = container;
        row.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        await TellRoomAsync(db, row, LineReply.ForApproval($"เลขตู้ {container}", ""), notifier, config, logs, token);

        return Results.Json(new
        {
            message = $"บันทึกเลขตู้ {container} ลงงาน {chosen} แล้ว",
            jobKey = chosen, from = "", to = container, arrival = "",
        });
    }

    /// <summary>
    /// The numbers a photo stands for, right now: the ones it was read with,
    /// or — when the check digit refused every one — the refused ones that
    /// the register or the haulier's typed messages have since vouched for.
    /// Asked at the moment a person looks, because the vouching message may
    /// have arrived after the photo did.
    /// </summary>
    private static async Task<string[]> PhotoNumbersAsync(LineEvent row, ScmosDbContext db, CancellationToken token)
    {
        var numbers = row.ImageReading.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length > 0) return numbers;
        var vouched = await LineMatching.VouchedAsync(
            db, row.LineGroupId, LineImageReading.RejectedIn(row.ImageNote), row.ReceivedAt, token);
        return [.. vouched];
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
            foreach (var name in new[] { "licence", "driver", "contact" })
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

        var note = written.Count > 0 ? "จะบันทึก " + string.Join(" · ", written) : "";
        if (notes.Count > 0) note = string.Join(" · ", new[] { note }.Concat(notes).Where(one => one.Length > 0));
        return (fields, note, string.Join(" · ", written));
    }

    /// <summary>
    /// A line into the room a message came from, after a person acted on it.
    /// Only a bound room, only while replies are on; a failure is logged and
    /// never fails the approval that was already written.
    /// </summary>
    private static async Task TellRoomAsync(ScmosDbContext db, LineEvent row, string text, ILineNotifier notifier,
        IConfiguration config, ILoggerFactory logs, CancellationToken token)
    {
        if (row.LineGroupId.Length == 0 || !notifier.Configured) return;
        if (string.Equals((config[LineEventWorker.RepliesKey] ?? "").Trim(), "off", StringComparison.OrdinalIgnoreCase)) return;
        var bound = await db.LineGroups.AsNoTracking()
            .AnyAsync(one => one.LineGroupId == row.LineGroupId && one.IsActive && one.GroupType == LineGroupType.Vendor && one.SupplierId > 0, token);
        if (!bound) return;
        try
        {
            await notifier.PushAsync(row.LineGroupId, [text], token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logs.CreateLogger("Line.Reply").LogWarning(error, "LINE approval reply for {Id} threw", row.Id);
        }
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
    private static (string? Date, string? Time, string HadDate, string HadTime, string Note) ArrivalWrite(
        LineParser.Parsed read, string data)
    {
        if (read.ArrivalTime is not { } at) return (null, null, "", "", "");

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

        var basis = read.ArrivalAtSend ? " (เวลาที่ส่งข้อความ — ไม่มีเวลาในข้อความ)" : "";
        if (hadDate.Length == 0 && hadTime.Length == 0)
            return (date, time, hadDate, hadTime, $"จะบันทึกเวลาถึง {date} {time}{basis}");
        if (hadDate == date && hadTime == time)
            return (null, null, hadDate, hadTime, $"เวลาถึง {date} {time} ตรงกับที่บันทึกไว้แล้ว");
        return (null, null, hadDate, hadTime,
            $"งานมีเวลาถึง {hadDate} {hadTime} อยู่แล้ว — ข้อความแจ้ง {date} {time}; แก้ในตารางงานถ้าต้องการ");
    }

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
