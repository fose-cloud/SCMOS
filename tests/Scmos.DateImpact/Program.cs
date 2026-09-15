using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Scmos.Api.Rules;

// Standalone diagnostic: never start the API, migration service or background workers.
// Only SELECT statements. Credentials stay in memory and are never printed.
if (!args.Contains("--production-read-only"))
{
    Console.WriteLine("Requires --production-read-only. No connection attempted.");
    return 2;
}
try
{
    var secret = Environment.GetEnvironmentVariable("SCMOS_DATE_IMPACT_CONNECTION");
    Environment.SetEnvironmentVariable("SCMOS_DATE_IMPACT_CONNECTION", null);
    if (string.IsNullOrWhiteSpace(secret)) { Console.WriteLine("CONNECTION_SETTING_MISSING"); return 3; }
    var builder = new SqlConnectionStringBuilder(secret)
    {
        ConnectTimeout = 15, Encrypt = true, TrustServerCertificate = false,
        ApplicationName = "SCMOS date KPI read-only census"
    };
    // ApplicationIntent alone is not a write restriction; the commands below are SELECT-only.
    using var connection = new SqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    using var metadata = connection.CreateCommand();
    metadata.CommandText = "SELECT is_read_committed_snapshot_on, snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID()";
    bool rcsi; int snapshot;
    using (var reader = await metadata.ExecuteReaderAsync())
    {
        await reader.ReadAsync(); rcsi = reader.GetBoolean(0); snapshot = reader.GetByte(1);
    }
    if (!rcsi && snapshot != 1)
    {
        Console.WriteLine("NO_SNAPSHOT_ISOLATION: census not run; no database settings changed.");
        return 4;
    }
    using var transaction = rcsi ? null : connection.BeginTransaction(System.Data.IsolationLevel.Snapshot);
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandTimeout = 60;
    // Project only six required business values. Inspect JSON field types server-side to
    // detect rows the production JobRecord deserializer would reject; do not retrieve PII.
    command.CommandText = """
        SELECT ISJSON(j.[data], OBJECT) AS valid_object,
            p.work_date, p.arr_date, p.plan_time, p.arr_time, p.category, p.job_status,
            p.incompatible_types, p.duplicate_properties
        FROM dbo.operation_jobs j
        OUTER APPLY (
            SELECT
                MAX(CASE WHEN LOWER([key]) = 'date' THEN [value] END) AS work_date,
                MAX(CASE WHEN LOWER([key]) = 'arrdate' THEN [value] END) AS arr_date,
                MAX(CASE WHEN LOWER([key]) = 'plantime' THEN [value] END) AS plan_time,
                MAX(CASE WHEN LOWER([key]) = 'arrtime' THEN [value] END) AS arr_time,
                MAX(CASE WHEN LOWER([key]) = 'cat' THEN [value] END) AS category,
                MAX(CASE WHEN LOWER([key]) = 'status' THEN [value] END) AS job_status,
                SUM(CASE WHEN [type] NOT IN (0,1) THEN 1 ELSE 0 END) AS incompatible_types,
                COUNT(*) - COUNT(DISTINCT LOWER([key])) AS duplicate_properties
            FROM OPENJSON(CASE WHEN ISJSON(j.[data], OBJECT) = 1 THEN j.[data] ELSE N'{}' END)
            WHERE LOWER([key]) IN (
                'key','id','cat','op','opid','date','customer','trucker','jobcode','abs','jobno',
                'plantime','arrdate','arrtime','closingdate','closingtime','status','container',
                'licence','driver','contact','type','weight','reason','incident','seal',
                'plant','destination','cyyard','wh')
        ) p
        """;
    var rows = new List<JobRecord>();
    var excludedNonObject = 0; var excludedTypes = 0; var ambiguousDuplicates = 0; var scanned = 0;
    var startedUtc = DateTimeOffset.UtcNow;
    using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            scanned++;
            if (reader.IsDBNull(0) || reader.GetInt32(0) != 1) { excludedNonObject++; continue; }
            if (!reader.IsDBNull(7) && reader.GetInt32(7) > 0) { excludedTypes++; continue; }
            if (!reader.IsDBNull(8) && reader.GetInt32(8) > 0) { ambiguousDuplicates++; continue; }
            string S(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
            rows.Add(new JobRecord { Date=S(1), ArrDate=S(2), PlanTime=S(3), ArrTime=S(4), Cat=S(5), Status=S(6) });
        }
    }
    // A read-only snapshot transaction can simply roll back; no statements changed records.
    transaction?.Rollback();
    var result = new
    {
        startedUtc, finishedUtc = DateTimeOffset.UtcNow,
        source = "Azure SQL / dbo.operation_jobs; six JSON business fields; no row identifiers",
        consistency = rcsi ? "single SELECT with READ_COMMITTED_SNAPSHOT" : "SNAPSHOT transaction",
        scanned, excludedNonObject, excludedTypes, ambiguousDuplicates,
        fullScan = true, population = "all categories/statuses; no date/customer/trucker filter",
        dateOnlyCounterfactual = "exclude measurable rows where either date fails Formats.ParseDay; unchanged time parser",
        overall = Summarize(rows),
        byCategory = rows.GroupBy(j => j.Cat).OrderBy(g=>g.Key).Select(g=>new { category=g.Key, counts=Summarize(g.ToList()) }),
        byPlanMonth = rows.GroupBy(j => Month(j.Date)).OrderBy(g=>g.Key).Select(g=>new { month=g.Key, counts=Summarize(g.ToList()) })
    };
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented=true }));
    return ambiguousDuplicates == 0 ? 0 : 5;
}
catch (SqlException ex)
{
    // Never print Message/ToString: these can expose endpoint, login or connection information.
    Console.WriteLine(JsonSerializer.Serialize(new { error="SQL_READ_FAILED", numbers=ex.Errors.Cast<SqlError>().Select(e=>e.Number).Distinct() }));
    return 6;
}
catch (Exception ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { error="DIAGNOSTIC_FAILED", type=ex.GetType().Name }));
    return 7;
}

static string Month(string value)
{
    var clean = Formats.Clean(value);
    return clean.Length >= 10 && clean[2]=='/' && clean[5]=='/' &&
        int.TryParse(clean.AsSpan(6,4), out _) && int.TryParse(clean.AsSpan(3,2), out _)
        ? clean.Substring(6,4)+"-"+clean.Substring(3,2) : "(blank-or-unrecognized)";
}

static string Kind(string value)
{
    var clean = Formats.Clean(value);
    if (clean.Length == 0) return "blank";
    if (Formats.ParseDay(value) is not null) return "valid";
    return Regex.IsMatch(clean, @"^\d{2}/\d{2}/\d{4}$") ? "impossible-calendar-date" : "malformed-or-placeholder";
}

static string Shape(string value)
{
    var text = Formats.Clean(value);
    if (text.Length == 0) return "blank";
    if (Formats.ParseDay(text) != null) return "valid";
    if (Regex.IsMatch(text, @"^\d{2}/\d{2}/\d{4}$")) return "impossible-calendar-date";
    if (Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}")) return "iso-like";
    if (Regex.IsMatch(text, @"^\d{1,2}/\d{1,2}/\d{2,4}$")) return "slash-date-noncanonical";
    if (Regex.IsMatch(text, @"^\d{2}/\d{2}/\d{4}.+")) return "date-with-extra-text";
    if (Regex.IsMatch(text, @"^\d+(\.\d+)?$")) return "numeric-not-assumed-excel-date";
    if (Regex.IsMatch(text, @"^\d{1,2}:\d{2}$")) return "time-without-date";
    if (text.Equals("WAIT", StringComparison.OrdinalIgnoreCase)) return "WAIT";
    if (!Regex.IsMatch(text, @"\d")) return "other-text-without-digits";
    return "other-mixed-text";
}

static object Summarize(List<JobRecord> rows)
{
    var measurable = rows.Where(LegacyMeasurable).ToList();
    var strict = measurable.Where(j=>Formats.ParseDay(j.Date)!=null && Formats.ParseDay(j.ArrDate)!=null).ToList();
    var met = measurable.Count(LegacyOnTime); var strictMet = strict.Count(JobRules.IsOnTime);
    double? Rate(int n,int d) => d==0 ? null : Math.Round(100.0*n/d,6);
    var oldRate=Rate(met,measurable.Count); var newRate=Rate(strictMet,strict.Count);
    return new {
        jobs=rows.Count, legacyMeasurable=measurable.Count, legacyOnTime=met,
        strictMeasurable=strict.Count, strictOnTime=strictMet,
        excludedMeasurable=measurable.Count-strict.Count, excludedOnTime=met-strictMet,
        legacyRatePct=oldRate, strictRatePct=newRate, changePercentagePoints=newRate-oldRate,
        measurableButNoMinutesLate=measurable.Count(j=>JobRules.MinutesLate(j)==null),
        planDates=rows.GroupBy(j=>Kind(j.Date)).ToDictionary(g=>g.Key,g=>g.Count()),
        arrivalDates=rows.GroupBy(j=>Kind(j.ArrDate)).ToDictionary(g=>g.Key,g=>g.Count()),
        invalidPlanShapes=rows.Where(j=>Kind(j.Date)!="valid" && Kind(j.Date)!="blank")
            .GroupBy(j=>Shape(j.Date)).ToDictionary(g=>g.Key,g=>g.Count()),
        invalidArrivalShapes=rows.Where(j=>Kind(j.ArrDate)!="valid" && Kind(j.ArrDate)!="blank")
            .GroupBy(j=>Shape(j.ArrDate)).ToDictionary(g=>g.Key,g=>g.Count()),
        invalidArrivalInCompletedJobs=rows.Count(j=>Kind(j.ArrDate)!="valid" && Kind(j.ArrDate)!="blank" && JobRules.IsDone(j.Status)),
        legacyPositiveButInvalidPlan=rows.Count(j=>LegacyDateNumber(j.Date)>0 && Formats.ParseDay(j.Date)==null),
        legacyPositiveButInvalidArrival=rows.Count(j=>LegacyDateNumber(j.ArrDate)>0 && Formats.ParseDay(j.ArrDate)==null)
    };
}

// Frozen pre-fix comparator: changing production rules must not silently change the baseline.
static int LegacyDateNumber(string? value)
{
    var text = Formats.Clean(value);
    if (text.Length < 10 || text[2] != '/' || text[5] != '/'
        || !int.TryParse(text.AsSpan(0, 2), out var day)
        || !int.TryParse(text.AsSpan(3, 2), out var month)
        || !int.TryParse(text.AsSpan(6, 4), out var year)) return 0;
    return year * 10000 + month * 100 + day;
}
static bool LegacyMeasurable(JobRecord job) =>
    Formats.TimeMinutes(job.PlanTime) != null && Formats.TimeMinutes(job.ArrTime) != null
    && LegacyDateNumber(job.Date) > 0 && LegacyDateNumber(job.ArrDate) > 0;
static bool LegacyOnTime(JobRecord job) =>
    LegacyMeasurable(job) && (LegacyDateNumber(job.ArrDate) < LegacyDateNumber(job.Date)
    || (LegacyDateNumber(job.ArrDate) == LegacyDateNumber(job.Date)
        && Formats.TimeMinutes(job.ArrTime) <= Formats.TimeMinutes(job.PlanTime)));
