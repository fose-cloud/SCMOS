using System.Text.Json;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Carrier;

/// <summary>What the Carrier Agent needs besides the job, prepared once per pass.</summary>
/// <param name="Priority">The workflow's own order for a customer and category (<see cref="WorkflowService.Rank"/>), keyed by upper-case customer and category.</param>
/// <param name="Attempts">Every request already made for a job, as the assignment rules read them.</param>
/// <param name="Supplier">The registered carrier a register spelling means (<see cref="WorkflowService.ResolveSupplier"/>), or null.</param>
public sealed record CarrierContext(
    IReadOnlyDictionary<(string Customer, string Category), IReadOnlyList<CarrierPriority>> Priority,
    IReadOnlyDictionary<string, IReadOnlyList<Attempt>> Attempts,
    Func<string, SupplierSummary?> Supplier);

/// <summary>
/// The Carrier Agent (AI Agent Platform specification §29) — SCMOS's
/// <c>vendor-agent</c>, connected at last: which carrier to ask for a job
/// still without one, and in what order. Rule first, no model, in shadow: it
/// recommends; a person asks, through the workflow, one carrier at a time
/// (AP-04 — enforced by <see cref="CarrierAssignment.CanRequest"/> and the
/// one-active-assignment index; the agent sends nothing).
///
/// <para>
/// The order is the workflow's own (<see cref="WorkflowService.Rank"/>: on-time
/// record for this customer once measurable, then experience), minus the
/// carriers already asked. The eligibility filter uses only what the register
/// states: a spelling the Supplier Register does not know cannot be asked (the
/// workflow refuses it); a carrier the register marks suspended or rejected is
/// not recommended. Compliance papers, capability flags and an unapproved
/// status are shown beside the candidate, not used to reorder — whether they
/// should block is a business rule still to be confirmed.
/// </para>
///
/// <para>
/// When a person then asks a carrier, the pass records who, and whether it was
/// the one recommended — the shadow comparison autonomy is only ever raised on.
/// </para>
/// </summary>
public static class CarrierAgent
{
    public const string Id = "vendor-agent";
    public const string DecisionType = "carrier_candidates";
    /// <summary>How far ahead a job still without a carrier is looked at.</summary>
    public const int AheadDays = 7;
    /// <summary>How many candidates the recommendation names, in order.</summary>
    public const int Named = 3;
    public const string AskPrefix = "Carrier.Ask:";

    private static readonly string[] Excluded = ["suspended", "rejected"];

    /// <summary>A job this agent looks at: open, planned from yesterday to a week ahead, no carrier on it, no request waiting or confirmed.</summary>
    public static bool InScope(JobRecord job, IReadOnlyList<Attempt> attempts, DateOnly today) =>
        !JobRules.IsDone(job.Status) && !WorkspaceTabs.IsCancelled(job.Status) && WorkspaceTabs.CountedInWorkspace(job.Cat)
        && Formats.Clean(job.Trucker).Length == 0
        && Formats.ParseDay(job.Date) is { } day && day >= today.AddDays(-1) && day <= today.AddDays(AheadDays)
        && !attempts.Any(one => one.Outcome is CarrierAssignment.Pending or CarrierAssignment.Confirmed);

    public static AgentResult? Assess(CachedJobRow row, DateTimeOffset now, CarrierContext context)
    {
        if (row.Record is not { } job || row.Key.Length == 0) return null;
        var today = DateOnly.FromDateTime(now.ToOffset(Formats.Zone).DateTime);
        var attempts = context.Attempts.TryGetValue(row.Key, out var asked) ? asked : [];
        if (!InScope(job, attempts, today)) return null;

        var key = row.Key;
        var source = $"job:{key}";
        var customer = Formats.Clean(job.Customer);
        var category = Formats.Clean(job.Cat).ToUpperInvariant();
        var priority = context.Priority.TryGetValue((customer.ToUpperInvariant(), category), out var ranked) ? ranked : [];
        var type = Formats.Clean(job.Type);
        var dangerous = Product(row.Raw).Split([' ', ',', '/', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Equals("DG", StringComparison.OrdinalIgnoreCase));

        var facts = new List<AgentFinding>
        {
            new($"ลูกค้า {(customer.Length > 0 ? customer : "—")} · {category} · {(type.Length > 0 ? type : "ไม่ระบุประเภทรถ")}", $"{source}.customer+cat+type"),
            new($"แผน {Formats.Clean(job.Date)} {Formats.Clean(job.PlanTime)}".Trim(), $"{source}.date+planTime"),
            new("ยังไม่มีผู้ขนส่ง และไม่มีคำขอที่รอคำตอบ", $"{source}.trucker+supplier_requests"),
        };
        foreach (var attempt in attempts.OrderBy(one => one.Rank))
            facts.Add(new($"ถามแล้ว #{attempt.Rank} {attempt.Carrier}: {attempt.Outcome}", $"{source}.supplier_requests"));
        if (dangerous) facts.Add(new("สินค้าระบุ DG", $"{source}.product"));

        var rules = new List<AgentFinding>
        {
            new("ถามทีละรายตามลำดับ; ข้ามลำดับได้เมื่อระบุเหตุผล", "CarrierAssignment.CanRequest"),
        };
        var codes = new List<string>();
        var evidence = new List<string> { source };
        var eligible = new List<string>();
        var remaining = priority.Where(one => !attempts.Any(a => string.Equals(a.Carrier, one.Carrier, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var candidate in remaining)
        {
            // Named in order until three can be asked; whoever comes after is not judged yet.
            if (eligible.Count >= Named) break;
            var name = candidate.Carrier;
            var supplier = context.Supplier(name);
            var tag = CarrierDirectory.Lookup.Key(name);
            if (supplier is null)
            {
                rules.Add(new($"#{candidate.Rank} {name} — ไม่อยู่ใน Supplier Register จึงส่งงานให้ไม่ได้", "WorkflowService.ResolveSupplier"));
                codes.Add("Carrier.NotRegistered:" + tag);
                continue;
            }
            if (Excluded.Contains(supplier.Status, StringComparer.OrdinalIgnoreCase))
            {
                rules.Add(new($"#{candidate.Rank} {name} — สถานะในทะเบียน {supplier.Status}", $"supplier:{supplier.Id}.status"));
                codes.Add("Carrier.Status:" + tag + ":" + supplier.Status.ToLowerInvariant());
                continue;
            }
            eligible.Add(name);
            evidence.Add($"supplier:{supplier.Id}");
            rules.Add(new($"#{candidate.Rank} {name} — {candidate.Basis}", "WorkflowService.PriorityFor"));
            var notes = new List<(string Text, string Source, string Code)>();
            if (!supplier.Status.Equals("approved", StringComparison.OrdinalIgnoreCase))
                notes.Add(($"สถานะในทะเบียน {supplier.Status}", $"supplier:{supplier.Id}.status", "status-" + supplier.Status.ToLowerInvariant()));
            if (supplier.ComplianceStatus is SupplierCompliance.State.Missing or SupplierCompliance.State.Expired)
                notes.Add(($"เอกสารบังคับ: {(supplier.ComplianceStatus == SupplierCompliance.State.Expired ? "มีฉบับหมดอายุ" : "ยังไม่ครบ")}", $"supplier:{supplier.Id}.compliance", "compliance-" + supplier.ComplianceStatus));
            if (JobVehicleType.IsTank(type) && !supplier.IsoTankCapable)
                notes.Add(("ทะเบียนไม่ได้ระบุว่ารับ ISO Tank", $"supplier:{supplier.Id}.isoTankCapable", "no-tank"));
            if (type.Contains("RF", StringComparison.OrdinalIgnoreCase) && !supplier.ReeferCapable)
                notes.Add(("ทะเบียนไม่ได้ระบุว่ารับตู้เย็น", $"supplier:{supplier.Id}.reeferCapable", "no-reefer"));
            if (dangerous && !supplier.DgCapable)
                notes.Add(("ทะเบียนไม่ได้ระบุว่ารับ DG", $"supplier:{supplier.Id}.dgCapable", "no-dg"));
            foreach (var (text, from, code) in notes)
            {
                rules.Add(new($"{name}: {text}", from));
                codes.Add($"Carrier.Note:{tag}:{code}");
            }
        }

        var soon = Formats.ParseDay(job.Date) is { } planned && planned <= today.AddDays(MonitorRules.SoonDays);
        var recommendations = new List<AgentFinding>();
        string status;
        string risk;
        string summary;
        if (eligible.Count == 0)
        {
            status = AgentResultRules.RequiresHumanReview;
            risk = soon ? "HIGH" : "MEDIUM";
            codes.Add("Carrier.NoEligible");
            rules.Add(new(priority.Count == 0 ? $"ยังไม่มีผู้ขนส่งที่เคยวิ่งงาน {category} ให้ลูกค้านี้" : "ผู้ขนส่งตามลำดับถูกถามหรือถูกคัดออกหมดแล้ว",
                "WorkflowService.PriorityFor"));
            recommendations.Add(new("เลือกผู้ขนส่งจาก Supplier Register แล้วระบุเหตุผลที่ข้ามลำดับ"));
            summary = "ยังไม่มีผู้ขนส่งที่แนะนำได้ตามลำดับ";
        }
        else
        {
            status = AgentResultRules.Completed;
            risk = soon ? "WATCH" : "";
            codes.Add(AskPrefix + CarrierDirectory.Lookup.Key(eligible[0]));
            for (var i = 1; i < eligible.Count; i++) codes.Add($"Carrier.Then{i}:{CarrierDirectory.Lookup.Key(eligible[i])}");
            var order = $"ถาม {eligible[0]} ก่อน";
            if (eligible.Count > 1) order += $" · ถ้าปฏิเสธหรือไม่ตอบ ถาม {eligible[1]}";
            if (eligible.Count > 2) order += $" · แล้วจึง {eligible[2]}";
            recommendations.Add(new(order));
            summary = $"แนะนำถาม {eligible[0]}";
        }

        return new AgentResult(Id, DecisionType, "job", key, status,
            $"{(Formats.Clean(job.JobCode).Length > 0 ? Formats.Clean(job.JobCode) : key)} · {summary}",
            facts.Take(AgentResultRules.MaxItems).ToList(), rules.Take(AgentResultRules.MaxItems).ToList(), [], [],
            recommendations, [], codes.Distinct().OrderBy(one => one, StringComparer.Ordinal).Take(AgentResultRules.MaxReferences).ToList(),
            evidence.Distinct().Take(AgentResultRules.MaxReferences).ToList(), risk);
    }

    /// <summary>The carrier the decision recommended asking first, from its rule references, or null.</summary>
    public static string? Recommended(IEnumerable<string> ruleReferences) =>
        ruleReferences.FirstOrDefault(one => one.StartsWith(AskPrefix, StringComparison.Ordinal))?[AskPrefix.Length..];

    private static string Product(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("product", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
}
