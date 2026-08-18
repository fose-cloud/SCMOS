namespace Scmos.Api.Data;

/// <summary>
/// A person the system knows about.
///
/// This used to be a hardcoded array in <c>StaffDirectory</c>, which meant
/// adding a colleague was a code change, a build and a deployment. It is a table
/// now so an administrator can do it from the Administration screen.
///
/// Three fields, three different jobs, and they are not interchangeable:
///
/// <see cref="Email"/> is what the sign-in token carries and what a person is
/// recognised by. <see cref="Name"/> is the spelling the plan workbooks use, and
/// the owner-id backfill matches on it — change it and jobs stop finding their
/// operator. <see cref="Id"/> is what a job actually stores, and it never
/// changes once work has been assigned to it.
/// </summary>
public class StaffMember
{
    /// <summary>OP-01, SV-01, AD-01… What a job's `opId` holds. Immutable once used.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The sign-in address. For a guest account this is the tenant's UPN form
    /// (<c>name_domain.com#EXT#@tenant.onmicrosoft.com</c>), not the original
    /// email — the token carries the former, so that is what has to be stored.
    /// </summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// As the plan workbooks spell it. The owner-id backfill matches the first
    /// word of an operator's name against this.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>The local sign-in name for development. Empty for people added later.</summary>
    public string Account { get; set; } = "";

    /// <summary>One of <see cref="Rules.Roles"/>. Decides the whole capability set.</summary>
    public string Role { get; set; } = "";

    /// <summary>
    /// Set false instead of deleting. A person who has left still owns the jobs
    /// they worked, and a deleted row would orphan every one of them — an
    /// inactive person keeps their history and stops being able to sign in.
    /// </summary>
    public bool Active { get; set; } = true;

    public string Note { get; set; } = "";

    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
