using System.Text.Json;
using System.Text.Json.Nodes;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Carrier;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// The Carrier Agent (Agent Platform, 28 Sep 2026) against a fixed clock —
/// 10:00 in Bangkok on 28 September: the workflow's own ranking, pulled out
/// pure and read unchanged; the order after those already asked; who the
/// Supplier Register bars; what is shown beside a candidate without moving it;
/// and every result held to the decision contract.
/// </summary>
static class CarrierAgentChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T03:00:00Z");

    private static JsonObject Job(string key, params (string Field, string Value)[] set)
    {
        var job = new JsonObject
        {
            ["key"] = key, ["cat"] = "IMPORT", ["opId"] = "OP-C1", ["op"] = "Operator", ["date"] = "29/09/2026", ["planTime"] = "09:00",
            ["status"] = JobStatus.WaitingSupplier, ["trucker"] = "", ["licence"] = "", ["driver"] = "", ["contact"] = "",
            ["customer"] = "BASF", ["type"] = "1X40'", ["container"] = "", ["seal"] = "", ["arrDate"] = "", ["arrTime"] = "",
            ["jobCode"] = "J-" + key, ["product"] = "",
        };
        foreach (var (field, value) in set) job[field] = value;
        return job;
    }

    private static CachedJobRow Row(JsonObject job)
    {
        var raw = JsonDocument.Parse(job.ToJsonString()).RootElement.Clone();
        return new CachedJobRow(job["key"]!.GetValue<string>(), job["trucker"]!.GetValue<string>(), raw, JobRecord.From(raw));
    }

    /// <summary>A delivered history job: planned 08:00, arrived <paramref name="arrived"/>.</summary>
    private static (string, JobRecord?) Ran(string carrier, string arrived) =>
        (carrier, Row(Job("H", ("trucker", carrier), ("status", "COMPLETED"), ("planTime", "08:00"), ("date", "01/09/2026"),
            ("arrDate", arrived.Length > 0 ? "01/09/2026" : ""), ("arrTime", arrived))).Record);

    private static SupplierSummary Supplier(int id, string name, string status = "approved", string compliance = SupplierCompliance.State.Valid,
        bool dg = false, bool reefer = false, bool tank = false) => new(
        id, "C" + id, name, status, "Trucking", "", dg, reefer, tank, false, 0, 0, 0, 0, null, "", [], 0, "", "", "",
        [], compliance, "", "", "", "", "", "", "", "", "", "", "", "", true, 0);

    public static void Run(Action<bool, string> check)
    {
        var known = new AgentRegistry().All.Select(agent => agent.Id).ToList();
        var every = new List<AgentResult>();
        AgentResult? Keep(AgentResult? result) { if (result is not null) every.Add(result); return result; }

        /* ---------------- the workflow's ranking, read unchanged ---------------- */
        var history = new List<(string, JobRecord?)>();
        for (var i = 0; i < 6; i++) history.Add(Ran("SHORE", "07:55"));                         // 6/6 on time
        for (var i = 0; i < 6; i++) history.Add(Ran("ACN", i < 3 ? "07:50" : "09:30"));        // 3/6
        for (var i = 0; i < 10; i++) history.Add(Ran("TATIYAPOL", i < 2 ? "07:58" : ""));      // 10 jobs, 2 measured
        history.Add(Ran("NEWCO", "07:40"));                                                    // one job on time is not 100%
        var ranked = WorkflowService.Rank(history);
        check(ranked.Select(one => one.Carrier).SequenceEqual(["SHORE", "ACN", "TATIYAPOL", "NEWCO"])
            && ranked.Select(one => one.Rank).SequenceEqual([1, 2, 3, 4]) && ranked[0].Basis.Contains("100%") && ranked[3].Basis.Contains("ยังน้อยเกิน"),
            "carrier: the workflow's order — rated on time first, then experience; one run on time is not a 100% record");

        var carriers = new[] { (1, "SHORE LOGISTICS", "SHR"), (2, "A.C.N. TRANSPORT", "ACN"), (3, "TATIYAPOL", "TTP") };
        var aliases = new[] { (1, "SHORE"), (4, "TATIYAPON") };
        check(WorkflowService.ResolveSupplier("shore", carriers, aliases) == 1 && WorkflowService.ResolveSupplier("A C N", carriers, aliases) == 2
            && WorkflowService.ResolveSupplier("tatiyapol", carriers, aliases) == 3 && WorkflowService.ResolveSupplier("TATIYAPON", carriers, aliases) == 4
            && WorkflowService.ResolveSupplier("NEWCO", carriers, aliases) is null && WorkflowService.ResolveSupplier(" .- ", carriers, aliases) is null,
            "carrier: a spelling resolves by name or code, then alias, letters and digits only — the rule a request must pass");

        /* ---------------- the order after those already asked ---------------- */
        var register = new Dictionary<string, SupplierSummary?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHORE"] = Supplier(1, "SHORE LOGISTICS"), ["ACN"] = Supplier(2, "A.C.N. TRANSPORT"),
            ["TATIYAPOL"] = Supplier(3, "TATIYAPOL"), ["NEWCO"] = null,
        };
        CarrierContext Context(IReadOnlyList<CarrierPriority>? order = null, IReadOnlyList<Attempt>? asked = null,
            IDictionary<string, SupplierSummary?>? suppliers = null, string key = "C1") => new(
            new Dictionary<(string, string), IReadOnlyList<CarrierPriority>> { [("BASF", "IMPORT")] = order ?? ranked },
            new Dictionary<string, IReadOnlyList<Attempt>> { [key] = asked ?? [] },
            name => (suppliers ?? register).TryGetValue(name, out var found) ? found : null);
        AgentResult? Assess(JsonObject job, CarrierContext? context = null) => CarrierAgent.Assess(Row(job), Now, context ?? Context());

        var fresh = Keep(Assess(Job("C1")));
        check(fresh is { Status: AgentResultRules.Completed, RiskLevel: "WATCH", DecisionType: CarrierAgent.DecisionType }
            && CarrierAgent.Recommended(fresh.RuleReferences) == "SHORE"
            && fresh.RuleReferences.Contains("Carrier.Then1:ACN") && fresh.RuleReferences.Contains("Carrier.Then2:TATIYAPOL")
            && fresh.Recommendations.Single().Text == "ถาม SHORE ก่อน · ถ้าปฏิเสธหรือไม่ตอบ ถาม ACN · แล้วจึง TATIYAPOL",
            "carrier: tomorrow's job with no carrier — ask SHORE first, then ACN, then TATIYAPOL, one at a time");
        var afterReject = Keep(Assess(Job("C1"), Context(asked: [new("SHORE", CarrierAssignment.Rejected, 1)])));
        check(afterReject is not null && CarrierAgent.Recommended(afterReject.RuleReferences) == "ACN"
            && afterReject.Facts.Any(fact => fact.Text.Contains("SHORE") && fact.Text.Contains(CarrierAssignment.Rejected))
            && AiDecisionLog.FingerprintOf(afterReject) != AiDecisionLog.FingerprintOf(fresh!),
            "carrier: SHORE declined — ACN is next, the refusal is a fact, and the finding changes (the old one is superseded)");
        var expired = Assess(Job("C1"), Context(asked: [new("shore", CarrierAssignment.Expired, 1)]));
        check(expired is not null && CarrierAgent.Recommended(expired.RuleReferences) == "ACN",
            "carrier: a request that expired unanswered counts as asked, whatever the spelling's case");
        check(Assess(Job("C1"), Context(asked: [new("SHORE", CarrierAssignment.Pending, 1)])) is null
            && Assess(Job("C1"), Context(asked: [new("SHORE", CarrierAssignment.Confirmed, 1)])) is null,
            "carrier: a request waiting or confirmed — nothing to recommend while a carrier holds the job (AP-04)");
        var later = Assess(Job("C1", ("date", "03/10/2026")));
        check(later is { RiskLevel: "" } && CarrierAgent.Recommended(later.RuleReferences) == "SHORE",
            "carrier: five days out it still recommends, with no risk — the monitor's window is two days");

        /* ---------------- scope ---------------- */
        check(Assess(Job("C1", ("trucker", "SHORE"))) is null && Assess(Job("C1", ("status", "COMPLETED"))) is null
            && Assess(Job("C1", ("status", "CANCELLED"))) is null && Assess(Job("C1", ("cat", "DELIVERY"))) is null,
            "carrier: a job with a carrier, finished, cancelled or domestic is not looked at");
        check(Assess(Job("C1", ("date", "26/09/2026"))) is null && Assess(Job("C1", ("date", "06/10/2026"))) is null
            && Assess(Job("C1", ("date", ""))) is null && Keep(Assess(Job("C1", ("date", "27/09/2026")))) is not null,
            "carrier: from yesterday to a week ahead, and only with a plan date");

        /* ---------------- what the register bars, and what it only notes ---------------- */
        var barred = Keep(Assess(Job("C1"), Context(suppliers: new Dictionary<string, SupplierSummary?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHORE"] = Supplier(1, "SHORE LOGISTICS", status: "suspended"), ["ACN"] = null,
            ["TATIYAPOL"] = Supplier(3, "TATIYAPOL", status: "rejected"), ["NEWCO"] = Supplier(5, "NEWCO"),
        })));
        check(barred is not null && CarrierAgent.Recommended(barred.RuleReferences) == "NEWCO"
            && barred.RuleReferences.Contains("Carrier.Status:SHORE:suspended") && barred.RuleReferences.Contains("Carrier.Status:TATIYAPOL:rejected")
            && barred.RuleReferences.Contains("Carrier.NotRegistered:ACN") && barred.Recommendations.Single().Text == "ถาม NEWCO ก่อน",
            "carrier: suspended, rejected and unregistered carriers are passed over, each with its rule — the next one is recommended");
        var noted = Keep(Assess(Job("C1", ("type", "1X20' TK"), ("product", "DG class 3")), Context(suppliers: new Dictionary<string, SupplierSummary?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHORE"] = Supplier(1, "SHORE LOGISTICS", status: "pending-audit", compliance: SupplierCompliance.State.Expired),
            ["ACN"] = Supplier(2, "ACN", tank: true, dg: true), ["TATIYAPOL"] = Supplier(3, "TATIYAPOL", compliance: SupplierCompliance.State.Missing, tank: true),
        })));
        check(noted is not null && CarrierAgent.Recommended(noted.RuleReferences) == "SHORE"
            && noted.RuleReferences.Contains("Carrier.Note:SHORE:status-pending-audit") && noted.RuleReferences.Contains("Carrier.Note:SHORE:compliance-expired")
            && noted.RuleReferences.Contains("Carrier.Note:SHORE:no-tank") && noted.RuleReferences.Contains("Carrier.Note:SHORE:no-dg")
            && !noted.RuleReferences.Any(code => code.StartsWith("Carrier.Note:ACN", StringComparison.Ordinal))
            && noted.RuleReferences.Contains("Carrier.Note:TATIYAPOL:compliance-missing") && noted.RuleReferences.Contains("Carrier.Note:TATIYAPOL:no-dg")
            && noted.Facts.Any(fact => fact.Source == "job:C1.product"),
            "carrier: status, papers and capability are noted beside a candidate, not used to reorder — whether they block is the business's rule");
        var reefer = Assess(Job("C1", ("type", "1X40' RF")));
        check(reefer is not null && reefer.RuleReferences.Contains("Carrier.Note:SHORE:no-reefer"), "carrier: a reefer job notes a carrier not marked reefer-capable");

        /* ---------------- nobody to recommend ---------------- */
        var nobody = Keep(Assess(Job("C1"), Context(order: [])));
        check(nobody is { Status: AgentResultRules.RequiresHumanReview, RiskLevel: "HIGH" } && nobody.RuleReferences.Contains("Carrier.NoEligible")
            && CarrierAgent.Recommended(nobody.RuleReferences) is null,
            "carrier: no carrier has run this customer's work — a person chooses; HIGH because the plan is tomorrow");
        var allAsked = Keep(Assess(Job("C1", ("date", "04/10/2026")), Context(asked: ranked.Select(one => new Attempt(one.Carrier, CarrierAssignment.Rejected, one.Rank)).ToList())));
        check(allAsked is { Status: AgentResultRules.RequiresHumanReview, RiskLevel: "MEDIUM" }
            && allAsked.RuleResults.Any(rule => rule.Text.Contains("ถูกถามหรือถูกคัดออกหมด")),
            "carrier: everyone in the order has declined — a person chooses; MEDIUM while the plan is days away");

        check(every.All(result => AgentResultRules.Problems(result, known).Count == 0) && every.All(result => result.AgentId == CarrierAgent.Id),
            "carrier: every result passes the decision contract, under the vendor-agent id");
    }
}
