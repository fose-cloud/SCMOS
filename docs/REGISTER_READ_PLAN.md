# The register read — what to do after Phase 9

Set down on 22 September 2026, when the department lead chose the order: the database tier and
the App Service plan first (done that morning — Standard S2, B2), the cache's stale-while-revalidate
second (v2.7.77), and this as the work that follows Phase 9 of the AI platform.

## The problem, measured

Every screen that summarises the register — the dashboard's cards, KPI, notifications, the monitor,
the reports, the AI reads — and the web app's opening load read the **whole** `operation_jobs`
table: 2.6 MB of JSON, every row since the year began, parsed in full and held in memory
(`JobRegisterCache`). On the free-offer database and a B1 worker that read took 24–62 seconds in
working hours, and every job write dropped the cache, so the morning's first loads paid it one after
another; on 22 September the worker sat at 100 % CPU from 09:15 to 09:45 and `/health` went
unanswered for two minutes. The register grows by ~2,300 jobs a month; a bigger machine only
postpones the day the read is slow again.

## The fix

Stop asking for the whole register per screen.

1. **Promote the columns the summaries filter and count on** to real, indexed columns on
   `operation_jobs`: work date (as a date), customer, trucker (and its resolved company), category,
   status, owner id, plan time, arrival date and time. The JSON `data` stays the record; the columns
   are read models kept in step by `JobsRepository` on every write (one place — the duplicated-rules
   lesson).
2. **Let SQL count**: the dashboard's period/customer/trucker counts, the KPI engine's per-period and
   per-carrier bases, the notification counts and the monitor's queues become `GROUP BY` queries over
   those columns, returning figures and the rows a screen shows — not the register. `JobRules`
   (measurable, on-time with `CustomerTerms`, late-beyond) must remain the single reading; where a
   rule needs a computed column (minutes late), compute it on write into a column, never in a second
   place.
3. **The web app stops loading the register at open.** The dashboard reads aggregates; the workspace
   already pages (`/api/jobs/page`) and syncs deltas (`/api/jobs/changed`); the few screens that still
   need the whole list (exports) ask for it explicitly and are told the cost.
4. **The AI reads follow**: `OperationsSource`, `KpiService`, `DocumentSource` read the columns, not
   the snapshot; the Data Agent's answer keeps its provenance fields.

## Order and proof

Migration first (additive columns + backfill, reviewed as the plan requires); then one screen at a
time, each with a check that its figure equals the snapshot's figure over the same rows before the
snapshot path is removed — the two must agree on the day they coexist, or the new one is wrong.
Three to five days of work.

**22 September 2026, evening.** Always On was already on for the API App Service, so the 75–85 seconds
measured after each of that day's deploys was never Azure unloading an idle site — it was this
snapshot, built by whoever opened a screen first. `RegisterWarmup` now builds it five seconds after
the process starts (2,106 rows in 2.6 s on the development copy; the first authenticated read after
it returned in 0.65 s), so a restart costs the process and not a person. That removes the
restart-shaped part of the cost and leaves the part this plan is about: the working-hours reads,
which are to be measured on a normal morning through the SRE Agent's `requests` view before any
column is promoted. Until then: v2.7.77's stale-while-revalidate and the S2/B2 tiers carry
the load, and [`--check-register-cache`](../server/Scmos.Api/Data/RegisterCacheCheck.cs) proves the
cache's behaviour on a development database.
