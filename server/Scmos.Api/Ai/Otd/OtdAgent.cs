using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Otd;

/// <summary>
/// The OTD Agent (AI Agent Platform specification §31) — delay risk found
/// before the on-time measure fails, rule first. No model is called: every
/// figure is a subtraction the register already supports, and every threshold
/// is one SCMOS already keeps — the customer's on-time grace
/// (<see cref="CustomerTerms"/>), the monitor's two-day window
/// (<see cref="MonitorRules"/>), the status ladder — except the two
/// near-plan windows, which are configuration
/// (<c>AI__OtdWatchMinutes</c>, <c>AI__OtdHighMinutes</c>).
///
/// <para>
/// A job is looked at only while there is still something to do about it:
/// not finished, not cancelled, no arrival recorded, planned from yesterday to
/// the monitor's window ahead. Older jobs with no arrival are a gap in the
/// record, which the Validation Agent and the delay reasons already chase.
/// </para>
///
/// <para>
/// Pure: a row and "now" in, a result out. What it concludes is written to the
/// decision log by <see cref="AgentScanner"/>; nothing here writes.
/// </para>
/// </summary>
public static class OtdAgent
{
    public const string Id = "otd-agent";
    public const string DecisionType = "otd_risk";

    public const string Normal = "NORMAL";
    public const string Watch = "WATCH";
    public const string High = "HIGH";
    public const string Critical = "CRITICAL";
    private static readonly string[] Order = [Normal, Watch, High, Critical];

    /// <summary>Past the customer's on-time window with no arrival recorded.</summary>
    public const string PastWindow = "OTD.PAST_WINDOW_NO_ARRIVAL";
    /// <summary>Past the plan time, still inside the customer's grace.</summary>
    public const string PastPlan = "OTD.PAST_PLAN_WITHIN_GRACE";
    /// <summary>The plan time is close and no truck or driver is named.</summary>
    public const string NoTruckNearPlan = "OTD.NO_TRUCK_NEAR_PLAN";
    /// <summary>The plan time is close and the status has not reached a running step.</summary>
    public const string NotDispatchedNearPlan = "OTD.NOT_DISPATCHED_NEAR_PLAN";

    private static readonly TimeSpan Zone = Formats.Zone;

    public static AgentResult? Assess(CachedJobRow row, DateTimeOffset now, int watchMinutes, int highMinutes)
    {
        if (row.Record is not { } job) return null;
        var key = row.Key;
        if (key.Length == 0 || JobRules.IsDone(job.Status) || WorkspaceTabs.IsCancelled(job.Status)) return null;
        if (Formats.Clean(job.ArrDate).Length > 0 || Formats.Clean(job.ArrTime).Length > 0) return null;
        var local = now.ToOffset(Zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        if (Formats.ParseDay(job.Date) is not { } day || day < today.AddDays(-1) || day > today.AddDays(MonitorRules.SoonDays)) return null;

        var planned = Formats.MomentAt(job.Date, job.PlanTime);
        var term = CustomerTerms.Of(job.Customer, job.Type);
        var grace = term?.GraceMinutes ?? CustomerTerms.DefaultGraceMinutes;
        var carrier = Formats.Clean(job.Trucker);
        var noTruck = Formats.Clean(job.Licence).Length == 0 && Formats.Clean(job.Driver).Length == 0;
        var running = JobRules.IsRunning(job.Status);

        var codes = new List<(string Level, string Code)>();
        int? pastMinutes = null;
        if (planned is { } plan)
        {
            var past = (int)Math.Floor((local - plan).TotalMinutes);
            var ahead = -past;
            if (past > grace) { codes.Add((Critical, PastWindow)); pastMinutes = past; }
            else if (past > 0) { codes.Add((High, PastPlan)); pastMinutes = past; }
            else if (ahead <= highMinutes && noTruck) codes.Add((High, NoTruckNearPlan));
            else if (ahead <= watchMinutes && !running) codes.Add((Watch, NotDispatchedNearPlan));
        }
        // The monitor's own judgement, as the supervisor's screen shows it — one rule, read in two places.
        if (MonitorRules.Judge(WorkspaceTabs.JobView.From(row.Raw), today) is { } flag)
        {
            var level = flag.Why switch
            {
                MonitorRules.Risk.Overdue => Critical,
                MonitorRules.Risk.Unassigned or MonitorRules.Risk.NoCarrier => High,
                _ => Watch,
            };
            // A plan time already judged tells more than the day did; the day's overdue adds nothing to it.
            if (!(flag.Why == MonitorRules.Risk.Overdue && pastMinutes is not null))
                codes.Add((level, "MonitorRules." + flag.Why));
        }
        if (codes.Count == 0) return null;

        var worst = codes.MaxBy(one => Array.IndexOf(Order, one.Level)).Level;
        var source = $"job:{key}";
        var planText = planned is { } shown ? shown.ToString("dd/MM/yyyy HH:mm") : $"{Formats.Clean(job.Date)} (ไม่มีเวลาแผน)";
        var facts = new List<AgentFinding>
        {
            new($"แผน {planText}", $"{source}.date+planTime"),
            new($"สถานะ {Formats.Clean(job.Status).OrDash()}", $"{source}.status"),
            new(carrier.Length > 0 ? $"ผู้ขนส่ง {carrier}" : "ยังไม่มีผู้ขนส่ง", $"{source}.trucker"),
            new(noTruck ? "ยังไม่มีทะเบียนรถและคนขับ" : $"รถ {Formats.Clean(job.Licence).OrDash()} · คนขับ {Formats.Clean(job.Driver).OrDash()}", $"{source}.licence+driver"),
            new("ยังไม่มีวันเวลาถึงในตารางงาน", $"{source}.arrDate+arrTime"),
            new($"เวลาตรวจ {local:dd/MM/yyyy HH:mm}", "clock"),
        };
        var rules = new List<AgentFinding>();
        if (planned is { } deadlineOf)
            rules.Add(new($"ถึงภายใน {deadlineOf.AddMinutes(grace):HH:mm} จึงนับตรงเวลา (ผ่อนผัน {grace} นาที)",
                term is null ? "CustomerTerms.Default" : $"CustomerTerms.{term.Customer}"));
        var inferences = new List<AgentFinding>();
        var recommendations = new List<AgentFinding>();
        var who = carrier.Length > 0 ? carrier : "ผู้ขนส่ง";
        foreach (var (_, code) in codes)
        {
            switch (code)
            {
                case PastWindow:
                    rules.Add(new($"เลยหน้าต่างตรงเวลาแล้ว {pastMinutes - grace} นาที", PastWindow));
                    inferences.Add(new("งานนี้มีแนวโน้มไม่ตรงเวลา เว้นแต่รถถึงแล้วแต่ยังไม่ได้บันทึก"));
                    recommendations.Add(new($"ติดต่อ {who} เพื่อยืนยันเวลาถึงจริง แล้วบันทึกวันเวลาถึง"));
                    break;
                case PastPlan:
                    rules.Add(new($"เลยเวลาแผนมา {pastMinutes} นาที ยังอยู่ในช่วงผ่อนผัน", PastPlan));
                    inferences.Add(new("รถอาจถึงหลังหมดช่วงผ่อนผัน"));
                    recommendations.Add(new($"ยืนยันตำแหน่งรถกับ {who}"));
                    break;
                case NoTruckNearPlan:
                    rules.Add(new($"อีกไม่เกิน {highMinutes} นาทีถึงเวลาแผน ยังไม่มีรถหรือคนขับ", NoTruckNearPlan));
                    inferences.Add(new("หากยังไม่ได้รถ งานนี้อาจเริ่มช้ากว่าแผน"));
                    recommendations.Add(new($"ขอทะเบียนรถและคนขับจาก {who}"));
                    break;
                case NotDispatchedNearPlan:
                    rules.Add(new($"อีกไม่เกิน {watchMinutes} นาทีถึงเวลาแผน สถานะยังไม่ถึงขั้นรถออก", NotDispatchedNearPlan));
                    inferences.Add(new("รถอาจออกไม่ทันเวลาแผน"));
                    recommendations.Add(new($"ยืนยันกับ {who} ว่ารถออกแล้ว"));
                    break;
                case "MonitorRules.Overdue":
                    rules.Add(new("Shipment Monitor: เลยวันแผนแล้วยังไม่มีเวลาถึง", code));
                    recommendations.Add(new("ตรวจว่างานนี้วิ่งแล้วหรือไม่ แล้วบันทึกวันเวลาถึง"));
                    break;
                case "MonitorRules.Unassigned":
                    rules.Add(new("Shipment Monitor: งานนี้ยังไม่มีผู้รับผิดชอบ", code));
                    recommendations.Add(new("มอบหมายผู้รับผิดชอบงานนี้"));
                    break;
                case "MonitorRules.NoCarrier":
                    rules.Add(new($"Shipment Monitor: ภายใน {MonitorRules.SoonDays} วันยังไม่มีผู้ขนส่ง", code));
                    recommendations.Add(new("หาผู้ขนส่งให้งานนี้"));
                    break;
                case "MonitorRules.NoTruck":
                    rules.Add(new($"Shipment Monitor: ภายใน {MonitorRules.SoonDays} วันยังไม่มีรถหรือคนขับ", code));
                    recommendations.Add(new($"ขอทะเบียนรถและคนขับจาก {who}"));
                    break;
            }
        }
        var summary = codes.Any(one => one.Code == PastWindow) ? $"เลยเวลาแผน {pastMinutes} นาที ยังไม่มีเวลาถึง"
            : codes.Any(one => one.Code == PastPlan) ? $"เลยเวลาแผน {pastMinutes} นาที (ในช่วงผ่อนผัน {grace} นาที)"
            : codes.Any(one => one.Code == NoTruckNearPlan) ? "ใกล้เวลาแผน ยังไม่มีรถหรือคนขับ"
            : codes.Any(one => one.Code == NotDispatchedNearPlan) ? "ใกล้เวลาแผน รถยังไม่ออก"
            : codes[0].Code switch
            {
                "MonitorRules.Overdue" => "เลยวันแผน ยังไม่มีเวลาถึง",
                "MonitorRules.Unassigned" => "ยังไม่มีผู้รับผิดชอบ",
                "MonitorRules.NoCarrier" => "ใกล้วันแผน ยังไม่มีผู้ขนส่ง",
                _ => "ใกล้วันแผน ยังไม่มีรถหรือคนขับ",
            };
        return new AgentResult(Id, DecisionType, "job", key, AgentResultRules.Completed,
            $"{Formats.Clean(job.JobCode).OrDash()} · {summary}",
            facts, rules, [], inferences.Distinct().ToList(), recommendations.Distinct().ToList(), [],
            codes.Select(one => one.Code).Distinct().OrderBy(one => one, StringComparer.Ordinal).ToList(),
            [source], worst);
    }

    private static string OrDash(this string text) => text.Length > 0 ? text : "—";
}
