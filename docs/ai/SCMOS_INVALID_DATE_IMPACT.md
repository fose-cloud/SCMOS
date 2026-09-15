# Invalid date impact assessment — 15 September 2026

Scope: source-code inspection, deterministic rule tests, and an authorized
read-only Production census at 15 September 2026 14:00:12–14:00:13 Asia/Bangkok.
Grain is one operation_jobs row (primary key), not job code.
No Production business records, formulas, Azure settings or deployments were
changed. A subsequent local-only calendar-validation fix is described below.

## Follow-up classification and local fix — 15 September 14:05 Bangkok

A second complete read-only snapshot at 07:05:27–07:05:28 UTC again contained
3,537 jobs with identical overall KPI counts and zero skipped/ambiguous rows.

- Nonblank unparseable DATE: WAIT in 7 IMPORT jobs, mixed text in 1 EXPORT job.
- Nonblank unparseable ARRIVAL DATE: 201 slash-separated numeric values that do
  not match canonical DD/MM/YYYY (54 IMPORT, 147 EXPORT), plus 4 mixed-text IMPORT
  values. This shape check does not establish the intended date or assume a year.
- All 205 jobs with nonblank unparseable ARRIVAL DATE are completed according to
  JobRules.IsDone. This is a concrete completeness problem for historical OTD:
  these jobs are excluded from measurement. The corrected OTD after human
  resolution cannot be calculated without verified dates and times.
- DATE and ARRIVAL DATE shape counts are field-level; do not add overlapping
  field totals as unique jobs. Blank arrival values are a separate population.

The safe next data step is review against source documents, prioritizing these
205 completed jobs. No replacement dates were inferred, exported or written.
The census reads no job identifiers, so it is a shape/impact report, not a
record-level remediation list.

Local implementation (not pushed/deployed): IsDate, DateNumber and PartsOf now
reject impossible calendar dates. The jobs PUT endpoint rejects newly introduced
impossible DATE/ARRIVAL DATE/CLOSING DATE values before any batch save, while
allowing byte-for-byte unchanged legacy values and placeholders such as WAIT.
Malformed noncanonical text is not automatically converted or newly blanket-
blocked; the new write guard targets impossible canonical calendar literals.
Existing AI change validators still apply. No new AI write capability is granted.
The diagnostic runner freezes the pre-fix numeric parser for reproducible legacy
comparisons even after the main rules change.

Verification: 343 AI foundation/Operations/audit checks passed locally after the
fix, including invalid month/day, non-leap century, valid leap years, trailing
text, KPI/period exclusion, new-date rejection and unchanged-legacy acceptance.
No live OpenAI calls or Production writes were part of those tests.

## Production result

Read all 3,537 rows from dbo.operation_jobs in one SELECT under existing
READ_COMMITTED_SNAPSHOT. There was no row limit. No malformed JSON objects,
incompatible recognized field types, or ambiguous duplicate JSON properties were
excluded (all three counts zero). No firewall changes were necessary.

Only date, arrDate, planTime, arrTime, cat and status values were retrieved.
Other recognized JSON property types were checked server-side without returning
their values. Credentials came from the authorized Azure App Setting into
process memory, were not logged or written to disk, and the temporary environment
variable was cleared/restored. The standalone runner does not start the API.

| Population | Jobs | Measurable (old / strict) | On-time (old / strict) | OTD (old / strict) | Change |
| --- | ---: | ---: | ---: | ---: | ---: |
| All register | 3,537 | 1,935 / 1,935 | 1,377 / 1,377 | 71.162791% / 71.162791% | 0 pp |
| IMPORT | 2,357 | 1,199 / 1,199 | 917 / 917 | 76.480400% / 76.480400% | 0 pp |
| EXPORT | 1,120 | 736 / 736 | 460 / 460 | 62.500000% / 62.500000% | 0 pp |
| DELIVERY | 60 | 0 / 0 | 0 / 0 | Not measurable | Not applicable |
| July 2026 plan dates | 1 | 1 / 1 | 0 / 0 | 0% / 0% | 0 pp |
| August 2026 plan dates | 2,283 | 1,164 / 1,164 | 760 / 760 | 65.292096% / 65.292096% | 0 pp |
| September 2026 plan dates | 1,240 | 770 / 770 | 617 / 617 | 80.129870% / 80.129870% | 0 pp |
| Blank/unrecognized plan date | 13 | 0 / 0 | 0 / 0 | Not measurable | Not applicable |

The comparison changes ONLY calendar validation for the existing measurable
population; time parsing, all-category/status population and IsOnTime stay fixed.
No impossible calendar dates were found in either date field. No positive
DateNumber value failed ParseDay. No measurable row had null MinutesLate.
Consequently this snapshot shows zero OTD impact from this particular defect.
This does not mean that all date data is complete or that the code defect is fixed.

| Field | Valid calendar date | Blank/recognized blank placeholder | Nonblank unparseable | Impossible calendar date |
| --- | ---: | ---: | ---: | ---: |
| DATE | 3,524 | 5 | 8 | 0 |
| ARRIVAL DATE | 2,899 | 433 | 205 | 0 |

Nonblank unparseable includes malformed text or placeholders not recognized by
Formats.Clean; values were not exported or classified further in this census.
The two field counts may overlap in the same job: do not add 13 and 638 as unique
affected jobs. Missing arrival dates can be expected for future/not-yet-arrived
jobs; this readout does not classify them all as operator errors. The 1,602
non-measurable jobs also include missing/invalid times, not just date problems.

Reproduction: tests/Scmos.DateImpact/Program.cs contains the exact SELECT and uses
the current JobRules/Formats functions. Build is independent of execution;
execution requires --production-read-only and a transient
SCMOS_DATE_IMPACT_CONNECTION environment variable. No credential belongs in a file
or command-line argument. The result is a full-register rule comparison, not a
verification of a particular browser's filters or five-minute cached snapshot.
No customer/carrier values were retrieved; merged-carrier scorecards were not
individually reconciled. Other KPI formulas, date semantics (including valid but
incorrect real-world dates), time-format changes and historical snapshots are
outside this result.

## Confirmed mechanism

The pre-fix Formats.DateNumber built YYYYMMDD from numeric substrings without validating
calendar existence. IsMeasurable accepts positive results and valid-looking times;
IsOnTime compares those numbers. Equal planned/arrival dates of 31/02/2026 can
therefore count as measurable and on-time. ParseDay and MinutesLate reject this
calendar date. Boundary tests reproduce the discrepancy.

## Impact surface

| Consumer | Confirmed code path / possible consequence |
| --- | --- |
| KpiService headline and carriers | IsMeasurable denominator and IsOnTime numerator can contain invalid dates; no IsKpiReady filter is applied to these counts |
| KpiEngine OTD and carrier components | Same measurement functions; carrier rate and minimum-sample decisions may change after exclusions |
| MonthlyReport.Line | Same measurement functions; exclusion may move a carrier below its five-measurement threshold |
| Date grouping and undated count | KpiService uses DateNumber; impossible dates may be grouped as dated, not counted as undated |
| Period filtering | InPeriod/PartsOf extracts date substrings; an impossible February day may still enter February |
| Minute-based lateness / Monitor | Calendar parsing rejects invalid dates, so disagreement with the KPI population is possible |

Source paths: server/Scmos.Api/Rules/Formats.cs; Rules/JobRules.cs;
Rules/MonthlyReport.cs; Rules/MonitorRules.cs; Services/KpiService.cs;
Services/KpiEngine.cs. Reproduction: tests/Scmos.Ai.Checks/SemanticBoundaryChecks.cs.

## Direction is not predetermined

Removing an invalid row counted on-time may lower the percentage; removing an
invalid row counted late may raise it. Both directions are reproduced with
isolated test fixtures, not measured company data. If N/M is the current ratio,
and a invalid measurable rows include b counted on-time rows, the hypothetical
calendar-only result is (N-b)/(M-a), undefined when M-a=0.
This comparison holds the population, time parser, and other rules unchanged.

## Remaining limits

The current full-register and category/month impact has now been measured above.
The defect remains confirmed by fixtures but was not present in the measured
Production population. Historical occurrence and correctness of otherwise valid
dates have not been established. Do not use this snapshot to claim future safety.

## Census method and follow-up boundaries

1. Read only authoritative key, category/status, reporting grouping and the four
   date/time fields from one consistent snapshot; avoid PII/notes.
2. Separate blanks/placeholders, malformed/trailing-text dates, impossible calendar
   dates, and valid calendar dates. Keep bad time inputs separate to isolate dates.
3. Compare legacy measurable/on-time counts with calendar-only exclusions on the
   exact same population; include all-register and consumer-specific slices.
4. Report affected key count, old/new denominators and numerators, percentage-point
   differences and below-minimum/empty groups. Return aggregates, not raw records.
5. Assess period filtering separately: changing it changes population as well as
   measurement and must not be mixed into the calendar-only comparison.

Access boundary: the user explicitly authorized Azure App Settings credentials
for necessary read-only business data and KPI impact assessment for this census.
No authorization to correct business data is inferred. Any fix to date parsing,
data remediation or deployment is a separate step; do not automatically normalize
the unparseable values into guessed dates.
