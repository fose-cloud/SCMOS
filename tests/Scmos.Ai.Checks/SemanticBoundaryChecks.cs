using System.Text.Json;
using Scmos.Api.Ai.Semantic;
using Scmos.Api.Data;
using Scmos.Api.Rules;

static class SemanticBoundaryChecks
{
    public static void Run(Action<bool, string> check)
    {
        var today = new DateOnly(2026, 9, 15);
        JobRecord Arrival(string date, string planned, string arrivedDate, string arrived) =>
            new() { Date = date, PlanTime = planned, ArrDate = arrivedDate, ArrTime = arrived };
        foreach (var item in new[] { ("09:00", true, false), ("09:01", true, false),
            ("09:30", true, false), ("09:31", false, true), ("08:59", true, false) })
        {
            var job = Arrival("15/09/2026", "09:00", "15/09/2026", item.Item1);
            check(JobRules.IsOnTime(job) == item.Item2 && JobRules.LateBeyond(job) == item.Item3,
                "1C: shared 30-minute OTD and lateness boundary " + item.Item1);
        }
        var midnight = Arrival("15/09/2026", "23:50", "16/09/2026", "00:20");
        check(JobRules.MinutesLate(midnight) == 30 && JobRules.IsOnTime(midnight) && !JobRules.LateBeyond(midnight),
            "1C: midnight preserves exactly 30 minutes");
        check(JobRules.MinutesLate(Arrival("28/02/2028", "23:50", "29/02/2028", "00:21")) == 31,
            "1C: leap-day arrival is measurable");
        foreach (var job in new[] { Arrival("15/09/2026", "", "15/09/2026", "09:00"),
            Arrival("15/09/2026", "09:00", "", "09:00") })
            check(!JobRules.IsMeasurable(job) && JobRules.MinutesLate(job) is null
                && !JobRules.IsOnTime(job) && !JobRules.LateBeyond(job),
                "1C: missing date/time is unmeasurable");
        var impossible = Arrival("31/02/2026", "09:00", "31/02/2026", "09:00");
        var validOnTime = Arrival("15/09/2026", "09:00", "15/09/2026", "09:00");
        var validLate = Arrival("15/09/2026", "09:00", "15/09/2026", "09:31");
        var invalidLate = Arrival("30/02/2026", "09:00", "31/02/2026", "09:00");
        bool CalendarValid(JobRecord row) => Formats.ParseDay(row.Date) is not null && Formats.ParseDay(row.ArrDate) is not null;
        double Rate(IEnumerable<JobRecord> rows) {
            var measured = rows.Where(JobRules.IsMeasurable).ToArray();
            return measured.Count(JobRules.IsOnTime) / (double)measured.Length;
        }
        var inflated = new[] { validOnTime, validLate, impossible };
        var depressed = new[] { validOnTime, validLate, invalidLate };
        check(Rate(inflated) == Rate(inflated.Where(CalendarValid)), "calendar guard excludes false on-time from KPI");
        check(Rate(depressed) == Rate(depressed.Where(CalendarValid)), "calendar guard excludes false late from KPI");
        check(!JobRules.InPeriod(impossible.Date, "2026", "02", ""), "calendar guard excludes impossible day from period");
        check(Formats.DateNumber(impossible.Date) == 0 && Formats.ParseDay(impossible.Date) is null,
            "calendar guard counts impossible dates as unparseable");
        check(!JobRules.IsMeasurable(impossible) && !JobRules.IsOnTime(impossible)
            && JobRules.MinutesLate(impossible) is null && Formats.ParseDay(impossible.Date) is null,
            "calendar guard agrees with moment parsing");
        foreach (var invalid in new[] { "31/02/2026", "29/02/2026", "29/02/2100", "31/04/2026", "00/09/2026", "01/13/2026", "01/01/0000", "15/09/2026 extra" })
            check(!Formats.IsDate(invalid) && Formats.DateNumber(invalid) == 0 && Formats.PartsOf(invalid) == ("", "", ""),
                "calendar rejection: " + invalid);
        foreach (var valid in new[] { "29/02/2000", "29/02/2028", "30/04/2026", "31/12/2026" })
            check(Formats.IsDate(valid) && Formats.DateNumber(valid) > 0, "calendar acceptance: " + valid);
        var badInput = JsonSerializer.SerializeToElement(new { date = "31/02/2026" });
        check(JobDateInputGuard.InvalidField(badInput) == "date", "new impossible plan date rejected");
        check(JobDateInputGuard.InvalidField(badInput, new Dictionary<string,string> { ["date"] = "31/02/2026" }) is null,
            "unchanged legacy date does not block unrelated edits");
        check(JobDateInputGuard.InvalidField(JsonSerializer.SerializeToElement(new { date = "WAIT" })) is null,
            "WAIT remains unchanged and is not guessed");
        check(JobDateInputGuard.InvalidField(JsonSerializer.SerializeToElement(new { arrDate = "31/04/2026" })) == "arrDate",
            "new impossible arrival date rejected");
        check(JobDateInputGuard.InvalidField(JsonSerializer.SerializeToElement(new { closingDate = "29/02/2100" })) == "closingDate",
            "new impossible closing date rejected");
        check(JobDateInputGuard.InvalidField(badInput, new Dictionary<string,string> { ["date"] = "28/02/2026" }) == "date",
            "changing existing valid date to impossible date rejected");
        check(JobDateInputGuard.InvalidField(JsonSerializer.SerializeToElement(new { date = "29/02/2028", arrDate = "", closingDate = "-" })) is null,
            "valid leap date and blank placeholders remain accepted");
        check(JobRules.Validate(impossible).Any(i => i.Field == "date" && i.Severity == Severity.Error),
            "job validation flags impossible date");
        WorkspaceTabs.JobView View(string date = "15/09/2026", string status = "RECEIVED",
            string reason = "", string owner = "Owner", string carrier = "Carrier",
            string driver = "Driver", string plate = "Plate", string arrival = "") =>
            JobsRepository.AnalysisRow("semantic-key", "OP-A", JsonSerializer.Serialize(new {
                cat = "IMPORT", date, status, reason, op = owner, opId = "OP-A",
                trucker = carrier, driver, licence = plate, arrDate = arrival
            }), DateTimeOffset.UtcNow).Job!.Value;
        check(WorkspaceTabs.Matches(WorkspaceTabs.Delay, View(reason: "Recorded"), "", today),
            "1C: recorded reason enters DELAY without arrival measurement");
        check(!WorkspaceTabs.Matches(WorkspaceTabs.Delay, View(status: "CANCELLED", reason: "Recorded"), "", today),
            "1C: cancellation excluded from DELAY even with reason");
        check(WorkspaceTabs.Matches(WorkspaceTabs.Delay, View(status: "COMPLETED", reason: "Recorded"), "", today),
            "1C: raw DELAY rule can include completed; Operations adapter excludes it");
        check(MonitorRules.Judge(View(date: "17/09/2026", carrier: ""), today)?.Why == MonitorRules.Risk.NoCarrier
            && MonitorRules.Judge(View(date: "18/09/2026", carrier: ""), today) is null,
            "1C: carrier horizon includes day +2 but not +3");
        check(MonitorRules.Judge(View(date: "18/09/2026", owner: ""), today)?.Why == MonitorRules.Risk.Unassigned,
            "1C: raw unassigned rule precedes window; adapter applies its own window");
        check(MonitorRules.Judge(View(date: "14/09/2026", owner: "", carrier: ""), today)?.Why == MonitorRules.Risk.Overdue,
            "1C: overdue has priority over missing assignment and carrier");
        check(MonitorRules.Judge(View(driver: "", plate: ""), today)?.Why == MonitorRules.Risk.NoTruck
            && MonitorRules.Judge(View(driver: ""), today) is null,
            "1C: monitor no-truck requires both fields blank, unlike separate missing-details alert");
        check(MonitorRules.Judge(View(date: "WAIT"), today) is null
            && MonitorRules.Judge(View(date: "14/09/2026", arrival: "recorded"), today) is null,
            "1C: undated or any recorded arrival suppresses monitor flag");
        check(SourceScopeRegistry.All.Count == 4 && SourceScopeRegistry.Resolve("unknown") is null,
            "1C: only documented source scopes resolve");
    }
}
