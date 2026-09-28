using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Validation;

/// <summary>
/// The Validation Agent (AI Agent Platform specification §28) — the checks
/// SCMOS already makes, run over the work that is about to happen and kept as
/// a history, rule first and with no model:
///
/// <list type="bullet">
/// <item>a value present but unreadable — date, time, container, weight, phone,
/// plate, a status off the category's ladder (<see cref="JobRules.Validate"/>);</item>
/// <item>a vehicle type that is not on the department's list
/// (<see cref="JobVehicleType.IsKnown"/>);</item>
/// <item>an export whose truck is due after the yard closes
/// (<see cref="JobRules.GateInRisk"/>);</item>
/// <item>the container and, on an export, the seal still missing once the run is
/// inside the monitor's window (<see cref="JobRules.Gaps"/>).</item>
/// </list>
///
/// <para>
/// PASS writes nothing; WARNING and NEEDS_INFORMATION are decisions a person
/// answers. BLOCKED is reserved for a deterministic rule that already blocks
/// continuation — there is none among these, so none is claimed. A truck or a
/// driver still missing is the OTD Agent's finding, not this one's, so a job is
/// not raised twice for the same gap.
/// </para>
/// </summary>
public static class ValidationAgent
{
    public const string Id = "validation-agent";
    public const string DecisionType = "validation";

    public const string Pass = "PASS";
    public const string Warning = "WARNING";
    public const string NeedsInformation = "NEEDS_INFORMATION";
    public const string Blocked = "BLOCKED";

    /// <summary>How far ahead the register is checked: the work of the coming week.</summary>
    public const int AheadDays = 7;

    public static AgentResult? Assess(CachedJobRow row, DateTimeOffset now)
    {
        if (row.Record is not { } job) return null;
        var key = row.Key;
        if (key.Length == 0 || JobRules.IsDone(job.Status) || WorkspaceTabs.IsCancelled(job.Status)) return null;
        var today = DateOnly.FromDateTime(now.ToOffset(Formats.Zone).DateTime);
        var day = Formats.ParseDay(job.Date);
        // An unreadable plan date is itself a finding (Validate says so); an empty one is the plan's gap.
        if (day is { } planned && (planned < today.AddDays(-1) || planned > today.AddDays(AheadDays))) return null;
        if (day is null && Formats.Clean(job.Date).Length == 0) return null;

        var source = $"job:{key}";
        var facts = new List<AgentFinding>();
        var rules = new List<AgentFinding>();
        var recommendations = new List<AgentFinding>();
        var codes = new List<string>();
        var needsInformation = false;

        foreach (var issue in JobRules.Validate(job))
        {
            facts.Add(new($"{issue.Label}: \"{issue.Value}\"", $"{source}.{issue.Field}"));
            rules.Add(new($"{issue.Label} {issue.Message} — ต้องเป็น {issue.Expected}", $"JobRules.Validate.{issue.Field}"));
            recommendations.Add(new($"แก้ {issue.Label} ในตารางงานให้เป็น {issue.Expected}"));
            codes.Add($"VALIDATE.{issue.Field.ToUpperInvariant()}");
            if (issue.Severity == Severity.Error) needsInformation = true;
        }

        var type = Formats.Clean(job.Type);
        if (type.Length > 0 && !JobVehicleType.IsKnown(type))
        {
            facts.Add(new($"ประเภทรถ \"{type}\"", $"{source}.type"));
            rules.Add(new("ไม่อยู่ในรายการประเภทรถของแผนก", "JobVehicleType.Codes"));
            var canonical = JobVehicleType.Canonical(type);
            recommendations.Add(new(JobVehicleType.IsKnown(canonical) && canonical != type
                ? $"เปลี่ยนประเภทรถเป็น {canonical}" : "เลือกประเภทรถจากรายการ"));
            codes.Add("VEHICLE_TYPE_UNKNOWN");
        }

        if (JobRules.GateInRisk(job))
        {
            facts.Add(new($"Closing {Formats.Clean(job.ClosingDate)} {Formats.Clean(job.ClosingTime)}", $"{source}.closingTime"));
            facts.Add(new($"รถ {(Formats.Clean(job.ArrTime).Length > 0 ? "ถึง " + Formats.Clean(job.ArrTime) : "ตามแผน " + Formats.Clean(job.PlanTime))}",
                $"{source}.{(Formats.Clean(job.ArrTime).Length > 0 ? "arrTime" : "planTime")}"));
            rules.Add(new("รถมีกำหนดถึงหลังเวลาปิดรับตู้ (closing)", "JobRules.GateInRisk"));
            recommendations.Add(new("ยืนยันเวลาปิดรับตู้กับสายเรือ หรือขอให้รถเข้าเร็วขึ้น"));
            codes.Add("GATE_IN_RISK");
        }

        if (day is { } near && near <= today.AddDays(MonitorRules.SoonDays))
        {
            foreach (var gap in JobRules.Gaps(job))
            {
                var (field, label, code) = gap switch
                {
                    "Container missing" => ("container", "เลขตู้", "GAP.CONTAINER"),
                    "Seal missing" => ("seal", "เลขซีล", "GAP.SEAL"),
                    _ => ("", "", ""),
                };
                if (code.Length == 0) continue;
                facts.Add(new($"ยังไม่มี{label}", $"{source}.{field}"));
                rules.Add(new($"ภายใน {MonitorRules.SoonDays} วันก่อนวิ่ง ต้องมี{label}", "JobRules.Gaps"));
                recommendations.Add(new($"ขอ{label}จากลูกค้าหรือสายเรือ แล้วบันทึกในตารางงาน"));
                codes.Add(code);
                needsInformation = true;
            }
        }

        if (codes.Count == 0) return null;
        var outcome = needsInformation ? NeedsInformation : Warning;
        return new AgentResult(Id, DecisionType, "job", key,
            needsInformation ? AgentResultRules.InsufficientInformation : AgentResultRules.RequiresHumanReview,
            $"{(Formats.Clean(job.JobCode).Length > 0 ? Formats.Clean(job.JobCode) : key)} · {(needsInformation ? "ข้อมูลยังไม่ครบหรืออ่านไม่ได้" : "ควรตรวจ")} ({codes.Count} รายการ)",
            facts.Take(AgentResultRules.MaxItems).ToList(), rules.Take(AgentResultRules.MaxItems).ToList(), [], [],
            recommendations.Distinct().Take(AgentResultRules.MaxItems).ToList(), [],
            codes.Prepend("Validation." + outcome).Distinct().OrderBy(one => one, StringComparer.Ordinal).ToList(),
            [source], needsInformation ? "MEDIUM" : "LOW");
    }

    /// <summary>The outcome a stored decision stands for, from its rule references.</summary>
    public static string OutcomeOf(IEnumerable<string> ruleReferences) =>
        ruleReferences.FirstOrDefault(one => one.StartsWith("Validation.", StringComparison.Ordinal))?["Validation.".Length..] ?? Pass;
}
