using System.Text.Json;

namespace Scmos.Api.Rules;

/// <summary>
/// What Entra says it granted, read off the access token itself.
///
/// <para>
/// An application permission arrives as a <c>roles</c> claim in the token. So
/// the question "has anybody admin-consented <c>Mail.Read</c> yet?" can be
/// answered before a single mailbox is touched — which matters, because the
/// alternative diagnostic is a 403 from a mailbox read, and a 403 there means
/// either no consent, or no Exchange RBAC scope, or a mailbox that does not
/// exist. Three faults that look identical are three days of guessing.
/// </para>
///
/// <para>
/// <b>This reports; it does not decide.</b> Graph enforces the permission, and
/// nothing here should ever stand in for that. The claim is read out of a token
/// this API asked Entra for, so it says what we were given — not what we are
/// allowed to do with it.
/// </para>
///
/// <para>
/// Never throws and never logs. A token is a credential: the one thing this
/// must not do is end up printing part of one into a log while explaining that
/// it could not be parsed.
/// </para>
/// </summary>
public static class GraphToken
{
    /// <summary>The application permissions the token carries, or empty for anything unreadable.</summary>
    public static IReadOnlyList<string> RolesIn(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return [];

        var parts = jwt.Split('.');
        if (parts.Length < 2) return [];

        try
        {
            using var payload = JsonDocument.Parse(Base64Url(parts[1]));
            if (!payload.RootElement.TryGetProperty("roles", out var roles)
                || roles.ValueKind != JsonValueKind.Array) return [];

            var found = new List<string>();
            foreach (var role in roles.EnumerateArray())
                if (role.ValueKind == JsonValueKind.String && role.GetString() is { Length: > 0 } name)
                    found.Add(name);
            return found;
        }
        catch (FormatException) { return []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Whether one named permission is among them.</summary>
    public static bool Grants(string? jwt, string role) =>
        !string.IsNullOrWhiteSpace(role)
        && RolesIn(jwt).Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The base64url a JWT segment is written in: '+' and '/' swapped out, and
    /// the '=' padding dropped because it is redundant in a URL.
    /// </summary>
    private static byte[] Base64Url(string segment)
    {
        var text = segment.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '='));
    }
}
