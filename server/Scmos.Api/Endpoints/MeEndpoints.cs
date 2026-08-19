using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Who the caller is, in the account shape the workspace already renders. The
/// web app asks this on first paint so the header, the tabs and the edit rules
/// know who they are looking at.
/// </summary>
public static class MeEndpoints
{
    public static void MapMe(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/me", async (HttpContext context, IUserAccessor users,
            Services.DelegationService delegations, CancellationToken token) =>
        {
            // The one endpoint that asks who signed in rather than who is
            // allowed in. Somebody refused at the door has already passed
            // Microsoft's sign-in page, so answering "sign in" would send them
            // round a loop that cannot end. They need to be told their name is
            // not on the list, and who can put it there.
            var user = users.Identity(context);
            if (user is null) return Results.Json(new { account = (object?)null });

            var authorised = users.Current(context) is not null;

            var parts = user.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initials = (parts.Length > 1
                ? $"{parts[0][0]}{parts[1][0]}"
                : user.DisplayName[..Math.Min(2, user.DisplayName.Length)]).ToUpperInvariant();

            return Results.Json(new
            {
                account = new
                {
                    user = user.Signature,
                    // `name` stays the first word, because that is what the plan
                    // workbooks call this person. Ownership no longer depends on it —
                    // opId does — but the screen still greets them by it.
                    name = parts.FirstOrDefault() ?? user.DisplayName,
                    full = user.DisplayName,
                    role = user.Role,
                    id = user.OperatorId.Length > 0 ? user.OperatorId : user.UserId,
                    opId = user.OperatorId,
                    init = initials.Length > 0 ? initials : "??",
                },
                // What this person may do, from the same table the API enforces
                // against. The screens need it to decide what to render, and a
                // second list kept in the browser would eventually grant a
                // button the API refuses — or hide one it allows.
                can = authorised
                    ? Enum.GetValues<Capability>()
                        .Where(capability => capability != Capability.None && user.Can(capability))
                        .Select(capability => capability.ToString())
                        .ToArray()
                    : [],
                scope = Roles.Find(user.Role) is { } definition
                    ? new { en = definition.ScopeEn, th = definition.ScopeTh }
                    : null,
                // No owner id means the staff directory has never heard of this
                // person. Their workspace will be empty and nothing will look
                // like theirs — which is correct, and looks exactly like a
                // broken system unless the screen is told to say so.
                // Whose jobs this person is covering today. The grid reads it to
                // decide which rows to make editable; the API checks the same
                // service before accepting the write, so the two cannot drift.
                actingFor = await delegations.ActingForAsync(user.OperatorId, token),
                known = user.OperatorId.Length > 0,
                // Signed in, but nobody has granted this account anything. Every
                // other endpoint refuses them; this says so in one word so the
                // screen can stop rather than render an app made of errors.
                authorised,
                source = user.Source,
            });
        }).WithTags("Identity");
    }
}
