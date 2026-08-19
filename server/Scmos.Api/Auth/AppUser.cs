using Scmos.Api.Rules;

namespace Scmos.Api.Auth;

/// <summary>The signed-in person, in the shape the workspace already renders.</summary>
public record AppUser(
    string UserId,
    string Email,
    string DisplayName,
    string Role,
    /// <summary>Directory id (OP-01…). Empty when the account is not one of the eight.</summary>
    string OperatorId,
    /// <summary>Where the identity came from — "webapp" for App Service Web App Login.</summary>
    string Source,
    /// <summary>
    /// Whether somebody deliberately put this person in the staff directory or
    /// the role map.
    ///
    /// Signing in and being allowed in are two different questions, and they
    /// only looked like one question while a single tenant could reach the
    /// door. Once sign-in accepts more than one tenant, "authenticated" stops
    /// meaning anything about who this is.
    /// </summary>
    bool Recognised = false)
{
    public bool IsSupervisor => Roles.IsSupervisor(Role);

    /// <summary>Whether this person may do a particular thing. The only permission question worth asking.</summary>
    public bool Can(Capability capability) => Roles.Can(Role, capability);

    /// <summary>What gets written to updated_by, and shown in a job's history.</summary>
    public string Signature => Email.Length > 0 ? Email : UserId;
}
