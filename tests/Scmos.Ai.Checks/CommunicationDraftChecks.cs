using System.Text.Json;
using System.Text.Json.Nodes;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Data;
using Scmos.Api.Rules;

/// <summary>
/// The Communication Agent's drafts (Agent Platform, 28 Sep 2026) against a
/// fixed clock — 10:00 in Bangkok on 28 September: templates filled from
/// approved variables only, the three rules that owe a carrier a message, the
/// cases that owe nothing, one draft per trigger, and every result held to the
/// decision contract.
/// </summary>
static class CommunicationDraftChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T03:00:00Z");

    private static JsonObject Job(string key, params (string Field, string Value)[] set)
    {
        var job = new JsonObject
        {
            ["key"] = key, ["cat"] = "IMPORT", ["opId"] = "OP-C1", ["op"] = "Operator", ["date"] = "29/09/2026", ["planTime"] = "09:00",
            ["status"] = "SUPPLIER_CONFIRMED", ["trucker"] = "SHORE", ["licence"] = "", ["driver"] = "", ["contact"] = "",
            ["customer"] = "BASF", ["type"] = "1X40'", ["container"] = "TEMU5246902", ["seal"] = "", ["arrDate"] = "", ["arrTime"] = "",
            ["jobCode"] = "J-" + key,
        };
        foreach (var (field, value) in set) job[field] = value;
        return job;
    }

    private static CachedJobRow Row(JsonObject job)
    {
        var raw = JsonDocument.Parse(job.ToJsonString()).RootElement.Clone();
        return new CachedJobRow(job["key"]!.GetValue<string>(), job["trucker"]!.GetValue<string>(), raw, JobRecord.From(raw));
    }

    private static CommunicationContext Context(IDictionary<string, PendingRequest>? pending = null, IEnumerable<string>? pod = null,
        IEnumerable<string>? wrote = null) => new(
        new Dictionary<string, PendingRequest>(pending ?? new Dictionary<string, PendingRequest>(), StringComparer.Ordinal),
        (pod ?? []).ToHashSet(StringComparer.Ordinal), (wrote ?? []).ToHashSet(StringComparer.Ordinal), 60, 14);

    private static AgentResult? Draft(JsonObject job, CommunicationContext? context = null, DateTimeOffset? at = null) =>
        CommunicationDrafts.Assess(Row(job), at ?? Now, context ?? Context());

    private static bool Throws(Action act)
    {
        try { act(); return false; } catch (InvalidOperationException) { return true; }
    }

    public static void Run(Action<bool, string> check)
    {
        var known = new AgentRegistry().All.Select(agent => agent.Id).ToList();
        var every = new List<AgentResult>();
        AgentResult? Keep(AgentResult? result) { if (result is not null) every.Add(result); return result; }

        /* ---------------- templates ---------------- */
        check(CommunicationTemplates.All.Select(one => one.Code).SequenceEqual(
                [CommunicationTemplates.ConfirmationReminder, CommunicationTemplates.TruckDetailReminder, CommunicationTemplates.PodReminder])
            && CommunicationTemplates.All.All(one => one.Audience == "carrier"
                && CommunicationTemplates.Holes(one.Text).All(hole => one.Variables.Contains(hole))
                && one.Variables.All(name => CommunicationTemplates.Holes(one.Text).Contains(name))),
            "communication: three carrier templates, each asking for exactly its approved variables");
        var pod = CommunicationTemplates.Of(CommunicationTemplates.PodReminder);
        check(Throws(() => CommunicationTemplates.Render(pod, new Dictionary<string, string> { ["carrier"] = "SHORE", ["amount"] = "9,999" })),
            "communication: a value the template does not approve is refused, never slipped into the text");
        var filled = CommunicationTemplates.Render(pod, new Dictionary<string, string>
        {
            ["carrier"] = "SHORE\r\n{jobCode}", ["jobCode"] = "J-1", ["container"] = new string('X', 80), ["doneDate"] = "",
        });
        check(filled.StartsWith("เรียน SHORE jobCode รบกวน", StringComparison.Ordinal) && filled.Contains(new string('X', CommunicationTemplates.MaxValue) + " ที่ส่งถึง")
            && filled.Contains("วันที่ — เพื่อใช้วางบิล") && !filled.Contains('{') && !filled.Contains('\n'),
            "communication: a value is one line, braces removed, cut to 60 characters; a missing one reads — so the gap shows");

        /* ---------------- 1. a request left unanswered ---------------- */
        var waiting = Context(pending: new Dictionary<string, PendingRequest> { ["C1"] = new(41, "ACN", Now.AddMinutes(-75)) });
        var asked = Job("C1", ("trucker", ""), ("status", JobStatus.WaitingSupplier));
        var reminder = Keep(Draft(asked, waiting));
        check(reminder is { DecisionType: CommunicationDrafts.DecisionType, AgentId: CommunicationAgent.Id, RiskLevel: "" }
            && CommunicationDrafts.TemplateOf(reminder.RuleReferences) == CommunicationTemplates.ConfirmationReminder
            && reminder.RuleReferences.SequenceEqual(["Channel:MANUAL", "Recipient:carrier:ACN", "Template:CARRIER_CONFIRMATION_REMINDER", "Trigger:supplier_request:41"])
            && reminder.Recommendations.Single() is { Source: "template:CARRIER_CONFIRMATION_REMINDER" } message
            && message.Text == "เรียน ACN รบกวนยืนยันรับงาน J-C1 ลูกค้า BASF วันที่ 29/09/2026 เวลา 09:00 (ขอรถเมื่อ 28/09 08:45) ขอบคุณ"
            && reminder.RuleResults.Single().Text.Contains("75 นาที") && reminder.Facts.Any(fact => fact.Source == "supplier_request:41"),
            "communication: ACN has not answered for 75 minutes — a confirmation reminder, filled from the job and the request");
        check(Draft(asked, Context(pending: new Dictionary<string, PendingRequest> { ["C1"] = new(41, "ACN", Now.AddMinutes(-30)) })) is null,
            "communication: thirty minutes is inside the reminder window — nothing yet");
        check(Draft(Job("C1", ("trucker", ""), ("date", "25/09/2026")), waiting) is null,
            "communication: a request on a job planned days ago is not chased by a reminder");
        var later = Draft(asked, waiting, Now.AddMinutes(45))!;
        var another = Draft(asked, Context(pending: new Dictionary<string, PendingRequest> { ["C1"] = new(42, "ACN", Now.AddMinutes(-75)) }))!;
        check(later.RuleResults.Single().Text != reminder!.RuleResults.Single().Text
            && AiDecisionLog.FingerprintOf(later) == AiDecisionLog.FingerprintOf(reminder)
            && AiDecisionLog.FingerprintOf(another) != AiDecisionLog.FingerprintOf(reminder),
            "communication: one draft per request — the minutes move, the fingerprint does not; a new request is a new trigger");

        /* ---------------- 2. no truck named ---------------- */
        var truck = Keep(Draft(Job("T1", ("driver", "Somchai"))));
        check(truck is not null && CommunicationDrafts.TemplateOf(truck.RuleReferences) == CommunicationTemplates.TruckDetailReminder
            && truck.RuleReferences.Contains("Trigger:plan:2026-09-29") && truck.RuleReferences.Contains("Recipient:carrier:SHORE")
            && truck.Facts.Any(fact => fact.Text == "ยังไม่มีทะเบียนรถ")
            && truck.RuleResults.Single().Source == "Notifications.MissingBookingData"
            && truck.Recommendations.Single().Text.Contains("งาน J-T1 ตู้ TEMU5246902 วันที่ 29/09/2026 เวลา 09:00"),
            "communication: SHORE on tomorrow's job with no plate — ask for the truck, by the bell's own rule");
        check(Draft(Job("T2", ("date", "03/10/2026"))) is null && Draft(Job("T3", ("licence", "70-1111"), ("driver", "Somchai"))) is null
            && Draft(Job("T4", ("trucker", ""))) is null,
            "communication: five days out, a truck already named, or no carrier to write to — nothing");

        /* ---------------- 3. no POD ---------------- */
        var done = Job("P1", ("status", "COMPLETED"), ("date", "26/09/2026"), ("arrDate", "27/09/2026"), ("arrTime", "10:00"),
            ("licence", "70-1111"), ("driver", "Somchai"));
        var podDraft = Keep(Draft(done));
        check(podDraft is not null && CommunicationDrafts.TemplateOf(podDraft.RuleReferences) == CommunicationTemplates.PodReminder
            && podDraft.RuleReferences.Contains("Trigger:done:2026-09-27") && podDraft.Facts.Any(fact => fact.Source == "documents:P1.POD")
            && podDraft.Recommendations.Single().Text.Contains("ที่ส่งถึงวันที่ 27/09/2026 เพื่อใช้วางบิล"),
            "communication: finished yesterday by its arrival with no POD on file — ask the carrier for it");
        check(Draft(done, Context(pod: ["P1"])) is null, "communication: a POD on file owes nothing");
        check(Draft(Job("P2", ("status", "COMPLETED"), ("date", "28/09/2026"), ("arrDate", "28/09/2026"), ("licence", "70-1111"), ("driver", "S"))) is null
            && Draft(Job("P3", ("status", "COMPLETED"), ("date", "01/09/2026"), ("licence", "70-1111"), ("driver", "S"))) is null,
            "communication: finished today is not yet owed; finished four weeks ago is past the reminder window");
        var byPlan = Keep(Draft(Job("P4", ("status", "COMPLETED"), ("date", "25/09/2026"), ("licence", "70-1111"), ("driver", "S"))));
        check(byPlan is not null && byPlan.Facts.Any(fact => fact.Text.Contains("(ตามวันแผน)")),
            "communication: with no arrival recorded the plan day stands in, and the draft says so");

        /* ---------------- nothing to write ---------------- */
        check(Draft(asked, Context(pending: new Dictionary<string, PendingRequest> { ["C1"] = new(41, "ACN", Now.AddMinutes(-75)) }, wrote: ["C1"])) is null
            && Draft(Job("T1", ("driver", "Somchai")), Context(wrote: ["T1"])) is null && Draft(done, Context(wrote: ["P1"])) is null,
            "communication: the carrier has written and a person has not read it yet — no draft on top of their message");
        check(Draft(Job("X1", ("status", "CANCELLED"))) is null, "communication: a cancelled job owes nobody a message");

        check(every.All(result => AgentResultRules.Problems(result, known).Count == 0),
            "communication: every draft passes the decision contract");
    }
}
