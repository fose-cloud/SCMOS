using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Driver training, and whether it lets somebody run a customer's work.
///
/// Two kinds of caller write here. LESCHACO's own team maintains the whole
/// register; a carrier maintains their own drivers through their portal and
/// must not see anybody else's — the same boundary the carrier portal draws,
/// for the same reason.
/// </summary>
public static class TrainingEndpoints
{
    public record CourseBody(string? Code, string? Name, int? ValidMonths, string? Note);
    public record RequirementBody(string? Customer, int? CourseId, int? ValidMonths, bool? Mandatory, string? Note);
    public record DriverBody(string? Name, string? DriverIdNo, string? Phone, int? SupplierId, string? Note);
    public record RecordBody(
        int? DriverId, int? CourseId, string? Customer, string? TrainingDate, string? ExpiryDate,
        string? CertificateNo, string? Provider, string? Remark, long? DocumentId);
    public record VoidBody(string? Reason);
    public record OverrideBody(int? DriverId, string? Customer, string? JobKey, string? Reason);

    public static void MapTraining(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/training").WithTags("Training");

        /// The bands, so the screens colour by the same numbers the API judges by.
        group.MapGet("/rules", () => Results.Json(new
        {
            statuses = TrainingRules.All.Select(status => new
            {
                code = status,
                th = TrainingRules.Thai[status],
            }),
            alertDays = TrainingRules.AlertDays,
            bands = new
            {
                valid = "มากกว่า 60 วัน",
                attention = "31–60 วัน",
                expiringSoon = "1–30 วัน",
                expired = "หมดอายุแล้ว",
            },
        }));

        group.MapGet("/summary", async (string? customer, HttpContext context, IUserAccessor users,
            TrainingService training, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            return Results.Json(await training.SummaryAsync(customer ?? "", token));
        });

        group.MapGet("/drivers", async (string? q, int? supplierId, HttpContext context,
            IUserAccessor users, CarrierService carriers, ScmosDbContext db, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var scope = await ScopeOfAsync(user, carriers, token);
            var query = db.Drivers.AsNoTracking().Where(driver => driver.Active);

            // A carrier sees their own drivers and nobody else's.
            if (scope is { } onlySupplier) query = query.Where(driver => driver.SupplierId == onlySupplier);
            else if (supplierId is { } wanted) query = query.Where(driver => driver.SupplierId == wanted);

            var search = (q ?? "").Trim();
            if (search.Length > 0)
            {
                query = query.Where(driver =>
                    driver.Name.Contains(search) || driver.DriverIdNo.Contains(search));
            }

            return Results.Json(await query.OrderBy(driver => driver.Name).Take(400).ToListAsync(token));
        });

        group.MapPost("/drivers", async ([FromBody] DriverBody body, HttpContext context,
            IUserAccessor users, CarrierService carriers, ScmosDbContext db, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!await MayWriteAsync(user, carriers, token))
                return ApiResults.Error("ไม่มีสิทธิ์เพิ่มคนขับ", StatusCodes.Status403Forbidden);

            var name = (body.Name ?? "").Trim();
            if (name.Length == 0) return ApiResults.Error("ต้องระบุชื่อคนขับ", StatusCodes.Status400BadRequest);

            var idNo = (body.DriverIdNo ?? "").Trim();
            if (idNo.Length > 0 && await db.Drivers.AnyAsync(d => d.DriverIdNo == idNo, token))
                return ApiResults.Error("เลขบัตร/ใบขับขี่นี้มีคนขับอยู่แล้ว", StatusCodes.Status400BadRequest);

            // A carrier's own portal can only add their own drivers; it does not
            // get to file somebody under another company.
            var scope = await ScopeOfAsync(user, carriers, token);

            var driver = new Driver
            {
                Name = name,
                DriverIdNo = idNo,
                Phone = (body.Phone ?? "").Trim(),
                SupplierId = scope ?? body.SupplierId,
                Note = (body.Note ?? "").Trim(),
                CreatedBy = user.Signature, CreatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = user.Signature, UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Drivers.Add(driver);
            await db.SaveChangesAsync(token);

            await audit.RecordAsync(user, AuditActions.Register, "driver", driver.Id.ToString(),
                name, "driver", "", idNo, body.Note ?? "", token);

            return Results.Json(new { message = $"เพิ่มคนขับ {name} แล้ว", id = driver.Id });
        });

        /// <summary>
        /// One form, one action: the driver, the certificate and the photograph.
        ///
        /// Split into three calls it would be three ways to end up half-entered
        /// — a driver with no training, a certificate with no photograph, a
        /// photograph uploaded against a driver the next call failed to create.
        /// The driver is found rather than duplicated when the licence number is
        /// already known, which is what stops the same person accumulating a row
        /// per course.
        /// </summary>
        group.MapPost("/entry", async (HttpContext context, IUserAccessor users,
            CarrierService carriers, ScmosDbContext db, TrainingService training,
            DocumentService documents, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!await MayWriteAsync(user, carriers, token))
                return ApiResults.Error("ไม่มีสิทธิ์บันทึกการอบรม", StatusCodes.Status403Forbidden);
            if (!context.Request.HasFormContentType)
                return ApiResults.Error("ต้องส่งเป็น multipart form", StatusCodes.Status415UnsupportedMediaType);

            var form = await context.Request.ReadFormAsync(token);
            string Text(string name) => form[name].ToString().Trim();

            var driverName = Text("driverName");
            if (driverName.Length == 0)
                return ApiResults.Error("ต้องระบุชื่อ-สกุลคนขับรถ", StatusCodes.Status400BadRequest);
            if (!int.TryParse(Text("courseId"), out var courseId) || courseId <= 0)
                return ApiResults.Error("ต้องเลือกหลักสูตร", StatusCodes.Status400BadRequest);

            var scope = await ScopeOfAsync(user, carriers, token);
            var supplierId = scope
                ?? (int.TryParse(Text("supplierId"), out var chosen) && chosen > 0 ? chosen : null);

            var idNo = Text("driverIdNo");

            // Found, not duplicated. Without the licence number there is nothing
            // reliable to match on — two drivers share a name far more often
            // than a number — so a nameless-number entry creates a new row and
            // says so rather than guessing which existing person was meant.
            var driver = idNo.Length > 0
                ? await db.Drivers.FirstOrDefaultAsync(d => d.DriverIdNo == idNo, token)
                : null;

            var createdDriver = driver is null;
            if (driver is null)
            {
                driver = new Driver
                {
                    Name = driverName, DriverIdNo = idNo, Phone = Text("phone"),
                    SupplierId = supplierId, Note = Text("note"),
                    CreatedBy = user.Signature, CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedBy = user.Signature, UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.Drivers.Add(driver);
                await db.SaveChangesAsync(token);
            }
            else if (scope is not null && driver.SupplierId != scope)
            {
                return ApiResults.Error("เลขบัตร/ใบขับขี่นี้เป็นของคนขับบริษัทอื่น",
                    StatusCodes.Status403Forbidden);
            }

            // The photograph, when one was attached. A failure here does not
            // lose the training record — the certificate is the point, the
            // picture is help.
            var photo = form.Files["photo"];
            if (photo is not null && photo.Length > 0)
            {
                var stored = await documents.AddToDriverAsync(driver.Id, "Other", "รูปพนักงานขับรถ",
                    "", "", photo, user, token);
                if (stored.Ok && stored.Document is not null)
                {
                    driver.PhotoDocumentId = stored.Document.Id;
                    driver.UpdatedBy = user.Signature;
                    driver.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(token);
                }
            }

            long? certificateId = null;
            var certificate = form.Files["certificate"];
            if (certificate is not null && certificate.Length > 0)
            {
                var stored = await documents.AddToDriverAsync(driver.Id, "Training", "ใบรับรองการอบรม",
                    Text("expiryDate"), Text("remark"), certificate, user, token);
                if (stored.Ok && stored.Document is not null) certificateId = stored.Document.Id;
            }

            var result = await training.RecordAsync(new DriverTraining
            {
                DriverId = driver.Id,
                CourseId = courseId,
                Customer = Text("customer"),
                TrainingDate = Text("trainingDate"),
                ExpiryDate = Text("expiryDate"),
                CertificateNo = Text("certificateNo"),
                Provider = Text("provider"),
                Remark = Text("remark"),
                DocumentId = certificateId,
            }, user, token);

            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Register, "driver-training",
                result.Id.ToString(), driverName, "training", "",
                $"{Text("trainingDate")} → {Text("expiryDate")}", Text("remark"), token);

            return Results.Json(new
            {
                message = createdDriver
                    ? $"เพิ่มคนขับ {driverName} และบันทึกการอบรมแล้ว"
                    : $"บันทึกการอบรมของ {driverName} แล้ว",
                driverId = driver.Id,
                id = result.Id,
                createdDriver,
            });
        }).DisableAntiforgery();

        group.MapGet("/drivers/{id:int}", async (int id, string? customer, HttpContext context,
            IUserAccessor users, CarrierService carriers, ScmosDbContext db, TrainingService training,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!await MaySeeDriverAsync(user, carriers, db, id, token))
                return ApiResults.Error("คนขับรายนี้ไม่ได้อยู่กับบริษัทของคุณ", StatusCodes.Status403Forbidden);

            var profile = await training.ProfileAsync(id, customer ?? "", token);
            return profile is null
                ? ApiResults.Error("ไม่พบคนขับรายนี้", StatusCodes.Status404NotFound)
                : Results.Json(profile);
        });

        /// The question the assignment screen asks before putting a driver on a job.
        group.MapGet("/eligibility", async (int driverId, string customer, HttpContext context,
            IUserAccessor users, TrainingService training, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var result = await training.CheckAsync(driverId, customer ?? "", token);
            return result is null
                ? ApiResults.Error("ไม่พบคนขับรายนี้", StatusCodes.Status404NotFound)
                : Results.Json(result);
        });

        /// The gate itself: what the assignment screen shows, and whether this
        /// person may go past it.
        group.MapGet("/gate", async (int driverId, string customer, HttpContext context,
            IUserAccessor users, TrainingService training, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var gate = await training.GateAsync(driverId, customer ?? "", user, token);
            return gate is null
                ? ApiResults.Error("ไม่พบคนขับรายนี้", StatusCodes.Status404NotFound)
                : Results.Json(gate);
        });

        /// Going ahead anyway. The record is the whole value of the exception:
        /// without it an override is indistinguishable from the control never
        /// having been there.
        group.MapPost("/override", async ([FromBody] OverrideBody body, HttpContext context,
            IUserAccessor users, TrainingService training, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageTraining))
                return ApiResults.Error(
                    "ข้ามข้อกำหนดการอบรมได้เฉพาะทีมที่ดูแลผู้รับเหมา ตั้งแต่ระดับปฏิบัติการขึ้นไป",
                    StatusCodes.Status403Forbidden);

            var reason = (body.Reason ?? "").Trim();
            if (reason.Length < 10)
                return ApiResults.Error("ต้องระบุเหตุผลอย่างน้อย 10 ตัวอักษร — เหตุผลนี้จะถูกบันทึกไว้",
                    StatusCodes.Status400BadRequest);
            if (body.DriverId is not { } driverId)
                return ApiResults.Error("ต้องระบุคนขับ", StatusCodes.Status400BadRequest);

            var gate = await training.GateAsync(driverId, body.Customer ?? "", user, token);
            if (gate is null) return ApiResults.Error("ไม่พบคนขับรายนี้", StatusCodes.Status404NotFound);
            if (!gate.Blocked)
                return ApiResults.Error("คนขับรายนี้ผ่านข้อกำหนดอยู่แล้ว ไม่ต้องข้าม",
                    StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "training-override",
                body.JobKey ?? driverId.ToString(),
                gate.Blocking.Count > 0 ? string.Join(", ", gate.Blocking.Select(b => b.Name)) : "",
                "training", "blocked", "allowed", reason, token);

            return Results.Json(new
            {
                message = "บันทึกการข้ามข้อกำหนดแล้ว — เหตุผลถูกเก็บไว้ในประวัติการใช้งาน",
                blocking = gate.Blocking.Select(b => b.Name),
            });
        });

        group.MapGet("/history/{driverId:int}", async (int driverId, HttpContext context,
            IUserAccessor users, CarrierService carriers, ScmosDbContext db, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!await MaySeeDriverAsync(user, carriers, db, driverId, token))
                return ApiResults.Error("คนขับรายนี้ไม่ได้อยู่กับบริษัทของคุณ", StatusCodes.Status403Forbidden);

            // Everything ever recorded, voided rows included — this is the view
            // an auditor asks for, and hiding the withdrawn ones would answer a
            // different question than the one they came with.
            var rows = await db.DriverTrainings.AsNoTracking()
                .Where(record => record.DriverId == driverId)
                .OrderByDescending(record => record.Id)
                .ToListAsync(token);

            return Results.Json(rows);
        });

        group.MapPost("/records", async ([FromBody] RecordBody body, HttpContext context,
            IUserAccessor users, CarrierService carriers, ScmosDbContext db, TrainingService training,
            AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!await MayWriteAsync(user, carriers, token))
                return ApiResults.Error("ไม่มีสิทธิ์บันทึกการอบรม", StatusCodes.Status403Forbidden);
            if (body.DriverId is not { } driverId)
                return ApiResults.Error("ต้องระบุคนขับ", StatusCodes.Status400BadRequest);
            if (!await MaySeeDriverAsync(user, carriers, db, driverId, token))
                return ApiResults.Error("คนขับรายนี้ไม่ได้อยู่กับบริษัทของคุณ", StatusCodes.Status403Forbidden);

            var result = await training.RecordAsync(new DriverTraining
            {
                DriverId = driverId,
                CourseId = body.CourseId ?? 0,
                Customer = (body.Customer ?? "").Trim(),
                TrainingDate = (body.TrainingDate ?? "").Trim(),
                ExpiryDate = (body.ExpiryDate ?? "").Trim(),
                CertificateNo = (body.CertificateNo ?? "").Trim(),
                Provider = (body.Provider ?? "").Trim(),
                Remark = (body.Remark ?? "").Trim(),
                DocumentId = body.DocumentId,
            }, user, token);

            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Register, "driver-training",
                result.Id.ToString(), body.CertificateNo ?? "", "training", "",
                $"{body.TrainingDate} → {body.ExpiryDate}", body.Remark ?? "", token);

            return Results.Json(new { message = result.Message, id = result.Id });
        });

        group.MapPost("/records/{id:long}/void", async (long id, [FromBody] VoidBody body,
            HttpContext context, IUserAccessor users, TrainingService training, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("ยกเลิกรายการอบรมได้เฉพาะผู้ดูแลผู้รับเหมา",
                    StatusCodes.Status403Forbidden);

            var result = await training.VoidAsync(id, body.Reason ?? "", user, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "driver-training", id.ToString(),
                "", "voided", "", "true", body.Reason ?? "", token);

            return Results.Json(new { message = result.Message });
        });

        /* ------------------------------------------------- courses and rules */

        group.MapGet("/courses", async (ScmosDbContext db, HttpContext context, IUserAccessor users,
            CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await db.TrainingCourses.AsNoTracking()
                .OrderBy(course => course.Name).ToListAsync(token));
        });

        group.MapPost("/courses", async ([FromBody] CourseBody body, HttpContext context,
            IUserAccessor users, ScmosDbContext db, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("เพิ่มหลักสูตรได้เฉพาะผู้ดูแลผู้รับเหมา", StatusCodes.Status403Forbidden);

            var code = (body.Code ?? "").Trim().ToUpperInvariant();
            var name = (body.Name ?? "").Trim();
            if (code.Length == 0 || name.Length == 0)
                return ApiResults.Error("ต้องระบุรหัสและชื่อหลักสูตร", StatusCodes.Status400BadRequest);
            if (await db.TrainingCourses.AnyAsync(course => course.Code == code, token))
                return ApiResults.Error("รหัสหลักสูตรนี้มีอยู่แล้ว", StatusCodes.Status400BadRequest);

            var course = new TrainingCourse
            {
                Code = code, Name = name,
                ValidMonths = Math.Max(1, body.ValidMonths ?? 12),
                Note = (body.Note ?? "").Trim(),
            };
            db.TrainingCourses.Add(course);
            await db.SaveChangesAsync(token);
            return Results.Json(new { message = $"เพิ่มหลักสูตร {name} แล้ว", id = course.Id });
        });

        group.MapGet("/requirements", async (string? customer, ScmosDbContext db, HttpContext context,
            IUserAccessor users, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var query = db.CustomerTrainingRequirements.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(customer))
                query = query.Where(requirement => requirement.Customer == customer);

            var requirements = await query.ToListAsync(token);
            var courses = await db.TrainingCourses.AsNoTracking().ToListAsync(token);

            return Results.Json(requirements.Select(requirement => new
            {
                requirement.Id,
                requirement.Customer,
                requirement.CourseId,
                course = courses.FirstOrDefault(course => course.Id == requirement.CourseId)?.Name ?? "",
                code = courses.FirstOrDefault(course => course.Id == requirement.CourseId)?.Code ?? "",
                validMonths = requirement.ValidMonths
                              ?? courses.FirstOrDefault(course => course.Id == requirement.CourseId)?.ValidMonths
                              ?? 12,
                requirement.Mandatory,
                requirement.Note,
            }).OrderBy(item => item.Customer).ThenBy(item => item.course));
        });

        group.MapPost("/requirements", async ([FromBody] RequirementBody body, HttpContext context,
            IUserAccessor users, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("กำหนดข้อบังคับได้เฉพาะผู้ดูแลผู้รับเหมา", StatusCodes.Status403Forbidden);

            var customer = (body.Customer ?? "").Trim();
            if (customer.Length == 0 || body.CourseId is not { } courseId)
                return ApiResults.Error("ต้องระบุลูกค้าและหลักสูตร", StatusCodes.Status400BadRequest);

            var existing = await db.CustomerTrainingRequirements
                .FirstOrDefaultAsync(r => r.Customer == customer && r.CourseId == courseId, token);

            if (existing is null)
            {
                db.CustomerTrainingRequirements.Add(new CustomerTrainingRequirement
                {
                    Customer = customer, CourseId = courseId,
                    ValidMonths = body.ValidMonths, Mandatory = body.Mandatory ?? true,
                    Note = (body.Note ?? "").Trim(),
                    UpdatedBy = user.Signature, UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                existing.ValidMonths = body.ValidMonths;
                existing.Mandatory = body.Mandatory ?? true;
                existing.Note = (body.Note ?? "").Trim();
                existing.UpdatedBy = user.Signature;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(token);
            await audit.RecordAsync(user, AuditActions.Update, "training-requirement", customer,
                customer, "requirement", "", courseId.ToString(), body.Note ?? "", token);

            return Results.Json(new { message = $"บันทึกข้อบังคับของ {customer} แล้ว" });
        });
    }

    /// <summary>The supplier a carrier account is confined to, or null for staff.</summary>
    private static async Task<int?> ScopeOfAsync(AppUser user, CarrierService carriers,
        CancellationToken token) =>
        (await carriers.CompanyOfAsync(user, token))?.Id;

    /// <summary>
    /// Staff with supplier management, or a carrier maintaining their own
    /// people. Everyone else reads only.
    /// </summary>
    private static async Task<bool> MayWriteAsync(AppUser user, CarrierService carriers,
        CancellationToken token) =>
        user.Can(Capability.ManageTraining) || await ScopeOfAsync(user, carriers, token) is not null;

    private static async Task<bool> MaySeeDriverAsync(AppUser user, CarrierService carriers,
        ScmosDbContext db, int driverId, CancellationToken token)
    {
        var scope = await ScopeOfAsync(user, carriers, token);
        if (scope is null) return true;

        var driver = await db.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == driverId, token);
        return driver?.SupplierId == scope;
    }
}
