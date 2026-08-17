namespace Scmos.Api.Rules;

/// <summary>
/// Pre-run verification.
///
/// The day before a shipment runs, the carrier is sent the list of what they are
/// carrying and asked to confirm the truck and driver. Most of the time they
/// confirm; sometimes they correct it — a different plate, a different driver —
/// and that correction is the thing worth capturing, because a plate that
/// changed the night before is why a gate pass fails in the morning.
///
/// The SLA is on the answer, not on the sending. A list sent at 16:00 and
/// answered at 16:20 is a carrier doing their job; the same list unanswered at
/// 08:00 the next morning is a truck nobody can vouch for.
/// </summary>
public enum PreRunOutcome
{
    /// <summary>Sent, no answer yet.</summary>
    Pending,
    /// <summary>Carrier confirmed the truck and driver as sent.</summary>
    Confirmed,
    /// <summary>Carrier answered but changed something.</summary>
    Corrected,
    /// <summary>Closed without an answer.</summary>
    NoResponse,
}

/// <summary>How far the chase has gone. Each step is a real action somebody took.</summary>
public enum Escalation
{
    None,
    /// <summary>The SLA passed with no answer. Raised by the system, not a person.</summary>
    Alert,
    /// <summary>Somebody chased the carrier.</summary>
    FollowUp,
    /// <summary>Handed up — the carrier is not answering.</summary>
    Escalated,
}

public static class PreRun
{
    /// <summary>
    /// How long a carrier has to answer, in minutes.
    ///
    /// Two hours is what the operation already measured itself against before
    /// any of this was written down. It is configuration, not law — set
    /// PreRun:SlaMinutes to change it — but it has to have a value, because
    /// "late" is meaningless without one.
    /// </summary>
    public const int DefaultSlaMinutes = 120;

    /// <summary>Minutes taken to answer, or the minutes elapsed so far when still open.</summary>
    public static int MinutesTaken(DateTimeOffset sentAt, DateTimeOffset? respondedAt, DateTimeOffset now) =>
        (int)Math.Round(((respondedAt ?? now) - sentAt).TotalMinutes);

    /// <summary>
    /// Whether this check met the SLA.
    ///
    /// An unanswered check that is still inside the window is not a breach yet,
    /// and is not a pass either — it is simply not decided, which is why this
    /// returns null rather than false.
    /// </summary>
    public static bool? MetSla(DateTimeOffset sentAt, DateTimeOffset? respondedAt, DateTimeOffset now, int slaMinutes)
    {
        if (respondedAt is not null)
            return (respondedAt.Value - sentAt).TotalMinutes <= slaMinutes;

        return (now - sentAt).TotalMinutes > slaMinutes ? false : null;
    }

    /// <summary>
    /// A check is ready when the carrier answered inside the SLA. A correction
    /// still counts as ready — they told us in time, and what they told us is
    /// now on the job.
    /// </summary>
    public static bool IsReady(PreRunOutcome outcome, bool? metSla) =>
        metSla == true && outcome is PreRunOutcome.Confirmed or PreRunOutcome.Corrected;

    /// <summary>
    /// The escalation the system itself raises. People move it further along;
    /// this only decides when the first alert is due, so an unanswered list
    /// cannot sit quietly.
    /// </summary>
    public static Escalation DueEscalation(Escalation current, PreRunOutcome outcome, bool? metSla)
    {
        if (outcome is not PreRunOutcome.Pending) return current;
        if (metSla == false && current == Escalation.None) return Escalation.Alert;
        return current;
    }

    /// <summary>The next step a person may take. Null when there is nothing to chase.</summary>
    public static Escalation? NextStep(Escalation current, PreRunOutcome outcome) =>
        outcome is not PreRunOutcome.Pending
            ? null
            : current switch
            {
                Escalation.None or Escalation.Alert => Escalation.FollowUp,
                Escalation.FollowUp => Escalation.Escalated,
                _ => null,
            };

    public static string Describe(Escalation step) => step switch
    {
        Escalation.Alert => "เกิน SLA — ยังไม่ตอบ",
        Escalation.FollowUp => "ตามแล้ว",
        Escalation.Escalated => "ส่งต่อผู้บังคับบัญชา",
        _ => "ปกติ",
    };
}
