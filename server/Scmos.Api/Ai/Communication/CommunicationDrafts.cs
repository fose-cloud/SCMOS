using Scmos.Api.Ai.Documents;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Communication;

/// <summary>A carrier request still waiting for its answer.</summary>
public sealed record PendingRequest(long Id, string Carrier, DateTimeOffset RequestedAt);

/// <summary>What the Communication Agent needs besides the job, read once per pass.</summary>
/// <param name="Pending">The waiting request per job (one at most — AP-04).</param>
/// <param name="WithPod">Jobs holding a POD file, as the bell and the verification screen count them.</param>
/// <param name="CarrierWrote">Jobs with a carrier's LINE or TMS message waiting for a person — read that before writing to them.</param>
/// <param name="ReminderMinutes">How long a request waits before a reminder is drafted (<c>AI__CarrierReminderMinutes</c>).</param>
/// <param name="PodDays">How far back a finished job is still asked for its POD (<c>AI__PodReminderDays</c>).</param>
public sealed record CommunicationContext(
    IReadOnlyDictionary<string, PendingRequest> Pending,
    IReadOnlySet<string> WithPod,
    IReadOnlySet<string> CarrierWrote,
    int ReminderMinutes,
    int PodDays);

/// <summary>
/// The Communication Agent's drafts (AI Agent Platform specification §30,
/// internal only): a message to a carrier from a fixed template, when a rule
/// SCMOS already keeps says one is owed —
/// <list type="bullet">
/// <item>a request waiting longer than the reminder window → <c>CARRIER_CONFIRMATION_REMINDER</c>;</item>
/// <item>a carrier on the job, the plan inside the monitor's window, no plate or driver (the bell's
/// <see cref="Notifications.MissingBookingData(JobRecord)"/>) → <c>TRUCK_DETAIL_REMINDER</c>;</item>
/// <item>a finished job with no POD file (the bell's <c>PodMissing</c>, the checklist's POD) → <c>POD_REMINDER</c>.</item>
/// </list>
/// Nothing is sent: the draft goes to the decision log, the job's owner copies
/// it, sends it on their own channel and says so — or sends something else, or
/// nothing. That answer is the record of what went out (AP-02: attributable,
/// traceable, audited). A job whose carrier has written and is waiting for a
/// person gets no draft: the answer may already be there.
///
/// <para>
/// Duplicates: one draft per trigger. The fingerprint names the template, the
/// carrier and the trigger (the request, the plan day, the day it was done), so
/// a draft a person already answered is not raised again for the same thing
/// (<see cref="AgentScanner"/>), and a draft whose reason is gone is resolved.
/// </para>
/// </summary>
public static class CommunicationDrafts
{
    public const string DecisionType = "communication_draft";
    public const string ManualChannel = "Channel:MANUAL";

    public static AgentResult? Assess(CachedJobRow row, DateTimeOffset now, CommunicationContext context)
    {
        if (row.Record is not { } job || row.Key.Length == 0 || WorkspaceTabs.IsCancelled(job.Status)) return null;
        if (context.CarrierWrote.Contains(row.Key)) return null;
        var local = now.ToOffset(Formats.Zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var planned = Formats.ParseDay(job.Date);
        var key = row.Key;
        var source = $"job:{key}";
        var jobCode = Formats.Clean(job.JobCode) is { Length: > 0 } code ? code : key;
        var planFact = new AgentFinding($"แผน {Formats.Clean(job.Date)} {Formats.Clean(job.PlanTime)}".Trim(), $"{source}.date+planTime");

        // 1. A request left unanswered.
        if (context.Pending.TryGetValue(key, out var request) && !JobRules.IsDone(job.Status)
            && planned is { } day && day >= today.AddDays(-1))
        {
            var waited = (int)Math.Floor((now - request.RequestedAt).TotalMinutes);
            if (waited < context.ReminderMinutes) return null;
            var asked = request.RequestedAt.ToOffset(Formats.Zone).ToString("dd/MM HH:mm");
            return Draft(CommunicationTemplates.ConfirmationReminder, job, key, jobCode, request.Carrier,
                new Dictionary<string, string>
                {
                    ["carrier"] = request.Carrier, ["jobCode"] = jobCode, ["customer"] = job.Customer,
                    ["date"] = job.Date, ["planTime"] = job.PlanTime, ["requestedAt"] = asked,
                },
                [new($"ขอรถจาก {Formats.Clean(request.Carrier)} เมื่อ {asked} ยังไม่มีคำตอบ", $"supplier_request:{request.Id}"), planFact],
                [new($"รอคำตอบ {waited} นาที เกินกำหนดเตือน {context.ReminderMinutes} นาที", "AI__CarrierReminderMinutes")],
                $"Trigger:supplier_request:{request.Id}", [source, $"supplier_request:{request.Id}"]);
        }

        // 2. A carrier on the job and no truck named, inside the monitor's window.
        var carrier = Formats.Clean(job.Trucker);
        if (Notifications.MissingBookingData(job) && planned is { } soon && soon >= today && soon <= today.AddDays(MonitorRules.SoonDays))
        {
            var noPlate = Formats.Clean(job.Licence).Length == 0;
            var noDriver = Formats.Clean(job.Driver).Length == 0;
            return Draft(CommunicationTemplates.TruckDetailReminder, job, key, jobCode, carrier,
                new Dictionary<string, string>
                {
                    ["carrier"] = carrier, ["jobCode"] = jobCode, ["container"] = job.Container,
                    ["date"] = job.Date, ["planTime"] = job.PlanTime,
                },
                [new($"ผู้ขนส่ง {carrier}", $"{source}.trucker"), planFact,
                    new(noPlate && noDriver ? "ยังไม่มีทะเบียนรถและคนขับ" : noPlate ? "ยังไม่มีทะเบียนรถ" : "ยังไม่มีคนขับ", $"{source}.licence+driver")],
                [new($"มีผู้ขนส่งแล้วแต่ยังไม่มีรถหรือคนขับ ภายใน {MonitorRules.SoonDays} วันก่อนแผน", "Notifications.MissingBookingData")],
                $"Trigger:plan:{soon:yyyy-MM-dd}", [source]);
        }

        // 3. Finished, and no POD on file.
        if (JobRules.IsDone(job.Status) && carrier.Length > 0 && !context.WithPod.Contains(key)
            && DocumentChecklist.For(job.Cat).Any(document => document.Folder == "POD")
            && DocumentsReadService.DoneOn(WorkspaceTabs.JobView.From(row.Raw)) is ({ } done, var basis)
            && done < today && done >= today.AddDays(-context.PodDays))
        {
            var doneText = done.ToString("dd/MM/yyyy");
            return Draft(CommunicationTemplates.PodReminder, job, key, jobCode, carrier,
                new Dictionary<string, string>
                {
                    ["carrier"] = carrier, ["jobCode"] = jobCode, ["container"] = job.Container, ["doneDate"] = doneText,
                },
                [new($"ผู้ขนส่ง {carrier}", $"{source}.trucker"),
                    new($"งานเสร็จ {doneText}" + (basis == "arrival" ? "" : " (ตามวันแผน)"), $"{source}.status+{(basis == "arrival" ? "arrDate" : "date")}"),
                    new("ยังไม่มีไฟล์ในโฟลเดอร์ POD", $"documents:{key}.POD")],
                [new("งานที่เสร็จแล้วต้องมีใบรับของก่อนวางบิล", "DocumentChecklist.POD")],
                $"Trigger:done:{done:yyyy-MM-dd}", [source]);
        }
        return null;
    }

    /// <summary>The template's code, from a draft's rule references, or null.</summary>
    public static string? TemplateOf(IEnumerable<string> ruleReferences) =>
        ruleReferences.FirstOrDefault(one => one.StartsWith("Template:", StringComparison.Ordinal))?["Template:".Length..];

    private static AgentResult Draft(string code, JobRecord job, string key, string jobCode, string carrier,
        IReadOnlyDictionary<string, string> values, List<AgentFinding> facts, List<AgentFinding> rules,
        string trigger, IReadOnlyList<string> evidence)
    {
        var template = CommunicationTemplates.Of(code);
        var text = CommunicationTemplates.Render(template, values);
        return new AgentResult(CommunicationAgent.Id, DecisionType, "job", key, AgentResultRules.Completed,
            $"{jobCode} · {template.Thai} → {Formats.Clean(carrier)}",
            facts, rules, [], [], [new(text, "template:" + code)], [],
            new[] { "Template:" + code, "Recipient:carrier:" + CarrierDirectory.Lookup.Key(carrier), ManualChannel, trigger }
                .OrderBy(one => one, StringComparer.Ordinal).ToList(),
            evidence, "");
    }
}
