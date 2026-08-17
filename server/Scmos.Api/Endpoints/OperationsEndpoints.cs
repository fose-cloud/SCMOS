using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The keyed-entry table from the previous UI. Nothing in the workspace writes
/// to it any more, but the rows and their on-time judgements are real history,
/// so the route moved across with its rules unchanged.
/// </summary>
public static class OperationsEndpoints
{
    private static readonly HashSet<string> Owners =
        new(["Maliwan", "Ananya", "Jiratchaya", "Uthai", "Watsana"], StringComparer.Ordinal);

    private static readonly HashSet<string> Flows = new(["Import", "Export"], StringComparer.Ordinal);

    public record EntryRequest(
        string? OwnerName, string? Flow, string? WorkDate, string? Customer, string? Subcontractor,
        string? JobCode, string? PlanAt, string? ActualAt, string? EquipmentType, string? ContainerNo,
        string? OperationStatus, string? Remark);

    public static void MapOperations(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/operations").WithTags("Operations");

        group.MapGet("", async (string? owner, string? period, HttpContext context, IUserAccessor users,
            ScmosDbContext db, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var query = db.OperationEntries.AsNoTracking();

            var wantedOwner = Clean(owner, 40);
            if (wantedOwner.Length > 0 && wantedOwner != "All")
                query = query.Where(entry => entry.OwnerName == wantedOwner);

            var wantedPeriod = Clean(period, 20);
            if (wantedPeriod.Length > 0)
                query = query.Where(entry => entry.ReportingPeriod == wantedPeriod);

            var records = await query
                .OrderByDescending(entry => entry.WorkDate)
                .ThenByDescending(entry => entry.SubmittedAt)
                .Take(500)
                .ToListAsync(token);

            return Results.Json(new
            {
                records,
                viewer = new { email = user.Email, name = user.DisplayName },
            });
        });

        group.MapPost("", async (EntryRequest body, HttpContext context, IUserAccessor users,
            ScmosDbContext db, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var ownerName = Clean(body.OwnerName, 40);
            var flow = Clean(body.Flow, 10);
            var workDate = Clean(body.WorkDate, 10);
            var customer = Clean(body.Customer, 180);
            var subcontractor = Clean(body.Subcontractor, 180);
            var jobCode = Clean(body.JobCode, 80);
            var planAt = Clean(body.PlanAt, 32);

            if (!Owners.Contains(ownerName) || !Flows.Contains(flow) || workDate.Length == 0 ||
                customer.Length == 0 || subcontractor.Length == 0 || jobCode.Length == 0 || planAt.Length == 0)
            {
                return ApiResults.Error(
                    "Owner, date, flow, customer, subcontractor, job and plan time are required",
                    StatusCodes.Status400BadRequest);
            }

            var actualAt = Clean(body.ActualAt, 32);
            var equipmentType = Clean(body.EquipmentType, 40);
            var containerNo = Clean(body.ContainerNo, 80);

            var issues = new List<string>();
            if (equipmentType.Contains("FCL", StringComparison.OrdinalIgnoreCase) && containerNo.Length == 0)
                issues.Add("Missing container for FCL");

            var planParsed = TryParse(planAt, out var plan);
            if (!planParsed) issues.Add("Invalid plan time");

            DateTimeOffset? actual = null;
            if (actualAt.Length > 0)
            {
                if (TryParse(actualAt, out var parsedActual)) actual = parsedActual;
                else issues.Add("Invalid actual time");
            }

            var validationStatus = issues.Count > 0 ? "Needs review" : actual is not null ? "Ready" : "In progress";
            var otdStatus = actual is null || issues.Count > 0
                ? "Not Assessable"
                : actual <= plan ? "On Time" : "Late";

            var now = DateTimeOffset.UtcNow;
            var entry = new OperationEntry
            {
                Id = Guid.NewGuid(),
                OwnerName = ownerName,
                WorkDate = workDate,
                ReportingPeriod = workDate.Length >= 7 ? workDate[..7] : workDate,
                Flow = flow,
                Customer = customer,
                Subcontractor = subcontractor,
                JobCode = jobCode,
                ContainerNo = containerNo.Length > 0 ? containerNo : null,
                EquipmentType = equipmentType.Length > 0 ? equipmentType : null,
                PlanAt = planAt,
                ActualAt = actualAt.Length > 0 ? actualAt : null,
                OperationStatus = Clean(body.OperationStatus, 40) is { Length: > 0 } status ? status : "Planned",
                ValidationStatus = validationStatus,
                OtdStatus = otdStatus,
                Remark = Clean(body.Remark, 500) is { Length: > 0 } remark ? remark : null,
                SubmittedBy = user.Signature,
                SubmittedAt = now,
                UpdatedAt = now,
            };

            db.OperationEntries.Add(entry);
            await db.SaveChangesAsync(token);

            return Results.Json(new { id = entry.Id, validationStatus, otdStatus, issues }, statusCode: StatusCodes.Status201Created);
        });
    }

    private static bool TryParse(string value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed);

    private static string Clean(string? value, int max)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
