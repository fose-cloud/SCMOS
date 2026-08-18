# SCMOS — Subcontractor Management Operating System

Operation workspace, monitoring and supplier governance for
LESCHACO (Thailand) Ltd.

Two applications: a **React / Next.js** front end that holds the operational
business rules, and an **ASP.NET Core** API that owns persistence and the
integrations. Both run on Azure App Service.

| Component | Technology |
| --- | --- |
| Frontend | React / Next.js (App Router, `output: standalone`) |
| Backend | .NET 10 / ASP.NET Core minimal APIs |
| Database | Azure SQL Database (EF Core) |
| File storage | Azure Blob Storage |
| Long-term storage | Blob lifecycle policy → Cool / Archive |
| Hosting | Azure App Service (one for web, one for API) |
| AI | OpenAI GPT, strict JSON-schema responses |
| Authentication | App Service Web App Login (Entra ID) |
| Secrets | Azure Key Vault, read with the managed identity |
| Monitoring | Azure Monitor + Application Insights |
| Reporting | Excel (`xlsx`, in the browser) |
| Domain | Cloudflare DNS |
| Backup | Azure SQL automated backups |
| Source control | GitHub, deployed by GitHub Actions |

## Where the rules live

The rules that decide anything live in C#, in `server/Scmos.Api/Rules/`: the data
standard, the status vocabulary, the workflow state machine, carrier assignment,
the pre-run SLA, delay classification, the KPI measures and the AI permission
matrix. A rule the backend cannot see is a rule the backend cannot enforce, and a
browser is not a place to enforce anything.

The port was checked against the TypeScript it replaced on identical raw data
before anything relied on it — 2,102 jobs, 631 needing action, 183 gate-in risks,
109 measurable, 28 without a carrier, on both sides.

What stays in TypeScript is what only the browser does: reading and writing Excel
workbooks, the import preview and duplicate decisions, and the cleanup pass the
operator runs and reviews. `app/scmos/theme.ts` holds the one copy of the status
buckets the screens share.

> The recurring lesson of this codebase: **a duplicated rule drifts.** The status
> buckets existed in four places and a status migration silently moved COMPLETED
> from 11 to 239 before they were consolidated. When a rule needs to exist on both
> sides, one side imports it — `migration/build-rates.mjs` imports the parser from
> `app/scmos/rates.ts` rather than restating it.

The API also owns what the browser must not: the register, the blobs, the rate
book, the OpenAI key, and who is allowed to write.

## Prerequisites

- Node.js `>= 22.13.0`
- .NET SDK `10.0`
- SQL Server for local development — LocalDB is enough
  (`sqllocaldb start MSSQLLocalDB`)

## Local development

Two processes. The API first:

```bash
dotnet run --project server/Scmos.Api -- --migrate
```

```bash
ASPNETCORE_URLS=http://localhost:5080 dotnet run --project server/Scmos.Api
```

Then the web app:

```bash
npm install
```

```bash
npm run dev
```

Copy `.env.example` to `.env.local` first — it points the web app at the API.

Opens on `http://localhost:3000`. There is no identity provider locally, so the
sign-in screen keeps the eight demo accounts, which is how you switch roles to
see the permission model. The account travels to the API as `X-Scmos-Dev-User`,
and the API only honours that header when it is running in Development.

### Moving the register in

The 2,102 July jobs live in the old D1 database. See
[migration/README.md](migration/README.md) — export, then:

```bash
dotnet run --project server/Scmos.Api -- --seed ../../migration/register-export.json
```

## Authentication

App Service **Web App Login** signs the user in at the edge of the *web* app and
passes the verified principal on in `X-MS-CLIENT-PRINCIPAL`. The web app forwards
those headers to the API through its own `/api/*` proxy route, along with a
shared key from Key Vault.

The API refuses the forwarded principal unless that key comes with it. That is
what stops anyone who can reach the API from claiming to be somebody:

| `Auth:Mode` | When to use it | What is trusted |
| --- | --- | --- |
| `Proxy` (default) | Deployed | Platform headers, **only** with a matching `X-Scmos-Proxy-Key` |
| `Platform` | The API is itself behind Web App Login | Platform headers, which App Service guarantees |
| `Development` | Local only | The demo account in `X-Scmos-Dev-User` |

Lock the API App Service down with access restrictions as well, so only the web
app's outbound addresses reach it. The key is the second lock, not the only one.

### Roles and capabilities

Eight roles, and what each may do is a **capability set**, not a name test.
[`Rules/Roles.cs`](server/Scmos.Api/Rules/Roles.cs) owns it.

| Role | Scope | May not |
| --- | --- | --- |
| Administrator | Full System | — |
| Manager / Assistant Manager | Department Overview | administer the register |
| Operation Supervisor | Team Control | change rates, approve retention |
| Subcontractor | Operational Management | **see the rate book**, see the team |
| Operation User | Own Workspace | edit others' jobs, approve anything |
| CS | View / Upload | edit the operational record |
| Management | Dashboard | see the register |
| Viewer | Read Only | write anything |

This replaced a `SupervisorRoles` array that four files tested against — which
meant "who may approve an AI change" and "who may reassign a job" were the same
question by accident, and adding a ninth role would have granted it whatever the
array happened to contain. An unrecognised role now gets **Viewer's** grants, not
the default role's: a typo in a role claim should cost somebody their edit rights,
never hand them somebody else's.

The Subcontractor row is the one worth arguing about. A carrier signing in to
work their own jobs must not see `ViewRates` — the book holds seventeen other
carriers' negotiated prices, and one subcontractor reading another's rates is the
worst thing this system could leak. `/api/rates` refuses them, and the
Administration screen shows the whole matrix read from the same table the API
enforces against, so what people read cannot drift from what is in force.

**The browser decides nothing about identity or permission.** `/api/me` answers
with the role, the owner id and the capability list, and the API is the copy that
enforces them. Three separate second opinions were removed to get there:

- a `ROLE_BY_EMAIL` table in `app/auth.ts` whose own comment told you to "keep it
  in step with Auth:Roles" — two answers to "is this person a supervisor", and
  the one users would have seen was not the one enforced;
- a `matchAccount` beside `StaffDirectory.Match`, deciding which directory person
  an email is — and therefore which jobs are yours;
- an edit test reading `role !== "Operation User"`, in three files, true of a
  supervisor **and** of every read-only role.

Until `/api/me` answers, an account has no owner id and no capabilities: no job
looks like yours and no write control is offered. That is the safe direction to
be wrong in — but **"safe when it fails" is only half a design; the other half is
saying that it failed.** The first version had no second half, and when the API
was briefly unreachable the grid went read-only in complete silence while the
rows still said MY JOB. It looked like the screen was simply broken.

The fetch now retries with backoff to thirty seconds, and a failure shows a
banner saying editing is unavailable, why, and offering a retry. Recovery is
automatic: bring the API back and the grid becomes editable again without a
reload.

An email in neither `Auth:Roles` nor the staff directory gets **Viewer**, and the
workspace says so rather than silently showing nothing — an account nobody has
added yet should be able to read and nothing else, and an empty workspace with no
explanation is indistinguishable from a broken system.

> **Enforcement is at the API, not the screen.** `PUT /api/jobs` checked only
> that you were signed in: a Viewer could write to the register, and an operator
> could write to another operator's job, because ownership lived in the grid that
> drew the rows. `DELETE` had no check at all beyond sign-in. Both now test the
> capability and, for anyone without `EditAnyJob`, refuse a batch containing
> somebody else's job. Verified: Viewer write → 403, cross-owner write → 403,
> Viewer delete → 403, own-job write → 200, supervisor → 200.

### Audit trail

Every important change lands in one append-only `audit_events` table: **who ·
what · old value · new value · date · time · IP · session · reason.**

```
17/08 21:09  OP-01 (Operation User)  เปลี่ยนผู้ขนส่ง
260600800773 · ALTEK    DGT → PK TRANSPORT
reason: DGT capacity unavailable        ::1 / 0HNNSD49K5EQ5
```

- **[`AuditService`](server/Scmos.Api/Services/AuditService.cs) is the only
  writer.** Callers hand it what changed; it works out who, when and from where.
  An endpoint that had to remember to capture the caller's address would
  eventually forget, and the row it wrote would look complete.
- **Old values are read before the write.** The save endpoint snapshots the
  register first, because a change without its previous value is information
  rather than evidence.
- **Some changes must be explained.** Swapping a carrier, changing a rate,
  closing a CAR/PAR, approving retention, replacing the register — the values
  alone do not say why. Replacing a carrier prompts for a reason in the UI;
  naming the *first* one does not, because there is nothing to explain about
  filling a blank and asking anyway teaches people to type "update".
- **Recording never fails a change.** If the trail cannot be written the change
  still happens and the failure is logged loudly. Losing an audit row is bad;
  refusing an operator's edit because a log table is full is worse.
- **Nothing deletes from it**, and there is no route that edits an entry. A trail
  somebody can tidy up is not a trail.
- Reading it needs `ViewAudit` — supervisor and above. A system where everyone
  reads everyone's history is a different system from one where each person sees
  their own work.

Behind App Service the client address comes from `X-Forwarded-For` (first entry,
port stripped); recording the load balancer in every row would make the field
worthless.

### Notification engine

Twelve alert kinds, [named once in
`Rules/Notifications.cs`](server/Scmos.Api/Rules/Notifications.cs) and computed
from the register by `/api/notifications`:

supplier not confirmed · booking missing data · pre-run not confirmed · truck
delay · E-Card mismatch · document unclear · POD missing · supplier document
expiring · audit expiring · CAR/PAR overdue · capacity shortage · KPI below
target.

A fixed list, because the value of an alert is inversely proportional to how many
kinds there are, and the fastest way to make a team ignore a real alert is to sit
it next to nine they cannot fix. Every kind carries the action a person is meant
to take and the screen that answers it — an alert nobody can act on is noise
wearing a warning colour.

Nothing is stored. An alert is a fact about the current state, so it stops
existing the moment the state changes; a notifications table would need a rule
for marking each row read and another for deleting it, and the first time those
disagreed with reality somebody would be chasing a truck that already arrived.
Alerts are grouped — "1,080 jobs missing a plate" is actionable, 1,080 rows are a
wall people scroll past.

These rules used to live in the browser, which meant the twelve the operation
agreed on were invisible to the backend and a second copy waiting to disagree.

### TODAY

The front page answers the first question anyone has on opening the system.
`/api/dashboard/today` measures the plan date, not the write date — a job planned
for today is today's problem whether it was keyed last week or this morning. When
the plan holds nothing for today it reports the nearest planned day and says so,
rather than five zeroes that look like a quiet morning. (The July plan is in the
past, so that is currently the normal case.)

Any figure that cannot be measured renders as **ยังวัดไม่ได้**, in a smaller,
grey type so it cannot be mistaken for a number at a glance. Capacity risk is the
live example: nobody has told the system what capacity they have, so reporting 0
would be a claim it has no basis for.

### Job ownership

Ownership is decided on an **owner id** (`OP-01`…), never on a display name. The
plan workbooks call an operator "Watsana"; Entra introduces the same person as
`watsana.k@leschaco.co.th`. Matching those two spellings against each other is
what the previous version did, and switching real sign-in on would have taken
every job away from every operator at once. `opId` sits between them, is
backfilled from the operator name on the way in, and is written to the column and
the stored JSON together so the two cannot drift.

[`StaffDirectory`](server/Scmos.Api/Auth/Directory.cs) is the directory.
`ACCOUNTS` in [app/scmos/nav.ts](app/scmos/nav.ts) is not a second copy of it: it
is the development sign-in list, and the name-to-owner-id map the Excel importer
needs before a job has ever reached the API. Which directory person a signed-in
email *is* — the answer ownership depends on — is decided in C# and read from
`/api/me`.

## Getting it on the web, the short way

Four resources, all from the Azure Portal in a browser. No CLI, no Key Vault, no
managed identity, no slots, no GitHub secrets. About half an hour.

The long guide below is the end state. Almost none of it is required to have the
system running and signed into — that is worth saying plainly, because the length
of it suggests otherwise.

**1. Create four things** (same region, same resource group)

| | Setting |
| --- | --- |
| App Service | **Node 22**, Linux — the web app |
| App Service | **.NET 10**, Linux — the API, same plan |
| Azure SQL Database | Basic or S0 is plenty for 2,102 jobs |
| Storage account | Standard LRS, one private container `operation-files` |

**2. Tell them about each other** — Configuration → Application settings

API:

```
ConnectionStrings__ScmosDb = <the Azure SQL connection string, SQL auth>
Auth__Mode                 = Proxy
Auth__ProxyKey             = <any long random string>
Storage__ConnectionString  = <the storage account access key connection string>
Storage__Container         = operation-files
Auth__Roles__<you>@leschaco.co.th = Administrator
```

Web:

```
SCMOS_API_BASE_URL  = https://<api-app>.azurewebsites.net
SCMOS_API_PROXY_KEY = <the same random string>
```

That last pair is the whole security model at this size: the API trusts the
identity the web app forwards only when the shared key comes with it. Key Vault
and managed identity replace the two plain strings later; they change nothing
about how it works.

**3. Turn on Authentication** on the **web** app only — Microsoft, require
authentication. That is the Microsoft 365 sign-in, done.

**4. Deploy** — from VS Code with the Azure App Service extension, right-click
each app and Deploy. Or `npm run build` and drag the folder into the portal's
Advanced Tools. GitHub Actions is nicer once it changes often; it is not needed
to see it working today.

**5. Load the data** — see [step 6 of the long guide](#6-load-the-data), pointing
the three commands at the Azure connection string instead of LocalDB.

That is a working system. What the long guide adds, and when it is worth adding:

| Add | When |
| --- | --- |
| Key Vault + managed identity | Before anyone outside the team can read the app settings |
| GitHub Actions | Once you are deploying more than about once a week |
| Staging slot | Before there is data you would be upset to lose |
| Application Insights | The first time something breaks and nobody knows why |
| Blob lifecycle policy | Any time before the files are a year old |

None of them are load-bearing on day one. All of them are worth having by the
time this is carrying real work.

## Deploying to Azure

Nothing here has run against real Azure yet — everything in this repository is
verified on SQL Server LocalDB and the storage emulator. Work through this in
order; each step assumes the one before it.

### 0. Merge to `main`

Both workflows trigger on `main`. The work is on `azure-dotnet-migration`, so
nothing deploys until that branch is merged.

### 1. Resources

An Azure SQL database, a storage account, two App Services on one plan
(**Node 22** for web, **.NET 10** for the API), a Key Vault, and an Application
Insights resource.

Turn on a system-assigned managed identity for both App Services, then grant:

- **Key Vault Secrets User** on the vault — both
- **Storage Blob Data Contributor** on the storage account — API only
- a contained user in Azure SQL for the API's identity, in `db_datareader`,
  `db_datawriter` and `db_ddladmin`

`infra/setup-storage.sh` does the whole storage side, including the identity and
the lifecycle policy:

```bash
RESOURCE_GROUP=rg-scmos STORAGE_ACCOUNT=scmosfiles API_APP=scmos-api ./infra/setup-storage.sh
```

### 2. Secrets in Key Vault

| Secret | Used by |
| --- | --- |
| `ConnectionStrings--ScmosDb` | API — Azure SQL, authenticating as the managed identity |
| `OpenAI--ApiKey` | API — the document reader |
| `Auth--ProxyKey` | Both — the shared key on forwarded identity |

`Auth--ProxyKey` is any long random string. Without it the API refuses every
request and logs that it is doing so; that is deliberate, because the alternative
is an API that trusts identity headers anybody can set.

### 3. App settings

API App Service:

```
KeyVault__Uri                            = https://<vault>.vault.azure.net/
Auth__Mode                               = Proxy
Storage__ServiceUri                      = https://<account>.blob.core.windows.net
Storage__Container                       = operation-files
APPLICATIONINSIGHTS_CONNECTION_STRING    = <from the App Insights resource>
```

Web App Service:

```
SCMOS_API_BASE_URL     = https://<api-app>.azurewebsites.net
SCMOS_API_PROXY_KEY    = @Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/Auth--ProxyKey/)
NEXT_PUBLIC_SITE_URL   = https://<your domain>
```

**Then who gets more than read-only.** Since an email in neither `Auth:Roles` nor
the staff directory gets `Viewer`, deploying without this leaves everybody unable
to write. One app setting per person, on the API:

```
Auth__Roles__titchanatorn.k@leschaco.co.th = Operation Supervisor
Auth__Roles__nattikorn.s@leschaco.co.th    = Assistant Manager
Auth__Roles__<admin>@leschaco.co.th        = Administrator
```

The five operators are matched by the staff directory on the local part of their
email and need no entry. Valid roles are listed in `appsettings.json`.

### 4. Authentication

Turn on **Authentication** on the *web* App Service — Microsoft as the provider,
require authentication. Leave it **off** on the API: the API is reached only
through the web app's proxy, and `Auth:Mode=Proxy` is what makes it trust the
forwarded headers.

LESCHACO staff sign in with Microsoft 365, so this is the whole of the SSO work.

The path was tested end to end against the real header shape — a base64
`X-MS-CLIENT-PRINCIPAL` carrying the claims Entra emits — before it had ever run
on Azure:

| Signs in as | Resolves to | Role |
| --- | --- | --- |
| `watsana.k@leschaco.co.th` | `OP-01` | Operation User |
| `uthai@leschaco.co.th` | `OP-02` | Operation User |
| `titchanatorn.k@leschaco.co.th` | `SV-01` | Operation Supervisor, from `Auth:Roles` |
| `somchai.p@leschaco.co.th` | no owner id | Administrator, from `Auth:Roles` |
| `newhire@leschaco.co.th` | no owner id | **Viewer** |

The first row is the one that matters. Entra introduces an operator as
`watsana.k@…` while the plan workbooks call her "Watsana"; matching those two
spellings against each other is what the pre-migration version did, and it would
have taken every job away from every operator on the day sign-in was switched on.
The directory matches the local part, then the stem before its first dot, then
the display name's first word.

The fourth row is worth noticing too: an administrator who is not an operator has
every capability and no owner id, so no job is "theirs" and the workspace says
the directory does not know them — while they can still edit anything. The banner
says exactly that rather than the reverse.

### 5. Deploy

Push to `main` and both workflows build, test and release to production,
applying EF migrations before the API goes out.

**Try it on a staging slot first.** Both workflows also run on demand against any
branch, so a change can be seen running on Azure without releasing it:

```bash
RESOURCE_GROUP=rg-scmos WEB_APP=scmos-web API_APP=scmos-api STAGING_SQL_CONNECTION='Server=...;Database=scmos-staging;...' ./infra/setup-slot.sh
```

Then **Actions → API → Run workflow → slot: staging**, and the same for Web. Each
smoke-tests the slot it deployed to and fails if it does not answer.

Two things about slots that bite, both closed off by the script and the
workflows:

- **App settings swap with the slot** unless marked sticky, so a staging slot
  inherits production's connection string by default and writes to the live
  register. Everything the script sets is slot-specific.
- **A staging deploy must not migrate the production database.** The workflow
  reads `SCMOS_SQL_CONNECTION_STAGING` for a staging slot and *fails* if it is
  missing rather than falling back — a fallback is exactly how unreleased schema
  reaches live data.

Auto-swap is left off. A slot that promotes itself is not a test.

Repository secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID`, `SCMOS_SQL_CONNECTION`, and
`SCMOS_SQL_CONNECTION_STAGING` if you use a slot.
Repository variables: `API_APP_NAME`, `WEB_APP_NAME`.

`AZURE_CLIENT_ID` is a federated-credential app registration with **Contributor**
on the resource group; the workflows use OIDC, so no client secret is stored.

### 6. Load the data

The schema arrives empty. Three commands, run once, in this order — locally
against the Azure SQL connection string, or from a container with it set:

```bash
dotnet run --project server/Scmos.Api -- --seed public/data/ops.json
dotnet run --project server/Scmos.Api -- --migrate-status
dotnet run --project server/Scmos.Api -- --seed-suppliers migration/data/rates.json
```

The first keys the operation plan (2,102 jobs) and is idempotent on the job key.
The second moves free-text statuses onto the controlled vocabulary — run
`--migrate-status --dry-run` first to see what it would change. The third builds
the supplier register from the jobs and the rate cards (29 suppliers, 2,270
lanes, 82,290 prices) and registers the 21 AI tools.

`rates.json` is git-ignored and has to be rebuilt from the workbook folder first:

```bash
node migration/build-rates.mjs "D:/Leschaco/Dashboard/Transport cost subcon"
```

### 7. Check it came up

```bash
curl https://<api-app>.azurewebsites.net/health
```

Then sign in to the web app and confirm, in this order:

1. The header shows your name and role — if the role is **Viewer** and you expected
   more, `Auth__Roles__…` is missing or misspelled.
2. The workspace lists jobs and **MY JOBS** is not zero — if it is, the staff
   directory does not recognise your email; the screen says so.
3. A cell in your own row accepts an edit — if the grid is read-only and a red
   banner says permissions could not be loaded, the web app cannot reach the API
   (`SCMOS_API_BASE_URL`) or the proxy key does not match.
4. Upload a file on CAR/PAR — a 503 means `Storage__ServiceUri` or the Blob role
   assignment is missing, and the message says which.

### 8. Long-term storage

Already applied by `infra/setup-storage.sh` in step 1: Hot for a year, Cool to
three, Archive to ten, and **no delete action**. See the retention section below
for why that last part is a refusal rather than an omission.

## Data

> ⚠️ `public/data/ops.json` is served as a **static public asset** and holds real
> customer names, driver names and driver phone numbers. Any deployment that is
> not behind Web App Login exposes it to anyone who knows the URL. It is only a
> seed for an empty register — once Azure SQL holds the plan, replace it with
> anonymised data or remove it.
>
> The rate book **no longer sits on the public path**. Eighteen subcontractors'
> negotiated prices were reachable by anyone who guessed
> `/data/rates.json`; they now live in Azure SQL and are served by `/api/rates`
> to a signed-in caller. `migration/build-rates.mjs` writes to
> `migration/data/rates.json`, which is git-ignored and is only an input to
> `--seed-suppliers`.
>
> Neither file has ever been tracked, and this repository has a remote — git
> history is effectively permanent, so both stay out of it.

The published copy is missing its `delivery`, `rates` and `masters` sections
because the source export was truncated, so the DELIVERY category is empty and
master lists are derived from the jobs themselves. Dropping the complete file in
restores both with no code change — see [app/scmos/ops.ts](app/scmos/ops.ts).

## Commands

| Command | What it does |
| --- | --- |
| `npm run dev` | Web app on :3000 |
| `npm run build` | Production build into `.next/standalone` |
| `npm run lint` / `npm run typecheck` | ESLint / TypeScript |
| `dotnet build server` | Build the API |
| `dotnet run --project server/Scmos.Api` | Run the API |
| `dotnet run --project server/Scmos.Api -- --migrate` | Apply EF migrations |
| `dotnet run --project server/Scmos.Api -- --seed <file>` | Load a register export |
| `dotnet run --project server/Scmos.Api -- --migrate-status [--dry-run]` | Move free-text statuses onto the controlled vocabulary |
| `dotnet run --project server/Scmos.Api -- --seed-suppliers [rates.json]` | Build the supplier register from the jobs and rate cards, load the rate tables, register the AI tools |
| `dotnet ef migrations add <Name> --project server/Scmos.Api --output-dir Data/Migrations` | New migration after editing the model |
| `node migration/export-d1.mjs` | Export the register out of the old D1 database |
| `node migration/build-rates.mjs "<folder>"` | Rebuild `migration/data/rates.json` from the subcontractor rate workbooks |

## Layout

- `app/scmos/` — the SCMOS UI and the business rules: data standard, cleanup,
  duplicates, Excel, screens, overlays
- `app/api/[...path]/route.ts` — the proxy to the API; the only server code left
  in the web app besides identity
- `app/auth.ts`, `app/easy-auth.ts` — reading the Web App Login principal
- `server/Scmos.Api/Endpoints/` — jobs, uploads, operations, AI extract, identity,
  suppliers, rates, incidents, the AI gateway
- `server/Scmos.Api/Rules/` — the business rules: formats, job rules, the status
  vocabulary, the workflow state machine, carrier assignment, pre-run SLA, delay
  reasons, KPI measures, AI permissions
- `server/Scmos.Api/Data/` — EF model, migrations, the register repository, seeders
- `server/Scmos.Api/Auth/` — the staff directory and who is trusted
- `migration/` — moving the register off D1 (git-ignored outputs)

## Transportation rates

Transportation Rates reads the subcontractors' real quotations. Twenty-one
carriers quote on the LESCHACO form — a lane down the page and, across it, a
price per vehicle type repeated once for each diesel price band, because the
contract's fuel clause moves the rate about 3% at every band.

`node migration/build-rates.mjs "D:/…/Transport cost subcon"` reads that folder
and writes `migration/data/rates.json`; `--seed-suppliers` then loads it into
Azure SQL — **24 fuel bands, 2,270 lanes, 82,290 prices**. The parsing rules are
in [app/scmos/rates.ts](app/scmos/rates.ts) with the other business rules, and
the script imports them, so the browser and the build cannot disagree about how
a quotation is read.

The screen reads `/api/rates`, not a file. That matters beyond confidentiality:
the backend can now see what a carrier charges, which is the ordering the
carrier-priority rule actually asks for. `/api/rates/quotes?customer=…&vehicle=…
&diesel=…` answers "who quoted this journey, cheapest first" and is what the Rate
Quotation screen shows.

The screen prices every lane at one diesel price rather than showing the bands
as columns. There are twenty-four distinct bands across the carriers — the form
steps at 36.30 and DGT's per-customer contracts step at 36.00 — so a band-column
table would be honest and unreadable. The band that produced each figure is
named beside it.

Two things the forms do not agree on, handled rather than assumed:

- **Column meaning.** SHORE label their columns ORIGIN / CUSTOMER NAME /
  DESTINATION; TNB and SANGJA describe the same journeys with the customer and
  the pickup point in opposite columns. The comparison matches lanes on the end
  points as an unordered pair so the same lane still lines up.
- **Vocabulary.** 4WH and 4W, 20F and 20F', "40F DG/ 40HC DG" and 40F DG are the
  same thing and are normalised. DGT's "DRY 20 ' / 40'" covers both sizes at one
  price and stays as one entry rather than being split into two guesses.

What could not be read is shown on the screen and travels in the Excel export,
so nobody negotiates from a sheet believing it is complete.

## Truck booking

The booking queue is the plan itself. There is no separate booking record to
fall out of step with it: [app/scmos/booking.ts](app/scmos/booking.ts) reads each
job for what it is still missing, so a plate keyed on the workspace grid takes
the job out of the queue without anything being told.

Five stages, measured off the July register:

| Stage | Jobs |
| --- | --- |
| No carrier | 28 |
| No plate | 1,178 |
| No driver | 100 |
| Ready | 785 |
| Completed | 11 |

Selecting a job lists the carriers that quoted that lane and what each charges at
the current diesel price, cheapest first, with the quoted lane shown beside the
figure so a suggestion can be judged rather than trusted. Nothing is chosen
automatically. Carriers with a rate card but no lane for the journey are still
listed, unpriced — they are approved and can be asked, and hiding them would make
the list look like the whole market.

Choosing a carrier or keying a plate writes to the job through the same path as a
workspace edit: normalised, flagged, written to the job's own history, saved.
Naming a carrier moves a job off "Waiting Truck" to "Truck Confirmed"; the later
statuses stay the operators' to set.

The plan's container wording is mapped onto the rate cards' vocabulary in
`vehicleForType` — the workbooks write the same truck as `1X6WH'`, `1X6W`,
`1x6 WH` and `6 WHEEL`, and none of them can be priced until they all read as
`6W`. That covers 98% of the typed jobs; `COMBINE` and `1X45'` are left unmapped
rather than guessed.

## Supplier register

One row per company, with every spelling anyone has typed pointing at it. That
reconciliation is what makes the rest possible: a supplier's jobs, rates,
incidents and score can only be gathered together once somebody has said that
two spellings mean one firm.

`--seed-suppliers` builds it from the two places carriers are named — the 2,102
jobs and the rate workbooks — and produces **29 suppliers from 34 spellings**.
Only matches beyond doubt are merged: the same letters with punctuation and
spacing removed, so `TO = TO. = T.O. = T.O`, `ACN = A.C.N`, `W.A.K = WAK`. TTP is
*not* merged into TATIYAPON by this. An abbreviation could be another company,
and paying the wrong subcontractor is worse than having two rows; those pairs are
listed at the end of the seed for a person to decide, and the Supplier screen is
where they say so.

- **Supplier** — the register, with jobs, priced lanes, aliases and status.
- **Add New Vendor** — registration and the onboarding statuses. A new vendor
  starts as a draft; only an approved supplier can be given work, and moving the
  status is a supervisor's act.
- **Annual Evaluation** — on-time, confirmation and delay come from the KPI
  engine so the meeting argues with measured figures; safety and documents are
  the assessor's own. A carrier below the minimum sample gets no operational
  component rather than a flattering hundred.

## File storage

One root, three trees, and no caller composes a path:

```
SCMOS/
├── 2026/                                   ← the year the job ran
│   ├── LOTUS/                              ← customer
│   │   └── ABS260800001/                   ← job code, container, or key
│   │       ├── Booking/  ECard/  POD/
│   │       ├── Images/   Invoice/  CARPAR/
│   └── TANATEX/
├── Supplier/
│   └── DGT/                                ← supplier code, not name
│       ├── Audit/  Insurance/  License/
│       ├── Training/  Contract/
└── Report/2026/2026-07/                    ← monthly uploads
```

The shape is the point. A flat container is unusable within a year: nobody can
find one job's paperwork, a lifecycle policy cannot be written against a prefix,
and access cannot be narrowed to a customer or a supplier later. The path carries
the year, the customer, the job and the kind of document, so all four are
answerable from the key alone — which matters on the day the database and the
storage account disagree about something.

**SQL holds metadata and the blob URL. The bytes never come near it.** One
`documents` table for every file — a job's paperwork, a supplier's insurance
certificate, a CAR/PAR photo — because three tables would mean three copies of
"where does a file go".

[`Rules/BlobPaths.cs`](server/Scmos.Api/Rules/BlobPaths.cs) is the only thing that
builds a key. A caller says what the file is attached to and what kind it is; the
path follows from the register — the job's own work date gives the year, its
customer the folder, its job code the reference. Uploading is
`POST /api/documents` with a `jobKey`, a `supplierId` or a `caseId`, and exactly
one of them.

Four decisions worth knowing:

- **The folder list is controlled**, like the status vocabulary and for the same
  reason: "POD", "pod" and "Proof of delivery" as three sibling folders is the
  mess this prevents. Unrecognised kinds land in `Other` rather than inventing a
  folder nobody looks in again. Written spellings already in use — `e-card`,
  `driver-statement`, `safety-training` — map onto the agreed folders.
- **The year comes from the job, not the clock.** A July job filed in August is
  still under the year it ran.
- **Suppliers are keyed on the code, not the name.** TATIYAPOL and TATIYAPON are
  deliberately separate companies until somebody says otherwise, and a name that
  normalises the same way would quietly merge two firms' insurance.
- **An upload never overwrites.** The stored file name is
  `{timestamp}-{shortid}-{cleaned name}`, so two people uploading `POD.pdf` in
  the same second both keep their file, and a folder listing sorts by when things
  happened. The original name — Thai and all — is kept in the row.

Reading goes through `GET /api/documents/{id}/content`, not the blob URL: the
container is private and stays that way, because a URL that works without a
sign-in is a URL that ends up forwarded.

### Ten-year retention

| Age | Tier |
| --- | --- |
| 0–1 year | Hot |
| 1–3 years | Cool |
| 3–10 years | Archive |
| 10 years | **review — a person decides** |

Tiering is a storage account lifecycle policy
([`infra/storage-lifecycle.json`](infra/storage-lifecycle.json)), so changing the
rule is a policy change rather than a deployment. One script does the whole
storage side of a cutover — account, private container, managed identity, role
assignment, lifecycle and app settings — and every step is idempotent:

```bash
RESOURCE_GROUP=rg-scmos STORAGE_ACCOUNT=scmosfiles API_APP=scmos-api ./infra/setup-storage.sh
```

**There is no delete action in that policy and no code path in this system that
deletes a document.** That is a refusal, not an omission. A lifecycle rule that
deletes is one mistyped prefix away from destroying a customs file a dispute
three years later depends on, and blob deletion is not something an operator can
undo. `Rules/Retention.cs` has no state called "delete" either — a state a
program can produce is a state some later code will act on.

Retention end raises a review (`GET /api/documents/retention`). Somebody with
`ApproveRetention` — Manager and above, not Supervisor — decides, in writing,
with a reason, and that decision lands in the audit trail. Carrying it out is
then a separate, deliberate act against the storage account, so that "somebody
approved this" and "it is gone" are never the same event.

To exercise uploads locally, run the Azure Storage emulator and point the API at
it — without it, upload answers 503 and says why, which is the honest default:

```bash
npx azurite --silent --location .azurite --blobPort 10000
```

Then set `Storage:ConnectionString` to `UseDevelopmentStorage=true` in
`server/Scmos.Api/appsettings.Local.json` (git-ignored).

## Editing in the workspace

Every stored field on a job you own is editable in the grid. Click a cell, type,
and move with the keyboard:

| Key | Does |
| --- | --- |
| `←` `→` | Previous / next column — **only from the edge of the text**, so mid-word they still move the caret |
| `↑` `↓` | Same column, previous / next row on this page |
| `Tab` / `Shift+Tab` | Previous / next column, from anywhere in the text |
| `Enter` / `Shift+Enter` | Save and move down / up |
| `Esc` | Leave without saving |

The edge rule is the part worth keeping. Hijacking `←` and `→` outright would
break correcting one digit of a container number, which is most of what this
grid is for; moving only from the ends gives spreadsheet navigation without
taking the caret away.

Navigation stops at the edges of the page rather than wrapping. Wrapping from the
last column of one job to the first of the next is how somebody types a container
number into the wrong row, and paging to a row nobody can see is a cursor typing
in the dark.

The column order the keyboard walks is captured as the rows are built, so it is
the order on screen by construction rather than a second list to keep in step.

Two things are deliberately not editable:

- **Priority and the MY JOB flag** are worked out from the job — `flagJob` sets
  one, and `store.ts` drops both before saving. Offering them would let somebody
  change a value that reverts on the next reload.
- **Delivery cost** is priced from the rate card. A hand-keyed cost that
  disagrees with the card is a number nobody can explain later.

Category and status are dropdowns because their values are a controlled set.
Reassigning a job is a separate, permission-gated action, not an inline edit.

> Delivery's grid was read-only in all but two columns, which made it a report
> rather than a place to work. It has fourteen editable columns now.

## KPI

Eight measures, computed in .NET from the register, each carrying the base it was
measured over. Three things make the screen answerable rather than decorative:

- **A target.** On-time delivery and pickup are held to 95%, confirmation SLA to
  90% — the figures the operation already works to. The other five have no
  target and say so; an invented one is worse than none, because the first thing
  a target does is tell people which numbers to argue about.
- **A trend.** Each measure carries the preceding six months, built by running
  the same engine once per month so a trend point and the headline figure can
  never be computed two different ways. 55% is a crisis if it was 80% last month
  and a recovery if it was 40%. The register currently holds one month, so the
  screen says "มีข้อมูลเดือนเดียว" rather than drawing nothing.
- **A way through.** Every carrier row and the measures with a matching slice
  open the workspace on the jobs behind the number. A rate you cannot open is a
  rate you cannot act on.

The scorecard shows each component with its own base — a carrier scored on forty
measured jobs and one scored on five are not the same claim.

> **Two bugs this turned up, both of the same kind.**
>
> Delay-free was computed from `delay_records`, a table nothing writes to yet, so
> every carrier scored exactly 100% and collected a perfect fifth of their score
> for it. The component meant to separate carriers gave the best and the
> least-known the same mark. It now reads the register when the records are
> empty, and returns **null** rather than 100 when neither can say.
>
> Then the delay count came out at 2 while the workspace's DELAY tab showed 64.
> The tab counts a held status **or** a recorded reason — a job delayed on
> Tuesday and delivered on Wednesday is no longer held, and the reason an
> operator typed is its only trace. The engine counted status alone. One word,
> two definitions, thirty-fold apart, on two screens of the same app.
> `JobRules.WasDelayed` is now the single definition and the counts agree.

## Incident and CAR/PAR

One register in the database — a case carries its kind — so the Incident and
CAR/PAR menu names open the same cases rather than two half-registers.

Seven stages: open → analysis → action → follow-up → monitoring → approval →
closed. The service refuses to skip the things that make a case worth having: no
corrective action without a root cause, no follow-up without an owner and a due
date, no approval without a recorded effectiveness result. The last step is a
person's signature and only a supervisor may give it — the AI may draft the whole
case and may not close one.

## AI permissions

Three levels, and the difference between them is structural rather than textual.
Everything the assistant does passes through `AiGateway`, which is deliberately
the only way in:

| Level | What happens | Tools |
| --- | --- | --- |
| Allow | The tool runs | 16 — reading anything, drafting anything |
| Approval | Returns the exact payload it would have applied, parks it for a person | 5 — change a record, change a rate, send an email, close a CAR/PAR, assign a supplier |
| Deny | The tool is not in the catalogue at all | deletion, in every form |

Deny is the important one. A model cannot be instructed out of a capability it
does not have, and it can always be talked out of an instruction. There is no
delete tool and no code path that would call one; any name beginning `delete` or
`drop` is refused at the gate before dispatch. "AI must never delete records" is
therefore a property of the system, not a sentence in a prompt somebody may later
edit. Approving is a supervisor's act — an Operation User approving the
assistant's edit to their own job would just be the assistant editing it — and
approval and application are two separate steps, so nothing is written by the act
of reading the queue.

The AI Assistant screen reads the matrix from `/api/ai/tools`, the same source
that enforces it, so the version people read cannot drift from the version in
force.

**"วันนี้มีงานอะไรเสี่ยงบ้าง"** is answered by `/api/risk`, grouped by customer
with the reason on every group and the shipments listed underneath:

```
84 งานเสี่ยง จาก 12 ลูกค้า
  (ไม่ระบุลูกค้า)  28  ยังไม่มีผู้ขนส่งยืนยัน
  ALLNEX           14  เลขตู้ไม่ตรงมาตรฐาน จะไม่ตรงกับ E-Card
```

It is computed from the register's own rules, and the screen says so in as many
words. That makes the answer explainable — every shipment carries why it was
listed — and reproducible, which a model's answer to the same question would not
be. An assistant people believe read their day, when it did not, is one they will
eventually trust with something it never looked at.

The E-Card check is named for what it actually tests: a container number that
fails its own format will not match whatever the card says at the gate. A real
card-to-booking comparison needs the cards, which are not in the system yet.

## Known gaps

- **Every menu entry renders something real.** `NOT_BUILT` is empty. Two screens
  still read generated data — **Billing** (there is no invoice table; the KPI it
  reports on cannot be measured yet) and **Reports** (a catalogue of exports, of
  which only the KPI workbook exists).
- **No agents yet.** The permission layer, the tool catalogue and the approvals
  queue are real and enforced; the six agents that would call them are not built,
  so an Allow tool reports plainly that it is not wired to a real action rather
  than pretending to have done something. The risk answer is rules, not a model,
  and says so.
- **Two of the twelve alerts have never fired.** Audit expiry needs an audit
  report uploaded with a date; POD-missing needs a delivered job with no POD.
  Both report what they are waiting for rather than a reassuring zero. Capacity
  shortage and document-unclear now have screens that produce the data they need.
- **Document verification says every job is short of paperwork**, because
  nothing has been uploaded yet. That is accurate and will stay useless-looking
  until people start attaching files; it is not a bug to be tuned away.
- **The container check is not a real E-Card comparison.** It tests the number
  against its own format, which is what will actually fail at the gate. Comparing
  a card to a booking needs the cards.
- **Bulk saves record one audit row, not thousands.** Above fifty jobs a save is
  an import, and it is recorded as the one action it was.
- **Three tables are carried over from the previous system** — `report_uploads`,
  `operation_uploads`, `operation_entries` — with their endpoints. Nothing calls
  them and they are empty everywhere checked. They stay until somebody confirms
  the production database is empty too; the query to run is in
  [`Data/Entities.cs`](server/Scmos.Api/Data/Entities.cs).
- **Nothing has run against real Azure yet.** Everything here is verified on SQL
  Server LocalDB and the storage emulator. `infra/setup-storage.sh` does the
  storage half of the cutover; the database and App Service halves still need
  somebody with the subscription.
- **Rates are not joined to jobs.** Quotation prices a journey when you ask it to,
  but nothing costs the 2,102 jobs in the register as a whole.
- **The July plan is in the past.** Every loading date is July 2026, so the
  booking queue has no live urgency to sort by and does not pretend to; it orders
  by loading date and shows the date.
- **Fifteen suppliers carry work but have no rate card** — including PPK (127
  jobs), SJ (102), JTC (55) and T.O. (41). The Supplier screen counts them under
  "มีงานแต่ไม่มีราคา", because a carrier whose work cannot be costed is a finding,
  not a blank cell. The form's own remarks quote JTC's fuel adjustment, so a card
  exists somewhere.
- **Two rate carriers have no supplier row** — TNB and NHP have quoted but never
  carried. Quotation flags them rather than hiding them.
- **Evidence is not uploaded yet.** `incident_evidence` and the endpoint exist;
  the Blob upload from the CAR/PAR screen does not.
- **Delivery is empty.** The grid and its KPI exist and are ready.
- **Duplicates within one file are not detected.** The importer compares against
  the register, so a workbook that repeats a row inside itself brings both.
- **`xlsx` carries published advisories** with no fix on the npm registry. It
  only ever parses files a signed-in operator chose, but it is worth replacing.
