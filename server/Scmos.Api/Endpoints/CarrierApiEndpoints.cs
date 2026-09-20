using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The Carrier TMS API, V1 — <c>/api/carrier/v1/</c> — and the department's
/// side of it, <c>/api/carrier-api/clients</c>.
///
/// <para>
/// Phase 1 of the Carrier TMS Integration specification (20 Sep 2026):
/// reads only. A carrier's TMS presents a key, is told which supplier it
/// speaks for, and lists the assignments that supplier can see — the same
/// rows, from the same service, that the carrier's own person sees in the
/// portal (<see cref="CarrierService.ReadForAsync"/>). Nothing here reads a
/// carrier id from the request; the key's row says whose work it is, and a
/// job that is not this carrier's does not exist as far as the answer goes
/// (404, not 403 — a 403 would confirm the job is real).
/// </para>
///
/// <para>
/// The refusals are RFC 9457 Problem Details with a stable <c>type</c>
/// (<see cref="CarrierApi.ProblemOf"/>), every answer carries
/// <c>X-Correlation-Id</c>, and the group is rate-limited per key. The
/// existing carrier portal and its <c>{ "error": … }</c> shape are untouched:
/// this is a new door beside them, not a change to them.
/// </para>
///
/// <para>
/// Phase 2 (20 Sep 2026): the writes the portal already allows a carrier —
/// accept with the truck, decline with a reason — and the truck's details
/// into empty cells, each under an <c>Idempotency-Key</c>: a retry of the
/// same request is answered from the ledger (<c>carrier_api_requests</c>),
/// never run twice. The rows change through <see cref="CarrierService"/>,
/// the same code the portal's buttons call, audited under the key's own
/// name with source TMS.
/// </para>
/// </summary>
public static class CarrierApiEndpoints
{
    public const string RateLimitPolicy = "carrier-api";

    /// <summary>
    /// Where a carrier's TMS calls, for the screen to show beside a new key.
    /// Set it (<c>CarrierApi__BaseUrl</c>) when the API is reached under a
    /// name other than its own host; otherwise the host this request came in
    /// on — the API's own, since the web proxy calls it directly. Never the
    /// web host: its proxy does not pass the Authorization header on.
    /// </summary>
    public const string BaseUrlKey = "CarrierApi:BaseUrl";

    public static string BaseUrlOf(HttpContext context, IConfiguration config)
    {
        var set = (config[BaseUrlKey] ?? "").Trim();
        if (set.Length > 0) return set.EndsWith('/') ? set : set + "/";
        var host = context.Request.Host;
        var scheme = host.Host.Contains("localhost", StringComparison.OrdinalIgnoreCase) ? "http" : "https";
        return $"{scheme}://{host}/api/carrier/v1/";
    }

    public static void MapCarrierApi(this IEndpointRouteBuilder routes)
    {
        /* ------------------------------------------------ the carrier's door */

        var v1 = routes.MapGroup("/api/carrier/v1")
            .WithTags("Carrier API v1")
            .RequireRateLimiting(RateLimitPolicy)
            .AddEndpointFilter(AuthenticateAsync);

        v1.MapGet("/me", (HttpContext context) =>
        {
            var who = PrincipalOf(context);
            return Results.Json(new
            {
                clientId = who.ClientId,
                clientName = who.ClientName,
                supplier = new { id = who.Company.Id, code = who.Company.Code, name = who.Company.Name },
                limits = new
                {
                    requestsPerMinute = CarrierApi.RequestsPerMinute,
                    maxPageSize = CarrierApi.MaxPageSize,
                    maxWindowDays = CarrierApi.MaxWindowDays,
                },
                correlationId = context.TraceIdentifier,
            });
        });

        // The assignments this supplier can see: offered (a request waiting
        // for its answer) and accepted (the register names it as the carrier),
        // inside a work-date window, paged.
        v1.MapGet("/assignments", async (string? status, string? from, string? to, int? page, int? pageSize,
            HttpContext context, CarrierService carriers, CancellationToken token) =>
        {
            var who = PrincipalOf(context);
            if (!CarrierApi.TryGroup(status, out var group))
                return Refuse(context, CarrierApi.Invalid, $"status must be '{CarrierApi.Offered}' or '{CarrierApi.Accepted}'");
            var window = CarrierApi.Window(from, to, Today());
            if (window is null)
                return Refuse(context, CarrierApi.Invalid,
                    $"from/to must be yyyy-MM-dd or dd/MM/yyyy, to on or after from, at most {CarrierApi.MaxWindowDays} days apart");
            var (pageNo, size) = CarrierApi.Page(page, pageSize);

            var portal = await carriers.ReadForAsync(who.Company, token);
            var all = Assignments(portal)
                .Where(one => group is null || one.Group == group)
                .Where(one => CarrierApi.InWindow(one.DateText, window.Value.From, window.Value.To))
                .OrderBy(one => Formats.DateNumber(one.DateText))
                .ThenBy(one => one.JobCode, StringComparer.Ordinal)
                .ToList();
            var items = all.Skip((pageNo - 1) * size).Take(size).ToList();
            return Results.Json(new
            {
                items,
                page = pageNo,
                pageSize = size,
                total = all.Count,
                window = new { from = window.Value.From.ToString("yyyy-MM-dd"), to = window.Value.To.ToString("yyyy-MM-dd") },
                correlationId = context.TraceIdentifier,
            });
        });

        v1.MapGet("/assignments/{jobKey}", async (string jobKey, HttpContext context, CarrierService carriers,
            CancellationToken token) =>
        {
            var who = PrincipalOf(context);
            var portal = await carriers.ReadForAsync(who.Company, token);
            var one = Assignments(portal).FirstOrDefault(row => string.Equals(row.Id, jobKey, StringComparison.Ordinal));
            return one is null
                ? Refuse(context, CarrierApi.NotFound, "No assignment with this id is offered to or held by this carrier")
                : Results.Json(new { item = one, correlationId = context.TraceIdentifier });
        });

        /* --------------------------------------------- the writes (phase 2) */

        // Accepting: the request waiting for this carrier is confirmed, the
        // truck goes on the job, the other carriers' open requests close.
        v1.MapPost("/assignments/{jobKey}/accept", async (string jobKey, [FromBody] TruckBody? body,
            HttpContext context, CarrierService carriers, AuditService audit, CancellationToken token) =>
            await IdempotentAsync(context, "POST", body, async () =>
            {
                var who = PrincipalOf(context);
                var (truck, problems, warnings) = CarrierApi.ReadTruck(body?.Licence, body?.Driver, body?.Contact, body?.Container, body?.Seal, requireTruck: true);
                if (problems.Count > 0) return Problem(context, CarrierApi.Invalid, string.Join("; ", problems), new { problems });

                var result = await carriers.AcceptForAsync(who.Company, jobKey, truck.Licence, truck.Driver, truck.Contact,
                    truck.Container, truck.Seal, who.AsUser().Signature, token);
                if (!result.Ok) return RefusedWrite(context, result, jobKey, await GroupOfAsync(carriers, who, jobKey, token));

                var by = who.AsUser();
                await audit.RecordAsync(by, AuditActions.Update, "job", jobKey, jobKey,
                    "trucker, licence, driver, contact", result.Before,
                    $"{truck.Licence} · {truck.Driver} · {truck.Contact}",
                    $"ผู้รับเหมายืนยันรับงานและแจ้งรถผ่าน Carrier API ({who.ClientId})", token, EventSource.CarrierApi);
                foreach (var name in new[] { "container", "seal" })
                {
                    if (result.Written is { } written && written.TryGetValue(name, out var value))
                        await audit.RecordAsync(by, AuditActions.Update, "job", jobKey, jobKey, name,
                            result.Previous?.GetValueOrDefault(name, "") ?? "", value,
                            $"Carrier API ({who.ClientId})", token, EventSource.CarrierApi);
                }
                return Answer(200, new
                {
                    message = result.Message,
                    jobKey,
                    status = JobStatus.SupplierConfirmed,
                    written = result.Written,
                    skipped = result.Skipped ?? [],
                    warnings,
                    correlationId = context.TraceIdentifier,
                });
            }));

        // Declining: the request is answered no; who is asked next stays the
        // operator's decision.
        v1.MapPost("/assignments/{jobKey}/decline", async (string jobKey, [FromBody] DeclineBody? body,
            HttpContext context, CarrierService carriers, AuditService audit, CancellationToken token) =>
            await IdempotentAsync(context, "POST", body, async () =>
            {
                var who = PrincipalOf(context);
                var reason = Formats.Clean(body?.Reason);
                if (reason.Length == 0 || reason.Length > 400)
                    return Problem(context, CarrierApi.Invalid, "reason is required (at most 400 characters)");

                var result = await carriers.DeclineForAsync(who.Company, jobKey, reason, token);
                if (!result.Ok) return RefusedWrite(context, result, jobKey, await GroupOfAsync(carriers, who, jobKey, token));

                await audit.RecordAsync(who.AsUser(), AuditActions.Update, "job", jobKey, jobKey,
                    "supplier-response", "pending", "rejected", $"{reason} — Carrier API ({who.ClientId})", token, EventSource.CarrierApi);
                return Answer(200, new { message = result.Message, jobKey, correlationId = context.TraceIdentifier });
            }));

        // The truck's details after acceptance — a plate, a driver, a
        // number, the box, the seal — into empty cells only. A cell the
        // department keyed differently is a conflict and nothing is written.
        v1.MapPut("/assignments/{jobKey}/truck", async (string jobKey, [FromBody] TruckBody? body,
            HttpContext context, CarrierService carriers, AuditService audit, CancellationToken token) =>
            await IdempotentAsync(context, "PUT", body, async () =>
            {
                var who = PrincipalOf(context);
                var (truck, problems, warnings) = CarrierApi.ReadTruck(body?.Licence, body?.Driver, body?.Contact, body?.Container, body?.Seal, requireTruck: false);
                if (problems.Count > 0) return Problem(context, CarrierApi.Invalid, string.Join("; ", problems), new { problems });
                if (truck.IsEmpty) return Problem(context, CarrierApi.Invalid, "send at least one of licence, driver, contact, container, seal");

                var result = await carriers.UpdateTruckForAsync(who.Company, jobKey, truck.Licence, truck.Driver, truck.Contact,
                    truck.Container, truck.Seal, who.AsUser().Signature, token);
                if (!result.Ok) return RefusedWrite(context, result, jobKey, await GroupOfAsync(carriers, who, jobKey, token));

                var by = who.AsUser();
                foreach (var (name, value) in result.Written ?? new Dictionary<string, string>())
                    await audit.RecordAsync(by, AuditActions.Update, "job", jobKey, jobKey, name,
                        result.Previous?.GetValueOrDefault(name, "") ?? "", value,
                        $"Carrier API ({who.ClientId})", token, EventSource.CarrierApi);
                return Answer(200, new
                {
                    message = result.Message,
                    jobKey,
                    written = result.Written ?? new Dictionary<string, string>(),
                    skipped = result.Skipped ?? [],
                    warnings,
                    correlationId = context.TraceIdentifier,
                });
            }));

        /* ---------------------------------------------- the department's side */

        // Keys are issued and retired by whoever manages suppliers — the same
        // hand that binds a LINE room — and the key is shown exactly once.
        var admin = routes.MapGroup("/api/carrier-api/clients").WithTags("Carrier API clients");

        admin.MapGet("", async (HttpContext context, IUserAccessor users, ScmosDbContext db, IConfiguration config,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("ทำได้เฉพาะผู้ที่ดูแลผู้ขนส่ง", StatusCodes.Status403Forbidden);

            var rows = await db.CarrierApiClients.AsNoTracking().OrderByDescending(one => one.CreatedAt).ToListAsync(token);
            var ids = rows.Select(one => one.SupplierId).Distinct().ToList();
            var names = await db.Suppliers.AsNoTracking().Where(one => ids.Contains(one.Id))
                .ToDictionaryAsync(one => one.Id, one => one.Name, token);
            return Results.Json(new
            {
                baseUrl = BaseUrlOf(context, config),
                requestsPerMinute = CarrierApi.RequestsPerMinute,
                clients = rows.Select(one => new
                {
                    one.Id, one.ClientId, one.Name, one.SupplierId,
                    supplier = names.GetValueOrDefault(one.SupplierId, ""),
                    one.KeyPrefix, one.Status, one.CreatedAt, one.CreatedBy, one.RevokedAt, one.RevokedBy, one.LastSeenAt,
                }),
            });
        });

        admin.MapPost("", async ([FromBody] NewClientBody body, HttpContext context, IUserAccessor users,
            ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("ทำได้เฉพาะผู้ที่ดูแลผู้ขนส่ง", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.ManageSuppliers) is { } stop) return stop;

            var name = (body.Name ?? "").Trim();
            if (name.Length is 0 or > 120) return ApiResults.Error("ต้องตั้งชื่อคีย์ (ไม่เกิน 120 ตัวอักษร)", StatusCodes.Status400BadRequest);
            var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(one => one.Id == body.SupplierId, token);
            if (supplier is null) return ApiResults.Error("ไม่พบผู้ขนส่งรายนี้", StatusCodes.Status404NotFound);

            // Generated here, hashed here, returned once. The plain key exists
            // in this method and in the answer to this call, and nowhere else.
            var key = CarrierApi.NewKey();
            var row = new CarrierApiClient
            {
                SupplierId = supplier.Id,
                Name = name,
                ClientId = CarrierApi.NewClientId(),
                KeyHash = CarrierApi.HashOf(key),
                KeyPrefix = CarrierApi.ShownPrefixOf(key),
                Status = CarrierApiClientStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = user.Signature,
            };
            db.CarrierApiClients.Add(row);
            await db.SaveChangesAsync(token);
            await audit.RecordAsync(user, AuditActions.Register, "carrier-api-client", row.ClientId, name,
                "supplier", "", supplier.Name, "ออกคีย์ Carrier API", token);

            return Results.Json(new
            {
                row.Id, row.ClientId, row.Name, row.KeyPrefix,
                supplier = supplier.Name,
                key,
                message = $"ออกคีย์ {row.ClientId} ให้ {supplier.Name} แล้ว — คีย์แสดงครั้งนี้ครั้งเดียว",
            });
        });

        admin.MapPost("/{id:long}/revoke", async (long id, [FromBody] RevokeBody body, HttpContext context,
            IUserAccessor users, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("ทำได้เฉพาะผู้ที่ดูแลผู้ขนส่ง", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.ManageSuppliers) is { } stop) return stop;

            var row = await db.CarrierApiClients.FirstOrDefaultAsync(one => one.Id == id, token);
            if (row is null) return ApiResults.Error("ไม่พบคีย์นี้", StatusCodes.Status404NotFound);
            if (row.Status == CarrierApiClientStatus.Revoked)
                return ApiResults.Error("คีย์นี้ถูกยกเลิกไปแล้ว", StatusCodes.Status409Conflict);

            // Retired, not removed: the audit rows that name this client id
            // keep pointing at a row, and a revoked key presented later is
            // logged as such rather than as a stranger.
            row.Status = CarrierApiClientStatus.Revoked;
            row.RevokedAt = DateTimeOffset.UtcNow;
            row.RevokedBy = user.Signature;
            await db.SaveChangesAsync(token);
            await audit.RecordAsync(user, AuditActions.Revoke, "carrier-api-client", row.ClientId, row.Name,
                "status", CarrierApiClientStatus.Active, CarrierApiClientStatus.Revoked,
                (body.Reason ?? "").Trim(), token);
            return Results.Json(new { message = $"ยกเลิกคีย์ {row.ClientId} แล้ว" });
        });
    }

    /* ---------------------------------------------------------- the filter */

    /// <summary>
    /// What every call through the carrier's door gets first: a correlation
    /// id (the caller's or a fresh one, on the response and on the request's
    /// trace so the audit trail carries it) and then the key. No key, or one
    /// the register does not hold, is one refusal with one wording.
    /// </summary>
    private static async ValueTask<object?> AuthenticateAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var correlation = CarrierApi.CorrelationId(context.Request.Headers[CarrierApi.CorrelationHeader]);
        context.TraceIdentifier = correlation;
        context.Response.Headers[CarrierApi.CorrelationHeader] = correlation;

        var auth = context.RequestServices.GetRequiredService<CarrierApiAuth>();
        var who = await auth.ResolveAsync(context.Request.Headers.Authorization, context.RequestAborted);
        if (who is null)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer realm=\"scmos-carrier-api\"";
            return Refuse(context, CarrierApi.Unauthorized, "Send the key as: Authorization: Bearer scmos_ck_…");
        }
        context.Items[CarrierApiAuth.ItemKey] = who;
        return await next(invocation);
    }

    private static CarrierApiAuth.Principal PrincipalOf(HttpContext context) =>
        context.Items[CarrierApiAuth.ItemKey] as CarrierApiAuth.Principal
        ?? throw new InvalidOperationException("Carrier API endpoint reached without the authentication filter");

    /* ------------------------------------------------- idempotent writes */

    /// <summary>What a write answers: the status, the media type and the body as text — the shape the ledger stores and replays.</summary>
    public sealed record Written(int Status, string ContentType, string Body);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static Written Answer(int status, object body) =>
        new(status, "application/json; charset=utf-8", JsonSerializer.Serialize(body, Web));

    /// <summary>A refusal in the problem shape, as text, so the ledger can store and replay it like any answer.</summary>
    private static Written Problem(HttpContext context, string code, string detail, object? more = null)
    {
        var problem = CarrierApi.ProblemOf(code);
        var document = new Dictionary<string, object?>
        {
            ["type"] = problem.Type,
            ["title"] = problem.Title,
            ["status"] = problem.Status,
            ["detail"] = detail,
            ["instance"] = context.Request.Path.Value,
            ["code"] = problem.Code,
            ["correlationId"] = context.TraceIdentifier,
        };
        if (more is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(more, Web).EnumerateObject())
                document[property.Name] = property.Value;
        }
        return new Written(problem.Status, "application/problem+json; charset=utf-8", JsonSerializer.Serialize(document, Web));
    }

    /// <summary>
    /// The service's refusal as the API's: no request or no such job for
    /// this carrier is 404 when the job is not in the carrier's lists at
    /// all (its existence is not confirmed) and 409 when it is — offered
    /// but already answered, held rather than offered, closed; a cell the
    /// register holds otherwise is 409 with the conflicts named.
    /// </summary>
    private static Written RefusedWrite(HttpContext context, CarrierService.Result result, string jobKey, string? group)
    {
        switch (result.Code)
        {
            case CarrierService.ResultCode.Invalid:
                return Problem(context, CarrierApi.Invalid, result.Message);
            case CarrierService.ResultCode.NotOffered:
            case CarrierService.ResultCode.NotHeld:
                return group is null
                    ? Problem(context, CarrierApi.NotFound, "No assignment with this id is offered to or held by this carrier")
                    : Problem(context, CarrierApi.Conflict, result.Code == CarrierService.ResultCode.NotOffered
                        ? $"The assignment is {group}, not waiting for this carrier's answer"
                        : $"The assignment is {group}, not held by this carrier", new { reason = result.Code, group });
            case CarrierService.ResultCode.Closed:
                return Problem(context, CarrierApi.Conflict, result.Message, new { reason = result.Code });
            case CarrierService.ResultCode.Conflict:
                return Problem(context, CarrierApi.Conflict, result.Message, new
                {
                    reason = result.Code,
                    conflicts = (result.Conflicts ?? []).Select(one => new { field = one.Field, current = one.Current, sent = one.Sent }),
                    skipped = result.Skipped ?? [],
                });
            default:
                return Problem(context, CarrierApi.Unavailable, result.Message.Length > 0 ? result.Message : "The change could not be saved");
        }
    }

    /// <summary>Which of the carrier's two lists a job is in, or null when it is in neither — for the 404 / 409 distinction.</summary>
    private static async Task<string?> GroupOfAsync(CarrierService carriers, CarrierApiAuth.Principal who, string jobKey, CancellationToken token)
    {
        var portal = await carriers.ReadForAsync(who.Company, token);
        return Assignments(portal).FirstOrDefault(row => string.Equals(row.Id, jobKey, StringComparison.Ordinal))?.Group;
    }

    /// <summary>
    /// Runs a write once per Idempotency-Key. The key is required; a
    /// request with the same key and the same method, path and body is
    /// answered from the ledger with <c>Idempotent-Replayed: true</c>; the
    /// same key with a different request is refused (422); a request whose
    /// first run is still in flight is refused (409) rather than run twice.
    /// The ledger row is claimed before the work, under the database's
    /// unique index, so two retries racing cannot both be first.
    /// </summary>
    private static async Task<IResult> IdempotentAsync(HttpContext context, string method, object? body, Func<Task<Written>> work)
    {
        var who = PrincipalOf(context);
        var db = context.RequestServices.GetRequiredService<ScmosDbContext>();
        var token = context.RequestAborted;

        var key = CarrierApi.IdempotencyKeyOf(context.Request.Headers[CarrierApi.IdempotencyHeader]);
        if (key is null)
            return Refuse(context, CarrierApi.Invalid, $"{CarrierApi.IdempotencyHeader} header is required: 1–128 printable characters, unique per request");
        var hash = CarrierApi.RequestHash(method, context.Request.Path.Value ?? "", body is null ? "" : JsonSerializer.Serialize(body, Web));

        var existing = await db.CarrierApiRequests
            .FirstOrDefaultAsync(one => one.ClientRowId == who.ClientRowId && one.IdempotencyKey == key, token);
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
                return Refuse(context, CarrierApi.KeyReused, "This Idempotency-Key was used for a different request; use a new key for a new request");
            if (existing.ResponseStatus == 0)
                return Refuse(context, CarrierApi.InProgress, "The same request is still being processed; retry in a moment");
            context.Response.Headers[CarrierApi.ReplayedHeader] = "true";
            return Results.Content(existing.ResponseBody, existing.ResponseContentType, statusCode: existing.ResponseStatus);
        }

        // Claim the key. Under the unique index a second claim fails, and
        // that failure is the in-progress answer.
        var row = new CarrierApiRequest
        {
            ClientRowId = who.ClientRowId, IdempotencyKey = key, RequestHash = hash, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.CarrierApiRequests.Add(row);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            return Refuse(context, CarrierApi.InProgress, "The same request is still being processed; retry in a moment");
        }

        // The ledger is swept as it is written to: this client's rows older
        // than the retention go, so the table stays the size of a month.
        var stale = DateTimeOffset.UtcNow.AddDays(-CarrierApi.IdempotencyDays);
        await db.CarrierApiRequests
            .Where(one => one.ClientRowId == who.ClientRowId && one.CreatedAt < stale)
            .ExecuteDeleteAsync(token);

        Written answer;
        try
        {
            answer = await work();
        }
        catch
        {
            // A failed run leaves no claim behind: the retry runs it again.
            db.CarrierApiRequests.Remove(row);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        row.ResponseStatus = answer.Status;
        row.ResponseContentType = answer.ContentType;
        row.ResponseBody = answer.Body;
        row.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        return Results.Content(answer.Body, answer.ContentType, statusCode: answer.Status);
    }

    /// <summary>A refusal as RFC 9457 Problem Details, with the stable code and the correlation id — never a stack trace.</summary>
    public static IResult Refuse(HttpContext context, string code, string detail)
    {
        var problem = CarrierApi.ProblemOf(code);
        return Results.Problem(
            detail: detail,
            instance: context.Request.Path,
            statusCode: problem.Status,
            title: problem.Title,
            type: problem.Type,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = problem.Code,
                ["correlationId"] = context.TraceIdentifier,
            });
    }

    /// <summary>The problem document the rate limiter writes when a bucket is empty — the same shape as every other refusal.</summary>
    public static async Task WriteRateLimitedAsync(HttpContext context, CancellationToken token)
    {
        var correlation = CarrierApi.CorrelationId(context.Request.Headers[CarrierApi.CorrelationHeader]);
        var problem = CarrierApi.ProblemOf(CarrierApi.RateLimited);
        context.Response.StatusCode = problem.Status;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers[CarrierApi.CorrelationHeader] = correlation;
        context.Response.Headers.RetryAfter = "60";
        await context.Response.WriteAsJsonAsync(new
        {
            type = problem.Type,
            title = problem.Title,
            status = problem.Status,
            detail = $"At most {CarrierApi.RequestsPerMinute} requests a minute with a key; try again in a minute",
            instance = context.Request.Path.Value,
            code = problem.Code,
            correlationId = correlation,
        }, token);
    }

    private static DateOnly Today() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    /* ------------------------------------------------------- the contract */

    /// <summary>
    /// One assignment as the API describes it — the carrier's view of a job
    /// and, when there is one, the request that offered it. The id is the
    /// job's register key: the portal's accept and decline routes, the LINE
    /// approvals and the audit trail all name a job by it. Nothing
    /// commercial beyond the request's own quoted price, which the portal
    /// already shows the carrier.
    /// </summary>
    public sealed record Assignment(
        string Id, string Group, string JobKey, string JobCode, string Category, string Booking,
        string Customer, string Container, string Type, string Weight,
        string? Date, string DateText, string PlanTime, string PickupPlan,
        string Destination, string Plant, string ReturnLoc, string CyYard,
        string Status, Truck Truck, string Seal, Arrival? Arrival, Request? Request);

    public sealed record Truck(string Licence, string Driver, string Contact);
    public sealed record Arrival(string? Date, string DateText, string Time);
    public sealed record Request(long Id, int? QuotedPrice, DateTimeOffset? RequestedAt, DateTimeOffset? RespondedAt);

    /// <summary>The portal's two lists as one, each row saying which it came from.</summary>
    public static IEnumerable<Assignment> Assignments(CarrierService.Portal portal) =>
        portal.Offered.Select(one => Describe(one, CarrierApi.Offered))
            .Concat(portal.Accepted.Select(one => Describe(one, CarrierApi.Accepted)));

    public static Assignment Describe(CarrierService.CarrierJob job, string group) => new(
        Id: job.Key, Group: group, JobKey: job.Key, JobCode: job.JobCode, Category: job.Category, Booking: job.Booking,
        Customer: job.Customer, Container: job.Container, Type: job.Type, Weight: job.Weight,
        Date: Iso(job.Date), DateText: job.Date, PlanTime: job.PlanTime, PickupPlan: job.PickupPlan,
        Destination: job.Destination, Plant: job.Plant, ReturnLoc: job.ReturnLoc, CyYard: job.CyYard,
        Status: job.Status,
        Truck: new Truck(job.Licence, job.Driver, job.Contact),
        Seal: job.Seal,
        Arrival: Formats.Clean(job.ArrDate).Length > 0 || Formats.Clean(job.ArrTime).Length > 0
            ? new Arrival(Iso(job.ArrDate), job.ArrDate, job.ArrTime)
            : null,
        Request: job.RequestId is { } id ? new Request(id, job.QuotedPrice, job.RequestedAt, job.RespondedAt) : null);

    /// <summary>A register date as yyyy-MM-dd, or null when the cell is not a readable date; the text is always beside it.</summary>
    private static string? Iso(string? cell) =>
        CarrierApi.ReadDay(cell ?? "") is { } day ? day.ToString("yyyy-MM-dd") : null;

    public record NewClientBody(int SupplierId, string? Name);
    public record RevokeBody(string? Reason);

    /// <summary>The truck a carrier sends: on acceptance the first three are required; the box and the seal are an export's.</summary>
    public record TruckBody(string? Licence, string? Driver, string? Contact, string? Container, string? Seal);
    public record DeclineBody(string? Reason);
}
