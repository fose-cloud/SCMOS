# Moving the register to Azure SQL

The July plan is not re-keyed and not re-derived from `ops.json`. The 2,102 jobs
in the old D1 register have been cleaned, reassigned and edited, and each one
carries its own history — that is the thing being moved.

## 1. Export from D1

```bash
node migration/export-d1.mjs
```

Reads `.wrangler/state/v3/d1/miniflare-D1DatabaseObject/*.sqlite` directly, so
the dev server can stay running, and writes `migration/register-export.json` in
the same shape as the old app's `GET /api/jobs`.

Point it at a file instead if you have a copy from somewhere else:

```bash
node migration/export-d1.mjs path/to/snapshot.sqlite
```

## 2. Load into Azure SQL

```bash
dotnet run --project server/Scmos.Api -- --seed ../../migration/register-export.json --as migration@leschaco.com
```

Creates the schema if it is not there yet, then upserts on the job key in
batches of 500. Running it twice is the same as running it once, so a run that
dies halfway is resumed by starting it again.

`--as` is what lands in `updated_by` and in each job's history for this move.
Leave it off and the rows are stamped `migration`.

## 3. Check it

```sql
SELECT COUNT(*) FROM operation_jobs;                          -- 2102
SELECT owner, owner_id, COUNT(*) FROM operation_jobs
 GROUP BY owner, owner_id ORDER BY COUNT(*) DESC;
```

Expected, and what the export reported coming out of D1:

| Owner      | Id    | Jobs |
| ---------- | ----- | ---- |
| Uthai      | OP-02 |  589 |
| Ananya     | OP-03 |  559 |
| Watsana    | OP-01 |  419 |
| Maliwan    | OP-04 |  309 |
| Jiratchaya | OP-05 |  226 |

`owner_id` is filled in by the move. It did not exist on D1 — ownership was
matched on the operator's display name, which real sign-in would have broken.
Anything that comes out with a blank `owner_id` is a job whose owner is not one
of the five, and it needs a person.

## Nothing here is committed

Exports and snapshots hold real driver names, phone numbers and truck plates.
`.gitignore` keeps `migration/*.json` and `migration/*.sqlite` out of the
repository — keep it that way, and delete them once Azure SQL holds the register.
