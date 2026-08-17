using Scmos.Api.Auth;

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
        routes.MapGet("/api/me", (HttpContext context, IUserAccessor users) =>
        {
            var user = users.Current(context);
            if (user is null) return Results.Json(new { account = (object?)null });

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
                source = user.Source,
            });
        }).WithTags("Identity");
    }
}
