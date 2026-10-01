using Scmos.Api.Data;
using Scmos.Api.Rules;

/// <summary>
/// Annual Evaluation Phase 3 (1 Oct 2026): the figures one carrier's records make. Pure — no database — so every rule
/// the snapshot leans on is checked on numbers that can be worked out by hand.
/// </summary>
static class EvaluationEvidenceChecks
{
    public static void Run(Action<bool, string> check)
    {
        static EvaluationEvidence.Job Job(string key, string status, string arrival) =>
            new(key, new JobRecord { Key = key, Date = "15/09/2026", Status = status, PlanTime = "08:00", ArrDate = arrival.Length > 0 ? "15/09/2026" : "", ArrTime = arrival });
        var jobs = new List<EvaluationEvidence.Job>
        {
            Job("J1", JobStatus.Completed, "07:50"),      // on time
            Job("J2", JobStatus.Completed, "10:00"),      // late, the carrier's
            Job("J3", JobStatus.Completed, "10:00"),      // late, the customer's
            Job("J4", JobStatus.Completed, "10:00"),      // late, nobody said why
            Job("J5", JobStatus.Completed, ""),           // no arrival: cannot be judged
            Job("J6", "", "07:55"),                       // on time, not yet completed
        };
        const string accident = "ความปลอดภัย/อุบัติเหตุ";
        static OperationalIssue Issue(string code, string category, string source, string severity, string grade = "", long? caseId = null) =>
            new() { Code = code, Category = category, Source = source, Severity = severity, AccidentGrade = grade, CaseId = caseId };
        var issues = new List<OperationalIssue>
        {
            Issue("I1", accident, "Subcontractor", "สูง", "Major"),
            Issue("I2", accident, "Subcontractor", "ต่ำ", "Minor"),
            Issue("I3", accident, "Subcontractor", "วิกฤต"),                          // ungraded
            Issue("I4", ScorecardColumn.DamageCategory, "Warehouse", "สูง", caseId: 7),  // a claim: damage, 3 points, CAR/PAR closed
            Issue("I5", "รถเข้ารับ/ส่งล่าช้า", "Customer", ""),                         // a claim: the customer complained, unrated = 1
            Issue("I6", "รถเข้ารับ/ส่งล่าช้า", "CS", "สูง"),                             // a complaint, but from inside: not a claim
        };
        var september = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(7));
        var input = new EvaluationEvidence.Inputs(jobs,
            [new("J2", true), new("J3", false)],
            issues, new Dictionary<long, string> { [7] = "closed" },
            [new("INV-A", 1, september), new("INV-B", 2, september), new("INV-C", 1, null)],
            [
                new("J1", september, september, new DateOnly(2026, 9, 5), september.AddDays(2)),             // in time
                new("J2", september, september, new DateOnly(2026, 9, 5), september.AddDays(9)),             // late
                new("J3", september, september.AddDays(30), new DateOnly(2026, 9, 5), september.AddDays(31)),  // back-filled
                new("J4", september, september, new DateOnly(2026, 12, 31), null),                            // not due yet
            ],
            new HashSet<string>(["J1", "J2"]),
            [("insurance-vehicle", SupplierCompliance.State.Valid), ("insurance-cargo", SupplierCompliance.State.Expiring),
             ("transport-licence", SupplierCompliance.State.Expired), ("affidavit", SupplierCompliance.State.Missing),
             ("truck-profile", SupplierCompliance.State.Valid)],
            5, new DateOnly(2026, 10, 1));
        var metrics = EvaluationEvidence.Build(input).ToDictionary(metric => metric.Code);
        decimal? Value(string code) => metrics[code].Value;
        string Status(string code) => metrics[code].Status;

        check(Value("total-jobs") == 6 && Value("completed-jobs") == 5 && Value("otd-measured") == 5 && Value("late") == 3
            && Value("late-carrier") == 1 && Value("late-other") == 1 && Value("late-unclassified") == 1
            && metrics["late-unclassified"].Sources.SequenceEqual(["J4"])
            && Value("operational-otd") == 40m && Value("carrier-otd") == 80m && Status("carrier-otd") == AnnualEvaluationRules.Available
            && metrics["carrier-otd"].External && !metrics["operational-otd"].External,
            "evidence: carrier OTD charges only the late jobs whose reason is the carrier's; unexplained ones are counted apart");
        check(Value("incidents-major") == 1 && Value("incidents-minor") == 1 && Value("incidents-ungraded") == 1
            && Value("incident-rate") == 60m && Status("critical-safety") == AnnualEvaluationRules.NotAvailable,
            "evidence: accidents per hundred completed jobs, ungraded ones included; what SCMOS does not record is not available, not nought");
        check(Value("claims") == 2 && Value("claim-points") == 4 && Value("claim-rate") == 80m && Value("claims-car-par") == 1
            && metrics["claims-car-par"].Note.Contains("ปิดแล้ว 1") && metrics["claims-minor"].Note.Contains("ไม่ระบุความรุนแรง"),
            "evidence: a claim is damage or a customer's complaint, weighted 1·3·5, and its CAR/PAR is counted beside it");
        check(Value("billing-submitted") == 3 && Value("billing-accuracy") == 50m && Status("billing-accuracy") == AnnualEvaluationRules.InsufficientData
            && Value("billing-sla") == 50m && metrics["billing-sla"].Sources.SequenceEqual(["J2"]) && metrics["billing-sla"].Note.Contains("ย้อนหลัง"),
            "evidence: billing accuracy over invoices with an answer; SLA over jobs that fell due, back-filled cases left out and said so");
        check(Value("pod-compliance") == 40m && Value("compliance-documents") == 60m
            && metrics["compliance-documents"].Sources.SequenceEqual(["transport-licence", "affidavit"])
            && Status("iso-certificates") == AnnualEvaluationRules.NotAvailable && Status("pricing") == AnnualEvaluationRules.NotAvailable,
            "evidence: POD over completed jobs; required documents still in date over the five the register asks for");
        check(EvaluationEvidence.KpiMetric.Keys.Order().SequenceEqual(AnnualEvaluationRules.Kpis.Where(kpi => kpi.Method == AnnualEvaluationRules.Band)
                .Select(kpi => kpi.Code).Order())
            && EvaluationEvidence.KpiMetric.Values.All(metrics.ContainsKey),
            "evidence: every measured KPI of a campaign has its figure in the snapshot");

        var empty = EvaluationEvidence.Build(input with { Jobs = [], Delays = [], Issues = [], Invoices = [], SlaCases = [] })
            .ToDictionary(metric => metric.Code);
        check(empty["total-jobs"].Value == 0 && empty["total-jobs"].Status == AnnualEvaluationRules.Available
            && empty["carrier-otd"].Status == AnnualEvaluationRules.NotAvailable && empty["carrier-otd"].Value is null
            && empty["billing-accuracy"].Status == AnnualEvaluationRules.NotAvailable,
            "evidence: a carrier with no work has no rate — not a hundred, not a nought");
    }
}
