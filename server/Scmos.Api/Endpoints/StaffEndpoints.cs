using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Who may sign in, and as what.
///
/// Administrator only — `AdministerData` is the capability that separates
/// "runs the operation" from "decides who runs the operation", and it is the one
/// grant a supervisor deliberately does not have.
/// </summary>
public static class StaffEndpoints
{
    /// <param name="SignIn">
    /// How this person gets a way in: <c>invite</c> sends a Microsoft
    /// invitation to the address given, <c>tenant</c> creates a directory
    /// account with a temporary password they must replace, and <c>none</c>
    /// records the row only — for somebody who can already sign in.
    /// </param>
    public record CreateBody(string? Email, string? Name, string? Role, string? Note, string? SignIn);
    public record UpdateBody(string? Email, string? Name, string? Role, bool? Active, string? Note);

    public static void MapStaff(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/staff").WithTags("Administration");

        // Reading the directory needs ViewAudit rather than AdministerData: a
        // supervisor may need to see who holds what, and the list carries no
        // secret. Changing it is the administrator's alone.
        group.MapGet("", async (HttpContext context, IUserAccessor users, StaffService staff,
            SignInAccountService accounts, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewAudit))
                return ApiResults.Error("ดูทะเบียนผู้ใช้ได้เฉพาะระดับหัวหน้างานขึ้นไป",
                    StatusCodes.Status403Forbidden);

            return Results.Json(new
            {
                people = await staff.ListAsync(token),
                roles = Roles.All.Select(role => new
                {
                    name = role.Name,
                    scopeEn = role.ScopeEn,
                    scopeTh = role.ScopeTh,
                    grants = Enum.GetValues<Capability>()
                        .Where(capability => capability != Capability.None && Roles.Can(role.Name, capability))
                        .Select(capability => capability.ToString()).ToArray(),
                }),
                // Whether this caller may change anything, so the screen offers
                // controls that will work rather than ones that 403.
                canManage = user.Can(Capability.AdministerData),
                // Whether this API can build a sign-in as well as a row. Said
                // up front, because an administrator who fills in the form and
                // picks "invite" only to be told at the end that the API has no
                // directory permission has been made to do the work twice.
                signIn = await accounts.ReadyAsync(token) is var state
                    ? new { ready = state.Ready, why = state.Why }
                    : null,
                you = user.OperatorId,
            });
        });

        group.MapPost("", async ([FromBody] CreateBody body, HttpContext context, IUserAccessor users,
            StaffService staff, SignInAccountService accounts, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("เพิ่มผู้ใช้ได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            var email = body.Email ?? "";
            var name = body.Name ?? "";
            var mode = (body.SignIn ?? "invite").Trim().ToLowerInvariant();

            // Judged before anybody is invited: a rejected row after a sent
            // invitation leaves a real directory account with nothing pointing
            // at it, and an email in somebody's inbox for a system they cannot
            // get into.
            var checks = await staff.PrecheckAsync(email, name, body.Role ?? "", token);
            if (!checks.Ok) return ApiResults.Error(checks.Message, StatusCodes.Status400BadRequest);

            var signInName = "";
            var temporary = "";
            var howTheyGetIn = "บัญชีนี้ต้องลงชื่อเข้าใช้ด้วยบัญชีที่มีอยู่แล้ว";

            if (mode is "invite" or "tenant")
            {
                var made = mode == "invite"
                    ? await accounts.InviteAsync(email, name, token)
                    : await accounts.CreateTenantAccountAsync(name,
                        email.Contains('@') ? email[..email.IndexOf('@')] : email, token);

                if (!made.Ok) return ApiResults.Error(made.Message, StatusCodes.Status400BadRequest);

                // An invited guest keeps the address a person would search for.
                // Microsoft sends that address as the claim in the ordinary
                // case, and `StaffService.MatchIn` understands the `#EXT#` form
                // as well, so the readable one is the better of two that both
                // work. A tenant account has no second form — its UPN is the
                // only name it has.
                signInName = mode == "tenant" ? made.SignInName : "";
                temporary = made.TempPassword;
                howTheyGetIn = made.SignInName.Length > 0 && mode == "invite"
                    ? $"{made.Message} · ไดเรกทอรีจะเรียกบัญชีนี้ว่า {made.SignInName}"
                    : made.Message;
            }

            var result = await staff.CreateAsync(email, name, body.Role ?? "", body.Note ?? "",
                user, token, signInName);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Register, "staff", result.Id ?? "",
                name, "role", "", body.Role ?? "", body.Note ?? "", token);

            return Results.Json(new
            {
                message = result.Message,
                id = result.Id,
                signIn = howTheyGetIn,
                signInName,
                // Shown once and never stored — not in the row, not in the
                // audit trail. An administrator who loses it issues another.
                tempPassword = temporary,
            });
        });

        // Sending the invitation on its own, for somebody already in the
        // directory. Creating a person and inviting them is one action; this is
        // the second half by itself, for the row that was added before the API
        // could build sign-ins, or the invitation that never arrived.
        group.MapPost("/{id}/invite", async (string id, HttpContext context, IUserAccessor users,
            StaffService staff, SignInAccountService accounts, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("ส่งคำเชิญได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            var person = (await staff.ListAsync(token)).FirstOrDefault(p => p.Id == id);
            if (person is null) return ApiResults.Error("ไม่พบผู้ใช้", StatusCodes.Status404NotFound);
            if (person.Email.Trim().Length == 0)
                return ApiResults.Error("แถวนี้ยังไม่มีอีเมล — ใส่อีเมลก่อนจึงจะส่งคำเชิญได้",
                    StatusCodes.Status400BadRequest);

            // A directory account of this organisation has no invitation to
            // send: it already has a way in, and the person needs a password
            // rather than a link.
            if (person.Email.Contains('@') && person.Email.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase)
                && !person.Email.Contains("#EXT#", StringComparison.OrdinalIgnoreCase))
                return ApiResults.Error(
                    "บัญชีนี้เป็นบัญชีขององค์กรอยู่แล้ว ไม่ต้องเชิญ — ถ้าเข้าไม่ได้ให้ใช้ปุ่มออกรหัสใหม่",
                    StatusCodes.Status400BadRequest);

            var invited = await accounts.InviteAsync(person.Email, person.Name, token);
            if (!invited.Ok) return ApiResults.Error(invited.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Register, "staff", id, person.Name,
                "invitation", "", person.Email, "ผู้ดูแลระบบส่งคำเชิญเข้าใช้งาน", token);

            return Results.Json(new { message = invited.Message });
        });

        group.MapDelete("/{id}", async (string id, HttpContext context, IUserAccessor users,
            StaffService staff, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("ลบผู้ใช้ได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            // Read the row first: once it is gone there is nothing left to name
            // in the trail, and "AD-07 was deleted" is not much of a record.
            var before = (await staff.ListAsync(token)).FirstOrDefault(p => p.Id == id);

            var result = await staff.DeleteAsync(id, user, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "staff", id,
                before?.Name ?? id, "deleted",
                $"{before?.Role} · {before?.Email}", "(ลบออกจากทะเบียน)",
                "ผู้ดูแลระบบลบบัญชีที่ปิดแล้วและไม่มีงานค้าง", token);

            return Results.Json(new { message = result.Message });
        });

        group.MapPost("/{id}/reset-password", async (string id, HttpContext context,
            IUserAccessor users, StaffService staff, SignInAccountService accounts, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("ตั้งรหัสผ่านใหม่ได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            var person = (await staff.ListAsync(token)).FirstOrDefault(p => p.Id == id);
            if (person is null) return ApiResults.Error("ไม่พบผู้ใช้", StatusCodes.Status404NotFound);

            var result = await accounts.ResetPasswordAsync(person.Email, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            // The password itself is never recorded — only that it was replaced,
            // by whom, and for whom.
            await audit.RecordAsync(user, AuditActions.Update, "staff", id, person.Name,
                "password", "", "reset", "ผู้ดูแลระบบออกรหัสผ่านชั่วคราวใหม่", token);

            return Results.Json(new { message = result.Message, tempPassword = result.TempPassword });
        });

        group.MapPost("/{id}", async (string id, [FromBody] UpdateBody body, HttpContext context,
            IUserAccessor users, StaffService staff, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("แก้ไขผู้ใช้ได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            // Read the row before the change so the trail can say what it was.
            var before = (await staff.ListAsync(token)).FirstOrDefault(p => p.Id == id);

            var result = await staff.UpdateAsync(id, body.Email, body.Name, body.Role, body.Active,
                body.Note, user, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            var field = body.Role is not null ? "role" : body.Active is not null ? "active" : "details";
            var was = field switch
            {
                "role" => before?.Role ?? "",
                "active" => (before?.Active ?? true).ToString(),
                _ => before?.Email ?? "",
            };
            var now = field switch
            {
                "role" => body.Role ?? "",
                "active" => (body.Active ?? true).ToString(),
                _ => body.Email ?? "",
            };

            await audit.RecordAsync(user, AuditActions.Update, "staff", id,
                before?.Name ?? id, field, was, now, body.Note ?? "", token);

            return Results.Json(new { message = result.Message });
        });
    }
}
