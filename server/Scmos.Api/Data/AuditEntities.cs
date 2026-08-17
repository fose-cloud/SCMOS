namespace Scmos.Api.Data;

/// <summary>
/// One thing somebody changed.
///
/// Append-only, and never written by the same code that does the changing —
/// <see cref="Services.AuditService"/> is the only writer, so "was this
/// recorded" has one answer rather than one per call site.
///
/// The agreed fields are all here for a reason. Old and new value together are
/// what makes an entry arguable rather than merely informative; the reason is
/// what makes it useful a year later ("DGT capacity unavailable" explains a
/// carrier change that the values alone never will); and the address and session
/// are what distinguish a change somebody made from a change made in their name.
///
/// Nothing deletes from this table. That is the point of it.
/// </summary>
public class AuditEvent
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    /* ---- who ---- */

    /// <summary>The signature the rest of the system records — email, or the directory id.</summary>
    public string Who { get; set; } = "";

    /// <summary>Directory id (OP-01…), so a renamed account still resolves.</summary>
    public string WhoId { get; set; } = "";

    public string Role { get; set; } = "";

    /* ---- what ---- */

    /// <summary>A short verb: update · assign · approve · close · upload · status · delete-request.</summary>
    public string Action { get; set; } = "";

    /// <summary>What kind of thing: job · supplier · rate · incident · document · approval · register.</summary>
    public string Entity { get; set; } = "";

    /// <summary>Which one: the job key, the supplier id, the case reference.</summary>
    public string EntityId { get; set; } = "";

    /// <summary>A human label for the row, so the trail reads without joining.</summary>
    public string EntityLabel { get; set; } = "";

    /// <summary>The field that changed. Empty when the action is not a field edit.</summary>
    public string Field { get; set; } = "";

    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";

    /// <summary>
    /// Why. Required for the changes where the values alone do not explain
    /// themselves — see <see cref="Rules.AuditActions.NeedsReason"/>.
    /// </summary>
    public string Reason { get; set; } = "";

    /* ---- where from ---- */

    public string IpAddress { get; set; } = "";

    /// <summary>The platform's session id when there is one; otherwise the request id.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>web · api · import · migration · ai — how the change arrived.</summary>
    public string Source { get; set; } = "web";
}
