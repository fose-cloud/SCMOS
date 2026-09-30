using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Carrier Billing Phase 9 — the portal's own operations through the Carrier
/// API v1 door: the registered fleet, a truck and driver from it, the
/// operational status up to Delivery Complete, and the job's history.
///
/// <para>
/// BR-016 of the Carrier Collaboration + Billing specification: the portal and
/// a carrier's TMS use the same business logic. So these routes call the
/// operations the portal's buttons call — <see cref="CarrierService.AssignResourcesForAsync"/>
/// and <see cref="CarrierService.AdvanceForAsync"/> — with the supplier the
/// key's row names; the ladder, the fleet-ownership check, the history row,
/// the audit and the Billing Case at Delivery Complete are theirs, not copies.
/// </para>
///
/// <para>
/// What differs is who may. A person in the portal writes directly; a key has
/// written directly only when the department trusts it (the mark on the key
/// and <c>CarrierApi__AutoApply=on</c>, since v2.7.57). The two writes below
/// keep that: an untrusted key is refused with 403 and pointed at the routes
/// the department governs — the truck into empty cells, and status events the
/// job's owner approves. See <see cref="CarrierApi.MayWriteDirectly"/>.
/// </para>
/// </summary>
public static class CarrierOperationsApiEndpoints
{
    /// <summary>A truck and a driver from this carrier's register, and a second truck as the trailer when there is one.</summary>
    public record ResourcesBody(int? TruckId, int? TrailerId, int? DriverId);

    /// <summary>A step on the portal's ladder — one of <see cref="CarrierOperations.Types"/> — when it happened, and a remark.</summary>
    public record StatusBody(string? Type, string? At, string? Remark);

    public static void Map(RouteGroupBuilder v1)
    {
        // The carrier's own active trucks and drivers — the ids the resources route takes.
        v1.MapGet("/fleet", async (HttpContext context, ScmosDbContext db, CancellationToken token) =>
        {
            var who = CarrierApiEndpoints.PrincipalOf(context);
            var trucks = await db.SupplierTrucks.AsNoTracking()
                .Where(row => row.SupplierId == who.Company.Id && row.Status == "active")
                .OrderBy(row => row.Plate)
                .Select(row => new { row.Id, row.Plate, row.Kind, row.VehicleType, row.DgCapable, row.RegistrationExpiry })
                .ToListAsync(token);
            var drivers = await db.SupplierDrivers.AsNoTracking()
                .Where(row => row.SupplierId == who.Company.Id && row.Status == "active")
                .OrderBy(row => row.Name)
                .Select(row => new { row.Id, row.Name, row.Phone, row.LicenceExpiry, row.TrainingExpiry })
                .ToListAsync(token);
            return Results.Json(new { trucks, drivers, correlationId = context.TraceIdentifier });
        });

        // The portal's resource assignment: registered ids, owned by this carrier, over what the job holds.
        v1.MapPut("/assignments/{jobKey}/resources", async (string jobKey, [FromBody] ResourcesBody? body,
            HttpContext context, IConfiguration config, CarrierService carriers, CancellationToken token) =>
            await CarrierApiEndpoints.IdempotentAsync(context, "PUT", body, async () =>
            {
                var who = CarrierApiEndpoints.PrincipalOf(context);
                if (!CarrierApi.MayWriteDirectly(config[CarrierApi.AutoApplyKey], who.AutoApplyMarked))
                    return Untrusted(context, "PUT /assignments/{jobKey}/truck, which writes into empty cells only");
                if (body?.TruckId is not > 0 || body.DriverId is not > 0)
                    return CarrierApiEndpoints.Problem(context, CarrierApi.Invalid,
                        "truckId and driverId are required — ids from GET /fleet; trailerId is optional");

                var result = await carriers.AssignResourcesForAsync(who.AsUser(), who.Company, jobKey,
                    body.TruckId.Value, body.TrailerId, body.DriverId.Value, token);
                if (!result.Ok)
                    return CarrierApiEndpoints.RefusedWrite(context, result, jobKey,
                        await CarrierApiEndpoints.GroupOfAsync(carriers, who, jobKey, token));
                return CarrierApiEndpoints.Answer(StatusCodes.Status200OK, new
                {
                    message = result.Message,
                    jobKey,
                    written = result.Written ?? new Dictionary<string, string>(),
                    previous = result.Previous ?? new Dictionary<string, string>(),
                    replayed = result.Replayed,
                    correlationId = context.TraceIdentifier,
                });
            }));

        // The portal's status move: the category's ladder, forward only, Delivery Complete opening the Billing Case.
        v1.MapPost("/assignments/{jobKey}/status", async (string jobKey, [FromBody] StatusBody? body,
            HttpContext context, IConfiguration config, CarrierService carriers, ScmosDbContext db,
            CancellationToken token) =>
            await CarrierApiEndpoints.IdempotentAsync(context, "POST", body, async () =>
            {
                var who = CarrierApiEndpoints.PrincipalOf(context);
                if (!CarrierApi.MayWriteDirectly(config[CarrierApi.AutoApplyKey], who.AutoApplyMarked))
                    return Untrusted(context, "POST /assignments/{jobKey}/events, which the job's owner approves");
                var type = CarrierOperations.Normalize(body?.Type);
                if (!CarrierOperations.Types.Contains(type))
                    return CarrierApiEndpoints.Problem(context, CarrierApi.Invalid,
                        "type must be one of: " + string.Join(", ", CarrierOperations.Types), new { types = CarrierOperations.Types });
                var (at, atProblem) = CarrierEvent.ReadAt(body?.At, DateTimeOffset.UtcNow);
                if (at is null) return CarrierApiEndpoints.Problem(context, CarrierApi.Invalid, atProblem ?? "at could not be read");
                var remark = Formats.Clean(body?.Remark);
                if (remark.Length > CarrierEvent.MaxRemark)
                    return CarrierApiEndpoints.Problem(context, CarrierApi.Invalid, $"remark is longer than {CarrierEvent.MaxRemark} characters");

                var result = await carriers.AdvanceForAsync(who.AsUser(), who.Company, jobKey, type, at, remark, token);
                if (!result.Ok)
                    return CarrierApiEndpoints.RefusedWrite(context, result, jobKey,
                        await CarrierApiEndpoints.GroupOfAsync(carriers, who, jobKey, token));

                // Delivery Complete opens the Billing Case in the same operation; say which, so the
                // TMS can go straight to its draft without listing.
                var billingCase = CarrierOperations.IsDeliveryComplete(type)
                    ? await db.BillingCases.AsNoTracking()
                        .Where(row => row.JobKey == jobKey && row.SupplierId == who.Company.Id)
                        .Select(row => new { row.Id, row.Status, row.SlaDueDate, row.SlaIssueCode })
                        .FirstOrDefaultAsync(token)
                    : null;
                return CarrierApiEndpoints.Answer(StatusCodes.Status200OK, new
                {
                    message = result.Message,
                    jobKey,
                    type,
                    status = result.Written?.GetValueOrDefault("status"),
                    from = result.Previous?.GetValueOrDefault("status"),
                    replayed = result.Replayed,
                    billingCase = billingCase is null ? null : new
                    {
                        id = billingCase.Id,
                        status = billingCase.Status,
                        slaDueDate = billingCase.SlaDueDate?.ToString("yyyy-MM-dd"),
                        slaIssueCode = billingCase.SlaIssueCode,
                    },
                    correlationId = context.TraceIdentifier,
                });
            }));

        // What happened on a job this carrier holds: the portal's history rows and the PODs, never a storage URL.
        v1.MapGet("/assignments/{jobKey}/history", async (string jobKey, HttpContext context,
            CarrierService carriers, CancellationToken token) =>
        {
            var who = CarrierApiEndpoints.PrincipalOf(context);
            var portal = await carriers.ReadForAsync(who.Company, token);
            var job = portal.Accepted.FirstOrDefault(one => string.Equals(one.Key, jobKey, StringComparison.Ordinal));
            if (job is null || !job.OperationalAvailable)
                return CarrierApiEndpoints.Refuse(context, CarrierApi.NotFound, "No accepted assignment with this id is held by this carrier");
            return Results.Json(new
            {
                jobKey,
                status = job.Status,
                operations = (job.Operations ?? []).Select(one => new
                {
                    one.Id, one.Kind, one.From, one.To, one.Note, one.RecordedAt, one.EventAt,
                }),
                pods = (job.Pods ?? []).Select(CarrierBillingApiEndpoints.PublicDocument),
                correlationId = context.TraceIdentifier,
            });
        });
    }

    /// <summary>The refusal an untrusted key gets from a direct write, naming the route it may use instead.</summary>
    private static CarrierApiEndpoints.Written Untrusted(HttpContext context, string instead) =>
        CarrierApiEndpoints.Problem(context, CarrierApi.Forbidden,
            $"This key writes through the department's approval, not directly; use {instead}. "
            + "The department can mark the key on Integrations → Carrier API.",
            new { reason = "not-trusted" });
}
