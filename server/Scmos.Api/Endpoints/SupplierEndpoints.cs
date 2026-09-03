using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Suppliers, rates, CAR/PAR and the AI gateway.
///
/// The write routes are supervisor-only wherever the action commits the company
/// to something: approving a vendor, changing a rate, deciding on an AI
/// proposal. Reading is open to anyone signed in.
/// </summary>
/// <summary>Which quoted lanes to move into the contracted rate book.</summary>
public record PromoteBody(List<long>? LaneIds);

public static class SupplierEndpoints
{
    public record RegisterBody(string? Name, string? Code, string? ServiceType, string? ServiceArea, string? Reason);
    public record StatusBody(string? Status, string? Reason);
    /// <summary>Which row survives a merge, and which is folded into it.</summary>
    public record MergeBody(int KeepId, int FoldId, string? Reason);

    /// <summary>Every stored field of a supplier. Null leaves one alone.</summary>
    public record EditBody(
        string? Code, string? Name, string? Status,
        string? VendorNo, string? TaxId, string? Address,
        string? ServiceArea, string? ServiceType,
        bool? DgCapable, bool? ReeferCapable, bool? IsoTankCapable, bool? GpsEquipped,
        string? Reason);

    public record AliasBody(string? Alias, string? Reason);
    public record EvaluateBody(string? Period, int? Safety, int? Documents, string? Note);
    /// <summary>
    /// Opening a case, with the four of the 5W1H that are already known.
    ///
    /// Where, When and Who come off the job — where the load was going, when it
    /// was due, who was driving. What comes off the operational issue the case
    /// was escalated from, which is the record of what actually went wrong.
    ///
    /// Why and how are not here and are not seeded. Those are the case: they
    /// are what the investigation is for, and a form that arrives with them
    /// answered is a form nobody investigates.
    /// </summary>
    public record RaiseBody(string? JobKey, string? Kind, string? Category, string? Title,
        string? What = null, string? Where = null, string? When = null, string? Who = null);
    public record ReasonBody(string? Reason);

    /// <param name="Names">One haulage company per line, as their paperwork spells it.</param>
    /// <param name="Aliases">
    /// The short forms the plan sheets use, each naming the company it belongs
    /// to: "SJ = Sangja Transport Co., Ltd.". Without these the register counts
    /// SANGJA and SJ as two firms, which is what it has been doing.
    /// </param>
    public record DirectoryBody(List<string>? Names, List<AliasLine>? Aliases);

    public record AliasLine(string? Alias, string? Company);
    public record InvokeBody(string? Tool, string? Summary, JsonElementPayload? Payload);
    public record DecideBody(bool Approved, string? Note);
    public record AppliedBody(string? Result);

    /// <summary>The tool arguments, kept opaque — the gateway stores them verbatim.</summary>
    public record JsonElementPayload(Dictionary<string, string>? Fields);

    public static void MapSuppliers(this IEndpointRouteBuilder routes)
    {
        /* ------------------------------------------------------- suppliers */
        var suppliers = routes.MapGroup("/api/suppliers").WithTags("Suppliers");

        suppliers.MapGet("", async (string? status, string? q, HttpContext context, IUserAccessor users,
            SupplierService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await service.ListAsync(status, q, token));
        });

        suppliers.MapGet("/{id:int}", async (int id, HttpContext context, IUserAccessor users,
            SupplierService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var profile = await service.ProfileAsync(id, token);
            return profile is null
                ? ApiResults.Error("ไม่พบผู้ขนส่งรายนี้", StatusCodes.Status404NotFound)
                : Results.Json(profile);
        });

        suppliers.MapPost("", async ([FromBody] RegisterBody body, HttpContext context, IUserAccessor users,
            SupplierService service, AuditService audit, CancellationToken token) =>
            await Guarded(context, users, Capability.ManageSuppliers, audit, token,
                user => service.RegisterAsync(body.Name ?? "", body.Code ?? "", body.ServiceType ?? "",
                    body.ServiceArea ?? "", user.Signature, token),
                AuditActions.Register, "supplier", body.Name ?? "", "", "", body.Name ?? "", body.Reason ?? ""));

        suppliers.MapPost("/{id:int}/status", async (int id, [FromBody] StatusBody body, HttpContext context,
            IUserAccessor users, SupplierService service, AuditService audit, CancellationToken token) =>
            await Guarded(context, users, Capability.ManageSuppliers, audit, token,
                user => service.SetStatusAsync(id, body.Status ?? "", user.Signature, token),
                AuditActions.StatusChange, "supplier", id.ToString(), "สถานะการอนุมัติ", "",
                body.Status ?? "", body.Reason ?? ""));

        // The agreed list of haulage companies. Pasted in rather than shipped:
        // these names belong to the business the same way the customer list and
        // the rate book do, and the rule here is that they live in the database
        // and never in the repository or a deployment package.
        suppliers.MapPost("/directory", async ([FromBody] DirectoryBody body, HttpContext context,
            IUserAccessor users, SupplierService service, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ManageSuppliers))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ทะเบียนผู้ขนส่ง", StatusCodes.Status403Forbidden);

            var names = body.Names ?? [];
            var aliases = (body.Aliases ?? [])
                .Select(line => (Alias: line.Alias ?? "", Company: line.Company ?? ""))
                .Where(line => line.Alias.Length > 0 && line.Company.Length > 0)
                .ToList();

            if (names.Count == 0 && aliases.Count == 0)
                return ApiResults.Error("ไม่มีรายชื่อให้นำเข้า", StatusCodes.Status400BadRequest);

            var result = await service.ImportDirectoryAsync(names, aliases, user.Signature, token);

            await audit.RecordAsync(user, "import", "supplier-directory", "all",
                "ทะเบียนผู้ขนส่ง", "", "",
                $"เพิ่ม {result.Added} · มีอยู่แล้ว {result.AlreadyThere} · ผูกชื่อย่อ {result.AliasesLinked}",
                "", token);

            return Results.Json(result);
        });

        // Hauliers the register holds twice, and the merge that undoes it.
        //
        // Reading them is open to anybody signed in — knowing the register has
        // a problem is not a privileged act. Merging is not: it moves history
        // between rows and removes one, so it wants the same right as any other
        // change to the register, and it is audited by row, not in bulk.
        suppliers.MapGet("/duplicates", async (HttpContext context, IUserAccessor users,
            SupplierService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await service.DuplicatesAsync(token));
        });

        suppliers.MapPost("/merge", async ([FromBody] MergeBody body, HttpContext context,
            IUserAccessor users, SupplierService service, AuditService audit, CancellationToken token) =>
            await Guarded(context, users, Capability.ManageSuppliers, audit, token,
                user => service.MergeAsync(body.KeepId, body.FoldId, user.Signature, token),
                "merge", "supplier", body.KeepId.ToString(), "รวมรายการซ้ำ",
                body.FoldId.ToString(), body.KeepId.ToString(), body.Reason ?? ""));

        // Correcting a company's own details — every field of it.
        //
        // The register's screen shows seven columns and only three of them are
        // stored: the rest are counted from the jobs, the rate book and the
        // evaluations, and are corrected where they come from rather than
        // typed over here.
        suppliers.MapPost("/{id:int}/edit", async (int id, [FromBody] EditBody body, HttpContext context,
            IUserAccessor users, SupplierService service, AuditService audit, CancellationToken token) =>
            await Guarded(context, users, Capability.ManageSuppliers, audit, token,
                user => service.EditAsync(id, new SupplierService.SupplierEdit(
                    body.Code, body.Name, body.Status, body.VendorNo, body.TaxId, body.Address,
                    body.ServiceArea, body.ServiceType,
                    body.DgCapable, body.ReeferCapable, body.IsoTankCapable, body.GpsEquipped),
                    user.Signature, token),
                AuditActions.Update, "supplier", id.ToString(), "ข้อมูลบริษัท", "",
                body.Name ?? body.Code ?? "", body.Reason ?? ""));

        // Taking a company off the register.
        //
        // Refused by the service unless the row is holding nothing at all, so
        // this is not the dangerous verb it looks like: what it removes is a
        // name somebody typed wrong or a company that never traded. A company
        // with history is merged, not deleted.
        suppliers.MapDelete("/{id:int}", async (int id, string? reason, HttpContext context,
            IUserAccessor users, SupplierService service, AuditService audit, CancellationToken token) =>
            await Guarded(context, users, Capability.ManageSuppliers, audit, token,
                user => service.RemoveAsync(id, user.Signature, token),
                "remove", "supplier", id.ToString(), "ลบออกจากทะเบียน", id.ToString(), "",
                reason ?? ""));

        suppliers.MapPost("/{id:int}/alias", async (int id, [FromBody] AliasBody body, HttpContext context,
            IUserAccessor users, SupplierService service, AuditService audit, CancellationToken token) =>
            await Guarded(context, users, Capability.ManageSuppliers, audit, token,
                user => service.LinkAliasAsync(id, body.Alias ?? "", user.Signature, token),
                AuditActions.Update, "supplier", id.ToString(), "ชื่อที่ผูก", "", body.Alias ?? "",
                body.Reason ?? ""));

        suppliers.MapPost("/{id:int}/evaluate", async (int id, [FromBody] EvaluateBody body, HttpContext context,
            IUserAccessor users, SupplierService service, AuditService audit, CancellationToken token) =>
            await Guarded(context, users, Capability.ManageSuppliers, audit, token,
                user => service.EvaluateAsync(id, body.Period ?? "", body.Safety, body.Documents,
                    body.Note ?? "", user.Signature, token),
                "evaluate", "supplier", id.ToString(), $"ประเมินรอบ {body.Period}", "", body.Period ?? "",
                body.Note ?? ""));

        /* ----------------------------------------------------------- rates */
        var rates = routes.MapGroup("/api/rates").WithTags("Rates");

        // ViewRates, not merely signed in. The book holds eighteen carriers'
        // negotiated prices, and the Subcontractor role exists precisely so a
        // carrier can work their own jobs without reading what seventeen
        // competitors charge. That is the single worst thing this system could
        // leak, so it is refused here rather than hidden on a screen.
        // `source` is "carrier" for the signed forms, "quotation" for what the
        // rate sheet has been quoted and spread up the bands by the fuel clause,
        // or absent for both. The screen picks; nothing here decides for it.
        rates.MapGet("", async (string? carrier, string? service, string? source,
            HttpContext context, IUserAccessor users,
            RateService service_, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูตารางราคา", StatusCodes.Status403Forbidden);
            return Results.Json(await service_.ReadAsync(carrier, service, source, token));
        });

        /*
         * Moving quoted lanes into the contracted book.
         *
         * A write to the rate book, so ViewRates is not enough — this needs the
         * capability that governs changing a rate, and it is recorded. Until it
         * runs, a price keyed into Rate Quotation is a quotation; afterwards it
         * is a rate the team is working to, and which of the two a number is
         * should never be a question nobody can answer later.
         */
        rates.MapPost("/promote", async ([FromBody] PromoteBody body, HttpContext context,
            IUserAccessor users, RateService service, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง", StatusCodes.Status403Forbidden);

            var ids = body.LaneIds ?? [];
            if (ids.Count == 0)
                return ApiResults.Error("ไม่ได้เลือกเส้นทางที่จะย้าย", StatusCodes.Status400BadRequest);

            // A cap, because this writes seven prices per vehicle per lane and
            // the whole register at once is a request nobody can tell has hung.
            if (ids.Count > 500)
                return ApiResults.Error(
                    $"ย้ายได้ครั้งละไม่เกิน 500 เส้นทาง (เลือกมา {ids.Count:N0}) — กรองให้แคบลงก่อน",
                    StatusCodes.Status400BadRequest);

            var done = await service.PromoteAsync(ids, user.Signature, token);
            await audit.RecordAsync(user, AuditActions.Update, "rate", "promote",
                "Rate Quotation -> Transport Rate", "ราคา", "",
                $"{done.Lanes} เส้นทาง · {done.Prices} ราคา",
                $"ย้ายจาก New Transport Rate เข้าตารางอัตราที่ใช้งานจริง", token);

            var message = done.Lanes == 0
                ? "ไม่มีเส้นทางที่ย้ายได้"
                : $"ย้ายแล้ว {done.Lanes:N0} เส้นทาง · {done.Prices:N0} ราคา"
                  + (done.Skipped > 0 ? $" · ข้าม {done.Skipped:N0}" : "");
            return Results.Json(new { message, done.Lanes, done.Prices, done.Skipped, done.Notes });
        });

        rates.MapGet("/quotes", async (string? customer, string? destination, string? vehicle, decimal? diesel,
            HttpContext context, IUserAccessor users, RateService service, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูตารางราคา", StatusCodes.Status403Forbidden);
            var quotes = await service.QuotesForAsync(customer ?? "", destination ?? "",
                vehicle ?? "", diesel ?? 32.94m, token);
            return Results.Json(quotes);
        });

        /* ------------------------------------------------------- incidents */
        var incidents = routes.MapGroup("/api/incidents").WithTags("Incidents");

        incidents.MapGet("", async (string? stage, string? kind, HttpContext context, IUserAccessor users,
            IncidentService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await service.ListAsync(stage, kind, token));
        });

        incidents.MapGet("/{id:long}", async (long id, HttpContext context, IUserAccessor users,
            IncidentService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var view = await service.ReadAsync(id, token);
            return view is null ? ApiResults.Error("ไม่พบเคสนี้", StatusCodes.Status404NotFound) : Results.Json(view);
        });

        incidents.MapPost("", async ([FromBody] RaiseBody body, HttpContext context, IUserAccessor users,
            IncidentService service, AuditService audit, CancellationToken token) =>
            await GuardedIncident(context, users, audit, token,
                (user, _) => service.RaiseAsync(body.JobKey ?? "", body.Kind ?? "CAR",
                    body.Category ?? "other", body.Title ?? "", user.Signature, token,
                    body.What ?? "", body.Where ?? "", body.When ?? "", body.Who ?? ""),
                "create", body.Title ?? "", "", "", "open", ""));

        incidents.MapPost("/{id:long}", async (long id, [FromBody] Dictionary<string, string> body,
            HttpContext context, IUserAccessor users, IncidentService service, AuditService audit,
            CancellationToken token) =>
            await GuardedIncident(context, users, audit, token,
                (user, _) => service.UpdateAsync(id, body ?? [], user.Signature, token),
                AuditActions.Update, "", string.Join(", ", (body ?? []).Keys), "",
                string.Join(" · ", (body ?? []).Values.Where(v => v.Trim().Length > 0)), ""));

        // Advancing to closed is a signature, which is why the reason travels
        // with it: "why was this case closed" is the question an auditor asks
        // about a CAR/PAR, and the stage alone never answers it.
        incidents.MapPost("/{id:long}/advance", async (long id, [FromBody] ReasonBody? body,
            HttpContext context, IUserAccessor users, IncidentService service, AuditService audit,
            CancellationToken token) =>
            await GuardedIncident(context, users, audit, token,
                (user, role) => service.AdvanceAsync(id, user.Signature, role, token),
                AuditActions.Close, "", "ขั้นตอน", "", "", (body?.Reason ?? "").Trim()));

        // Evidence is uploaded, not declared: POST /api/documents with a caseId
        // and the file. This route stayed only long enough to notice that it let
        // the caller invent the blob path — the one thing the storage structure
        // depends on nobody doing.

        /* --------------------------------------------------------------- AI */
        var ai = routes.MapGroup("/api/ai").WithTags("AI");

        // The permission matrix, readable so the screen can show what the
        // assistant may and may not do rather than asserting it in prose.
        ai.MapGet("/tools", async (HttpContext context, IUserAccessor users, AiGateway gateway,
            CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(new
            {
                tools = await gateway.ToolsAsync(token),
                forbidden = AiPermissions.Forbidden,
            });
        });

        ai.MapPost("/invoke", async ([FromBody] InvokeBody body, HttpContext context, IUserAccessor users,
            AiGateway gateway, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            // No executor is passed: nothing in the catalogue is wired to a real
            // action yet. An Allow tool therefore reports that plainly instead of
            // pretending to have done something.
            var outcome = await gateway.InvokeAsync(
                body.Tool ?? "", body.Summary ?? "", body.Payload?.Fields ?? new Dictionary<string, string>(),
                user, null, token);

            return outcome.Ok
                ? Results.Json(new { message = outcome.Message, kind = outcome.Kind, approvalId = outcome.ApprovalId })
                : ApiResults.Error(outcome.Message, StatusCodes.Status403Forbidden);
        });

        ai.MapGet("/approvals", async (string? state, HttpContext context, IUserAccessor users,
            AiGateway gateway, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await gateway.ApprovalsAsync(state, token));
        });

        // A person agreeing to a machine's proposal is the entry an audit will
        // care about most, so it is recorded with the decision note as the
        // reason — and with the assistant named as the source of the change.
        ai.MapPost("/approvals/{id:long}", async (long id, [FromBody] DecideBody body, HttpContext context,
            IUserAccessor users, AiGateway gateway, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var outcome = await gateway.DecideAsync(id, body.Approved, body.Note ?? "", user, token);
            if (!outcome.Ok) return ApiResults.Error(outcome.Message, StatusCodes.Status403Forbidden);

            await audit.RecordAsync(user,
                body.Approved ? AuditActions.Approve : AuditActions.Reject,
                "approval", id.ToString(), outcome.Message, "", "pending",
                body.Approved ? "approved" : "rejected", body.Note ?? "", token, source: "ai");

            return Results.Json(new { message = outcome.Message });
        });

        ai.MapPost("/approvals/{id:long}/applied", async (long id, [FromBody] AppliedBody body,
            HttpContext context, IUserAccessor users, AiGateway gateway, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ApproveAi))
                return ApiResults.Error("ทำได้เฉพาะระดับหัวหน้างานขึ้นไป", StatusCodes.Status403Forbidden);
            var outcome = await gateway.MarkAppliedAsync(id, body.Result ?? "", token);
            if (!outcome.Ok) return ApiResults.Error(outcome.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Apply, "approval", id.ToString(), outcome.Message,
                "", "approved", "applied", body.Result ?? "", token, source: "ai");

            return Results.Json(new { message = outcome.Message });
        });
    }

    /// <summary>
    /// Checks the capability, runs the action, and records it.
    ///
    /// The audit row is written here rather than inside the service so that
    /// adding a supplier route cannot quietly add an unaudited one — the same
    /// wrapper that lets the call through is the one that writes it down.
    /// </summary>
    private static async Task<IResult> Guarded(HttpContext context, IUserAccessor users,
        Capability required, AuditService audit, CancellationToken token,
        Func<AppUser, Task<SupplierResult>> action,
        string auditAction, string entity, string entityId, string label,
        string oldValue, string newValue, string reason)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        if (!user.Can(required))
            return ApiResults.Error("ทำได้เฉพาะระดับหัวหน้างานขึ้นไป", StatusCodes.Status403Forbidden);

        var result = await action(user);
        if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

        await audit.RecordAsync(user, auditAction, entity, result.Id?.ToString() ?? entityId,
            label.Length > 0 ? label : result.Message, "", oldValue, newValue, reason, token);

        return Results.Json(new { message = result.Message, id = result.Id });
    }

    private static async Task<IResult> GuardedIncident(HttpContext context, IUserAccessor users,
        AuditService audit, CancellationToken token, Func<AppUser, string, Task<IncidentResult>> action,
        string auditAction, string label, string field, string oldValue, string newValue, string reason)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;

        var result = await action(user, user.Role);
        if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

        await audit.RecordAsync(user, auditAction, "incident", result.Id?.ToString() ?? "",
            label.Length > 0 ? label : result.Message, field, oldValue,
            newValue.Length > 0 ? newValue : result.Message, reason, token);

        return Results.Json(new { message = result.Message, id = result.Id });
    }
}
