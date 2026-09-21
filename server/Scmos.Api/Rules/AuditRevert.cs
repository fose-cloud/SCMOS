namespace Scmos.Api.Rules;

/// <summary>
/// Putting a job's cell back to what the audit says it was — asked for on
/// 21 September 2026, after a supervisor handed a colleague's jobs to the
/// wrong person in one batch and the department wanted them back without
/// keying forty rows by hand.
///
/// <para>
/// A revert is not an undo button on the past: it is a new edit, made by
/// the person pressing it, that writes the audit row's <em>old</em> value
/// into the cell — and it is refused when the cell no longer holds the
/// row's <em>new</em> value, because then somebody has been there since and
/// the row is no longer the last word. Only the cells the audit keeps a row
/// for (<see cref="AuditActions.For"/>) can come back, and only on a job.
/// The revert leaves its own row, naming the row it reversed.
/// </para>
///
/// <para>Pure. <c>--check-audit-revert</c> proves it.</para>
/// </summary>
public static class AuditRevert
{
    /// <summary>The entity a revert may touch. Suppliers, rates and cases have their own screens and their own undo.</summary>
    public const string Entity = "job";

    /// <summary>The most rows one request may revert — one batch of the grid's size.</summary>
    public const int MaxRows = 200;

    /// <summary>A reason is what the revert's own audit row will say; four characters is the register's usual floor.</summary>
    public const int MinReason = 4;

    /// <summary>The actions whose rows describe one cell going from one value to another.</summary>
    public static readonly string[] Actions = [AuditActions.Assign, AuditActions.Update, AuditActions.StatusChange, AuditActions.CarrierChange];

    /// <summary>Why a row cannot be reverted, or null when it can.</summary>
    public static string? Problem(string entity, string action, string fieldLabel)
    {
        if (!string.Equals(entity, Entity, StringComparison.OrdinalIgnoreCase)) return "not-a-job";
        if (!Actions.Contains(action, StringComparer.OrdinalIgnoreCase)) return "not-a-cell-change";
        if (AuditActions.FieldOf(fieldLabel) is null) return "unknown-cell";
        return null;
    }

    /// <summary>Whether a row is one the screen may offer to revert.</summary>
    public static bool Reversible(string entity, string action, string fieldLabel) => Problem(entity, action, fieldLabel) is null;

    /// <summary>
    /// The authority a revert needs: the same as making the change from the
    /// grid — handing a job to somebody is <see cref="Capability.AssignJobs"/>,
    /// any other cell on a colleague's job is <see cref="Capability.EditAnyJob"/>.
    /// </summary>
    public static Capability Required(string field) =>
        string.Equals(field, "op", StringComparison.OrdinalIgnoreCase) ? Capability.AssignJobs : Capability.EditAnyJob;

    /// <summary>
    /// Whether the cell still holds what the audit row says it became. A
    /// value is compared as the register keeps it: trimmed, and for a person
    /// or a carrier without regard to case, since a name is the same name
    /// however it was typed.
    /// </summary>
    public static bool StillHolds(string field, string current, string recorded)
    {
        var a = (current ?? "").Trim();
        var b = (recorded ?? "").Trim();
        return field is "op" or "trucker"
            ? string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            : string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>The reason the revert's own audit row carries: the person's words, and which row it reverses.</summary>
    public static string ReasonFor(long auditId, string reason) =>
        $"{Formats.Clean(reason)} — ย้อนกลับรายการ #{auditId}";

    /// <summary>Rows are reverted newest first, so a cell changed twice goes back through both steps in order.</summary>
    public static IEnumerable<T> InOrder<T>(IEnumerable<T> rows, Func<T, long> id) => rows.OrderByDescending(id);
}
