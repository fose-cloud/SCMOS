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
}
