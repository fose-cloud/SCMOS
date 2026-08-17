namespace Scmos.Api.Rules;

/// <summary>
/// The eight things the operation is measured on.
///
/// Each one carries the base it was measured over and whether it can be measured
/// at all. That second part is the important one: a rate computed from no
/// records is not 100% and it is not 0%, it is unknown, and a management
/// dashboard that renders unknown as a green 100% is worse than one that renders
/// nothing. Every measure here can say "no data yet", and several currently do.
/// </summary>
public enum MeasureId
{
    OnTimeDelivery,
    OnTimePickup,
    ConfirmationSla,
    Delay,
    Accident,
    CarPar,
    Billing,
    SupplierPerformance,
}

/// <summary>How a measure reads: a rate out of a base, or a straight count.</summary>
public enum MeasureKind { Rate, Count }

public record MeasureDefinition(
    MeasureId Id,
    string English,
    string Thai,
    MeasureKind Kind,
    /// <summary>What has to exist before this can be measured — shown when it cannot.</summary>
    string Source,
    /// <summary>Higher is better for a rate; for a count, lower is better.</summary>
    bool HigherIsBetter);

public static class KpiMeasures
{
    public static readonly MeasureDefinition[] All =
    [
        new(MeasureId.OnTimeDelivery, "On-Time Delivery", "ส่งมอบตรงเวลา", MeasureKind.Rate,
            "วันและเวลาตามแผน กับวันและเวลาที่ถึงจริง ในทะเบียนงาน", true),

        new(MeasureId.OnTimePickup, "On-Time Pickup", "รับตู้ตรงเวลา", MeasureKind.Rate,
            "milestone รับตู้ที่บันทึกเวลาแผนและเวลาจริงไว้ (shipment_milestones)", true),

        new(MeasureId.ConfirmationSla, "Confirmation SLA", "ตอบยืนยันภายใน SLA", MeasureKind.Rate,
            "คำขอรถที่ผู้ขนส่งตอบกลับ (supplier_requests) และ pre-run ที่ได้รับคำตอบ (pre_run_checks)", true),

        new(MeasureId.Delay, "Delay", "ความล่าช้า", MeasureKind.Count,
            "รายการความล่าช้าที่บันทึกพร้อมหมวด (delay_records)", false),

        new(MeasureId.Accident, "Accident", "อุบัติเหตุ", MeasureKind.Count,
            "เคสอุบัติเหตุใน incident_cases", false),

        new(MeasureId.CarPar, "CAR / PAR", "CAR / PAR", MeasureKind.Count,
            "เคส CAR/PAR ที่เปิดอยู่และที่เกินกำหนด (incident_cases)", false),

        new(MeasureId.Billing, "Billing", "การวางบิล", MeasureKind.Rate,
            "ใบแจ้งหนี้จากผู้รับเหมา เทียบกับกำหนด 4 วันหลังงานเสร็จ — ยังไม่มีตารางใบแจ้งหนี้ในระบบ", true),

        new(MeasureId.SupplierPerformance, "Supplier Performance", "ผลงานผู้ขนส่ง", MeasureKind.Rate,
            "คะแนนรวมถ่วงน้ำหนักจากตรงเวลา ตอบยืนยัน และความล่าช้าที่เป็นความรับผิดชอบของผู้ขนส่ง", true),
    ];

    public static MeasureDefinition Of(MeasureId id) => All.First(measure => measure.Id == id);

    /// <summary>
    /// The supplier scorecard's weights.
    ///
    /// On-time carries the most because it is what the customer feels. The
    /// weights are here rather than buried in the calculation so a supplier
    /// meeting can argue with them.
    /// </summary>
    public const double WeightOnTime = 0.5;
    public const double WeightConfirmation = 0.3;
    public const double WeightDelayFree = 0.2;

    /// <summary>
    /// How much measured history a component needs before it may score a
    /// carrier. Below this the percentage is noise.
    /// </summary>
    public const int MinimumSample = 5;

    /// <summary>
    /// A carrier's score, or null when there is not enough evidence to judge
    /// them.
    ///
    /// Two rules keep this honest, both learned from getting it wrong:
    ///
    /// A component only counts once it has <see cref="MinimumSample"/> measured
    /// records behind it. One delivery that arrived on time is not a 100%
    /// record.
    ///
    /// Absence of delays is not evidence of performance. A carrier nobody has
    /// recorded anything against has no delays, and scoring that as perfect put
    /// the least-known carriers at the top of the list — which is the opposite
    /// of what a scorecard is for. Delay-free only counts alongside a component
    /// that was actually measured.
    /// </summary>
    public static int? Score(
        double? onTime, int onTimeBase,
        double? confirmation, int confirmationBase,
        double? delayFree, int jobs)
    {
        var hasOnTime = onTime is not null && onTimeBase >= MinimumSample;
        var hasConfirmation = confirmation is not null && confirmationBase >= MinimumSample;
        if (!hasOnTime && !hasConfirmation) return null;

        double total = 0, weight = 0;
        void Add(double? value, double share)
        {
            if (value is null) return;
            total += value.Value * share;
            weight += share;
        }

        if (hasOnTime) Add(onTime, WeightOnTime);
        if (hasConfirmation) Add(confirmation, WeightConfirmation);
        if (jobs >= MinimumSample) Add(delayFree, WeightDelayFree);

        if (weight == 0) return null;
        return (int)Math.Round(total / weight * 100, MidpointRounding.AwayFromZero);
    }
}
