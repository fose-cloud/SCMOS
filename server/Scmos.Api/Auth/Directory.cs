using Scmos.Api.Rules;

namespace Scmos.Api.Auth;

/// <summary>
/// The people the register knows about.
///
/// This exists because job ownership has to survive real sign-in. The plan
/// workbooks name their operator as "Watsana"; Entra will introduce the same
/// person as <c>watsana.k@leschaco.co.th</c> with a full name. The id in the
/// middle is what both sides agree on, and it is what a job now stores.
/// </summary>
public record Operator(string Id, string Name, string Account, string Role);

public static class StaffDirectory
{
    public const string DefaultRole = Roles.Operation;

    /// <summary>
    /// Kept only for the places that still name roles. What a role may do is
    /// decided in <see cref="Roles"/>, by capability — an array of role names
    /// four files test against cannot say why any of them is on the list.
    /// </summary>
    public static readonly string[] SupervisorRoles =
        Roles.All.Where(role => Roles.IsSupervisor(role.Name)).Select(role => role.Name).ToArray();

    /// <summary>
    /// Kept in step with ACCOUNTS in app/scmos/nav.ts. The five operation users
    /// must stay spelled as they appear in the plan workbooks — that spelling is
    /// what the owner-id backfill matches on.
    /// </summary>
    public static readonly Operator[] All =
    [
        new("OP-01", "Watsana", "watsana", DefaultRole),
        new("OP-02", "Uthai", "uthai", DefaultRole),
        new("OP-03", "Ananya", "ananya", DefaultRole),
        new("OP-04", "Maliwan", "maliwan", DefaultRole),
        new("OP-05", "Jiratchaya", "jiratchaya", DefaultRole),
        new("SV-01", "Titchanatorn", "titchanatorn", Roles.Supervisor),
        new("AM-01", "Nattikorn", "nattikorn", Roles.AssistantManager),
        new("AD-01", "Admin", "admin", Roles.Admin),

        // The four roles that were defined and enforced but had nobody in them.
        // A capability set nobody signs in as is one nobody has tested: the
        // Subcontractor's inability to see the rate book is a rule this system
        // depends on, and until somebody could sign in and fail to see it, that
        // was an assertion rather than a fact.
        new("CS-01", "Customerservice", "cs", Roles.CustomerService),
        new("MG-01", "Management", "management", Roles.Management),
        new("VW-01", "Viewer", "viewer", Roles.Viewer),
        new("SC-01", "Subcontractor", "subcontractor", Roles.Subcontractor),
    ];

    public static bool IsSupervisor(string role) => Roles.IsSupervisor(role);

    /// <summary>
    /// The owner id for a name off the plan, or an empty string when the name is
    /// not one of the five. An unknown owner is left without an id rather than
    /// guessed at — the workspace shows those as unassigned instead of handing
    /// them to the wrong person.
    /// </summary>
    public static string IdForName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "";
        var first = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? trimmed;
        return All.FirstOrDefault(o => string.Equals(o.Name, first, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
    }

    /// <summary>
    /// Matches a signed-in identity to the directory: first on the local part of
    /// the email, then on the first word of the display name.
    /// </summary>
    public static Operator? Match(string email, string displayName)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        local = local.Trim().ToLowerInvariant();

        var byAccount = All.FirstOrDefault(o => o.Account == local);
        if (byAccount is not null) return byAccount;

        // watsana.k@… — the surname initial is part of the local part, so try the
        // stem before the first dot as well.
        var stem = local.Split('.')[0];
        var byStem = All.FirstOrDefault(o => o.Account == stem);
        if (byStem is not null) return byStem;

        var first = (displayName ?? "").Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is null ? null : All.FirstOrDefault(o => string.Equals(o.Name, first, StringComparison.OrdinalIgnoreCase));
    }
}
