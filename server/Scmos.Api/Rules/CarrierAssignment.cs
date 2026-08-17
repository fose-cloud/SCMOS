namespace Scmos.Api.Rules;

/// <summary>
/// Sequential carrier assignment.
///
/// The approved process asks one carrier at a time, in priority order, and waits
/// for an answer before asking the next. Sending one booking to three carriers
/// at once gets a truck faster and is a different process — one where the
/// company can end up committed to two of them, and where a carrier's answer
/// time stops meaning anything because they were racing.
///
/// These rules exist because a screen cannot be trusted to enforce them. Every
/// one of them is checked in the backend, on the way in, and each returns the
/// reason it refused so the operator is told what the process requires rather
/// than being left with a dead button.
/// </summary>
public enum AssignmentRule
{
    /// <summary>Only one request may be open on a booking at a time.</summary>
    OneAtATime,

    /// <summary>The open request must be answered or cancelled before the next carrier is asked.</summary>
    CloseBeforeNext,

    /// <summary>Carriers are asked in priority order; skipping one has to be justified.</summary>
    PriorityOrder,

    /// <summary>A truck may only be assigned to a carrier that confirmed.</summary>
    ConfirmedBeforeAssign,

    /// <summary>The same carrier is not asked twice for the same booking.</summary>
    NoRepeatCarrier,
}

public record RuleBreach(AssignmentRule Rule, string Message);

/// <summary>One carrier's place in the queue for a booking.</summary>
public record CarrierPriority(int Rank, string Carrier, int? Price, string Basis);

/// <summary>What has already been asked, in the shape the rules need to judge it.</summary>
public record Attempt(string Carrier, string Outcome, int Rank);

public static class CarrierAssignment
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";

    public static readonly string[] Outcomes = [Pending, Confirmed, "rejected", "cancelled", "no-response"];

    /// <summary>An outcome that closes a request, so the next carrier may be asked.</summary>
    public static bool IsClosed(string outcome) =>
        outcome is not Pending && Outcomes.Contains(outcome);

    /// <summary>
    /// Whether this carrier may be asked now.
    ///
    /// `priority` is the approved order for this booking. It may be empty — a
    /// lane nobody has quoted still has to be bookable — and when it is, the
    /// order rule cannot apply and does not pretend to.
    /// </summary>
    public static RuleBreach? CanRequest(
        string carrier,
        IReadOnlyList<Attempt> attempts,
        IReadOnlyList<CarrierPriority> priority,
        string? skipReason)
    {
        var name = carrier.Trim();
        if (name.Length == 0)
            return new RuleBreach(AssignmentRule.OneAtATime, "ต้องระบุผู้ขนส่ง");

        var open = attempts.FirstOrDefault(a => a.Outcome == Pending);
        if (open is not null)
        {
            return new RuleBreach(AssignmentRule.CloseBeforeNext,
                $"ยังรอคำตอบจาก {open.Carrier} อยู่ — ต้องบันทึกผล (ยืนยัน / ปฏิเสธ / ยกเลิก) ก่อนถามเจ้าถัดไป");
        }

        if (attempts.Any(a => a.Outcome == Confirmed))
        {
            var held = attempts.First(a => a.Outcome == Confirmed).Carrier;
            return new RuleBreach(AssignmentRule.OneAtATime,
                $"{held} ยืนยันรับงานแล้ว — ไม่ต้องถามเจ้าอื่นอีก");
        }

        if (attempts.Any(a => string.Equals(a.Carrier, name, StringComparison.OrdinalIgnoreCase)))
        {
            return new RuleBreach(AssignmentRule.NoRepeatCarrier,
                $"เคยถาม {name} ไปแล้วสำหรับงานนี้");
        }

        // Priority order. Everyone ahead of this carrier in the approved order
        // must already have been asked, unless the operator says why they were
        // passed over — a reason that is then on the record.
        if (priority.Count > 0 && string.IsNullOrWhiteSpace(skipReason))
        {
            var wanted = priority.FirstOrDefault(p =>
                !attempts.Any(a => string.Equals(a.Carrier, p.Carrier, StringComparison.OrdinalIgnoreCase)));

            if (wanted is not null && !string.Equals(wanted.Carrier, name, StringComparison.OrdinalIgnoreCase))
            {
                var price = wanted.Price is null ? "" : $" ({wanted.Price:N0} บาท)";
                return new RuleBreach(AssignmentRule.PriorityOrder,
                    $"ลำดับถัดไปตามกระบวนการคือ {wanted.Carrier}{price} — " +
                    $"ถ้าจะข้ามไป {name} ต้องระบุเหตุผล");
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a truck may be assigned to this carrier.
    ///
    /// This is the rule the booking screen used to be able to walk past: it set
    /// the carrier straight onto the job, which produced a booking nobody had
    /// agreed to take.
    /// </summary>
    public static RuleBreach? CanAssign(string carrier, IReadOnlyList<Attempt> attempts)
    {
        var name = carrier.Trim();
        if (name.Length == 0)
            return new RuleBreach(AssignmentRule.ConfirmedBeforeAssign, "ต้องระบุผู้ขนส่ง");

        var confirmed = attempts.FirstOrDefault(a =>
            a.Outcome == Confirmed && string.Equals(a.Carrier, name, StringComparison.OrdinalIgnoreCase));

        if (confirmed is not null) return null;

        var anyConfirmed = attempts.FirstOrDefault(a => a.Outcome == Confirmed);
        if (anyConfirmed is not null)
        {
            return new RuleBreach(AssignmentRule.ConfirmedBeforeAssign,
                $"{anyConfirmed.Carrier} เป็นเจ้าที่ยืนยันรับงานนี้ ไม่ใช่ {name}");
        }

        return new RuleBreach(AssignmentRule.ConfirmedBeforeAssign,
            $"{name} ยังไม่ได้ยืนยันรับงาน — ต้องขอกำลังรถและได้รับการยืนยันก่อนมอบหมาย");
    }

    /// <summary>
    /// The next carrier the process says to ask, or null when the order is
    /// exhausted or nobody has quoted this lane.
    /// </summary>
    public static CarrierPriority? NextInOrder(
        IReadOnlyList<Attempt> attempts, IReadOnlyList<CarrierPriority> priority) =>
        priority.FirstOrDefault(p =>
            !attempts.Any(a => string.Equals(a.Carrier, p.Carrier, StringComparison.OrdinalIgnoreCase)));
}
