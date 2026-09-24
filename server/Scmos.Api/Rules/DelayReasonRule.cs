using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// A reason proposed for the REASON / DELAY column of a shipment that
/// arrived late — asked for on 22 Sep 2026: "ให้ AI ช่วยใส่คำตอบในคอลัมน์
/// REASON/DELAY … โดยส่งเหตุผลหลัก ๆ คือ Delay due to Traffic Congestion,
/// Port Traffic Congestion … ให้ AI เสนอเหตุผล แล้วให้ Operation กดเลือกหรือ
/// Approve", for IMPORT and EXPORT alike.
///
/// <para>
/// The proposal is one of <see cref="Catalogue"/> — eight sentences, one per
/// <see cref="DelayCategory"/>, worded so <see cref="DelayReasons.Classify"/>
/// files each under its own category and the KPI counts it. Which one is
/// read off the evidence in this order: what the haulier said about the job
/// in LINE or from its TMS (the parser's own categories); failing that, the
/// department's two main reasons by the route — a port on the leg means
/// Port Traffic Congestion, anything else Delay due to Traffic Congestion —
/// and the reason on the proposal says which of the two it was, so the
/// owner picking another from the list is the expected outcome, not a
/// correction.
/// </para>
///
/// <para>
/// Only a shipment that is late by the KPI's own rule (past its plan beyond
/// the customer's grace), only within <see cref="LookbackDays"/>, only while
/// the column is blank, never a cancelled job. A reason typed by a person is
/// never proposed over; a proposal the owner rejected is not made again.
/// </para>
/// </summary>
public static partial class DelayReasonRule
{
    /// <summary>IMPORT's column: REASON / DELAY.</summary>
    public const string Field = "reason";
    /// <summary>EXPORT's column: the export layout has no REASON / DELAY, so the reason goes into REMARK ("สำหรับตารางงาน EXPORT กำหนดให้ใส่คำตอบในคอลัมน์ REMARK", 22 Sep 2026).</summary>
    public const string ExportField = "remark";
    public const string Label = "เหตุผล / ความล่าช้า";
    public const string ExportLabel = "หมายเหตุ (REMARK) — เหตุผลความล่าช้า";
    /// <summary>The rules' names all begin with this; a proposal is a delay reason by its rule, whichever column it goes to.</summary>
    public const string RulePrefix = "reason.";

    /// <summary>Which column the reason goes to for this category.</summary>
    public static string FieldFor(string category) => category.Trim().Equals("EXPORT", StringComparison.OrdinalIgnoreCase) ? ExportField : Field;
    public static bool IsReasonRule(string? rule) => (rule ?? "").StartsWith(RulePrefix, StringComparison.Ordinal);
    /// <summary>How far back a late shipment is asked about. Older than this, the owner is unlikely to remember and the KPI period has closed.</summary>
    public const int LookbackDays = 90;

    /// <summary>The reasons an owner may pick from — one per category, in the order the drawer lists them.</summary>
    public static readonly (DelayCategory Category, string Text)[] Catalogue =
    [
        (DelayCategory.Traffic, "Delay due to Traffic Congestion"),
        (DelayCategory.Port, "Port Traffic Congestion"),
        (DelayCategory.Truck, "Truck Breakdown"),
        (DelayCategory.Driver, "Driver Unavailable"),
        (DelayCategory.Depot, "Empty Container Not Ready at Depot"),
        (DelayCategory.Customer, "Waiting at Customer / Loading Queue"),
        (DelayCategory.Documentation, "Document Delay"),
        (DelayCategory.Other, "Other (see remark)"),
    ];

    public static string TextOf(DelayCategory category) => Catalogue.First(one => one.Category == category).Text;
    public static bool IsCatalogued(string? value) => Catalogue.Any(one => one.Text == (value ?? "").Trim());

    // A port or a container terminal on the leg: LCB, Laem Chabang, แหลมฉบัง, PAT, Bangkok Port, ICD, terminal, ท่าเรือ.
    [GeneratedRegex(@"\b(LCB|LCH|LAEM ?CHABANG|PAT|BANGKOK ?PORT|KLONG ?TOEY|ICD|TERMINAL|PORT)\b|แหลมฉบัง|ท่าเรือ|คลองเตย|ลาดกระบัง ?ICD", RegexOptions.IgnoreCase)]
    private static partial Regex PortPlaces();

    /// <summary>Whether this shipment is one a reason should be asked for today.</summary>
    public static bool Asks(JobRecord job, DateOnly today)
    {
        var cat = job.Cat.Trim().ToUpperInvariant();
        if (cat is not ("IMPORT" or "EXPORT")) return false;
        if (string.Equals(job.Status, JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)) return false;
        // A reason already recorded — in the column it goes to, or in REASON / DELAY on an export
        // from before the column moved — is an answer; a person's own words stand.
        if (Formats.Clean(job.Reason).Length > 0) return false;
        if (cat == "EXPORT" && Formats.Clean(job.Remark).Length > 0) return false;
        var late = JobRules.MinutesLate(job);
        if (late is null || late <= CustomerTerms.GraceMinutes(job.Customer, job.Type)) return false;
        var day = Formats.ParseDay(job.Date);
        return day is not null && day.Value >= today.AddDays(-LookbackDays) && day.Value <= today;
    }

    /// <summary>
    /// The reason to propose, or null when none should be: read off the
    /// haulier's messages first, the route second. <paramref name="messages"/>
    /// are what the haulier said about this job, newest first, as stored.
    /// </summary>
    public static Correction? Propose(JobRecord job, IReadOnlyList<string> messages, DateOnly today)
    {
        if (!Asks(job, today)) return null;
        var field = FieldFor(job.Cat);
        var was = field == ExportField ? job.Remark : job.Reason;
        var late = (int)Math.Round(JobRules.MinutesLate(job)!.Value);
        var lateness = $"รถถึงช้ากว่าแผน {late} นาที";

        foreach (var text in messages)
        {
            var read = DelayReasons.Classify(text);
            if (read.Category == DelayCategory.Other || read.Confidence < 0.6) continue;
            var excerpt = Formats.Clean(text);
            if (excerpt.Length > 80) excerpt = excerpt[..80] + "…";
            return new(field, was, TextOf(read.Category), "reason.carrier",
                $"{lateness} · ผู้ขนส่งแจ้ง \"{excerpt}\" ({read.Basis})");
        }

        var (from, to) = job.Leg;
        var places = string.Join(" · ", new[] { from, to, job.Plant, job.CyYard, job.Destination }.Where(place => place.Trim().Length > 0).Distinct());
        var port = new[] { from, to, job.Plant, job.CyYard, job.Destination }.FirstOrDefault(place => PortPlaces().IsMatch(place));
        return port is not null
            ? new(field, was, TextOf(DelayCategory.Port), "reason.route",
                $"{lateness} · ไม่มีข้อความจากผู้ขนส่ง — เส้นทางผ่านท่าเรือ/ลาน ({port.Trim()}) จึงเสนอเหตุผลหลัก; เลือกเหตุผลอื่นได้จากรายการ")
            : new(field, was, TextOf(DelayCategory.Traffic), "reason.route",
                $"{lateness} · ไม่มีข้อความจากผู้ขนส่ง — เสนอเหตุผลหลักของแผนก{(places.Length > 0 ? $" (เส้นทาง {places})" : "")}; เลือกเหตุผลอื่นได้จากรายการ");
    }
}
