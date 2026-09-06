using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The error shape the workspace already reads: <c>{ "error": "…" }</c> with a
/// status. Kept in one place so every endpoint answers the same way.
/// </summary>
public static class ApiResults
{
    public static IResult Error(string message, int status) =>
        Results.Json(new { error = message }, statusCode: status);

    public static IResult SignInRequired => Error("Sign in is required", StatusCodes.Status401Unauthorized);

    /// <summary>
    /// The refusal for a sign-in that has not been shown to carry a second
    /// factor, or null when there is nothing to refuse.
    ///
    /// <para>
    /// Asked <b>after</b> the capability, never instead of it. Being told to go
    /// and set up an authenticator for a permission you were never going to be
    /// granted is a worse answer than being told you do not have the permission.
    /// </para>
    ///
    /// <para>
    /// Every guarded endpoint asks it, and that uniformity is the feature: the
    /// three capabilities named in <see cref="SignIn.Guarded"/> are reached
    /// through twenty-one doors, and one of them left unasked is not a weaker
    /// control, it is the way around the control.
    /// </para>
    /// </summary>
    public static IResult? NeedsSecondFactor(IUserAccessor users, AppUser user, Capability capability) =>
        users.Refuses(user, capability) is { } why
            ? Error(why, StatusCodes.Status403Forbidden)
            : null;
}
