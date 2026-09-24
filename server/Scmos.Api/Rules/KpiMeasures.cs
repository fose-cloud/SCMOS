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
    bool HigherIsBetter,
    /// <summary>
    /// What the team agreed to hit. Null where nobody has set one — an invented
    /// target is worse than none, because the first thing a target does is tell
    /// people which numbers to argue about.
    /// </summary>
    double? Target = null);

public static class KpiMeasures
{
    public static readonly MeasureDefinition[] All =
    [
        // The three targets are the ones the operation already works to. The rest
        // have none, and say so rather than being given a plausible round number.
        new(MeasureId.OnTimeDelivery, "On-Time Delivery", "ส่งมอบตรงเวลา", MeasureKind.Rate,
            "วันและเวลาตามแผน กับวันและเวลาที่ถึงจริง ในทะเบียนงาน", true, 95),

        new(MeasureId.Delay, "Delay", "ความล่าช้า", MeasureKind.Count,
            "รายการความล่าช้าที่บันทึกพร้อมหมวด (delay_records)", false),

        new(MeasureId.Accident, "Accident", "อุบัติเหตุ", MeasureKind.Count,
            "เคสอุบัติเหตุใน incident_cases", false),

        new(MeasureId.CarPar, "CAR / PAR", "CAR / PAR", MeasureKind.Count,
            "เคส CAR/PAR ที่เปิดอยู่และที่เกินกำหนด (incident_cases)", false),

        new(MeasureId.Billing, "Billing", "การวางบิล", MeasureKind.Rate,
            $"ใบแจ้งหนี้จากผู้รับเหมา เทียบกับกำหนด {DocumentChecklist.InvoiceDays} วันหลังงานเสร็จ — ยังไม่มีตารางใบแจ้งหนี้ในระบบ", true),

        new(MeasureId.SupplierPerformance, "Supplier Performance", "ผลงานผู้ขนส่ง", MeasureKind.Rate,
            "Carrier Scorecard: อุบัติเหตุเล็กน้อย 15% · อุบัติเหตุใหญ่ 35% · รายงานความเสียหาย 20% · ความพร้อมรถ 10% · ส่งมอบตรงเวลา 10% · ความพึงพอใจลูกค้า 10%", true),
    ];

    public static MeasureDefinition Of(MeasureId id) => All.First(measure => measure.Id == id);
}

/// <summary>Where a delay figure came from, so the screen can say how much to trust it.</summary>
public enum DelayEvidence
{
    /// <summary>Nothing to go on. The figure is null, not zero.</summary>
    None,

    /// <summary>
    /// From the register's own statuses: jobs currently held or delayed. It is
    /// real evidence and it undercounts — a job delayed mid-run and then
    /// completed leaves no trace in its status. Better than the alternative,
    /// which was reporting a perfect record from an empty table.
    /// </summary>
    Status,

    /// <summary>From delay_records, where somebody categorised and attributed it.</summary>
    Records,
}
