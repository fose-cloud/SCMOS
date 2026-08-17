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

### Roles

Everyone who passes the Entra policy reaches the app. What they may *edit* comes
from `Auth:Roles` in the API's configuration — email to role — defaulting to
`Operation User`, who can only edit jobs assigned to them. Supervisor and above
edit any job, reassign, and reach the Data tools.

### Job ownership

Ownership is decided on an **owner id** (`OP-01`…), never on a display name. The
plan workbooks call an operator "Watsana"; Entra introduces the same person as
`watsana.k@leschaco.co.th`. Matching those two spellings against each other is
what the previous version did, and switching real sign-in on would have taken
every job away from every operator at once. `opId` sits between them, is
backfilled from the operator name on the way in, and is written to the column and
the stored JSON together so the two cannot drift.

The directory lives in two places that must stay in step:
[app/scmos/nav.ts](app/scmos/nav.ts) for what the screen shows, and
[server/Scmos.Api/Auth/Directory.cs](server/Scmos.Api/Auth/Directory.cs) for what
is enforced.

## Deploying to Azure

### 1. Resources

An Azure SQL database, a storage account, two App Services (Node 22 and .NET 10)
on one plan, a Key Vault, and an Application Insights resource.

Turn on a system-assigned managed identity for both App Services, then grant it:

- **Key Vault Secrets User** on the vault (both)
- **Storage Blob Data Contributor** on the storage account (API)
- a contained user in Azure SQL for the API's identity, in `db_datareader`,
  `db_datawriter` and `db_ddladmin`

### 2. Secrets in Key Vault

| Secret | Used by |
| --- | --- |
| `ConnectionStrings--ScmosDb` | API — Azure SQL, authenticating as the managed identity |
| `OpenAI--ApiKey` | API — the document reader |
| `Auth--ProxyKey` | Both — the shared key on forwarded identity |

### 3. App settings

API App Service:

```
KeyVault__Uri                            = https://<vault>.vault.azure.net/
Auth__Mode                               = Proxy
Storage__ServiceUri                      = https://<account>.blob.core.windows.net
APPLICATIONINSIGHTS_CONNECTION_STRING    = <from the App Insights resource>
```

Web App Service:

```
SCMOS_API_BASE_URL     = https://<api-app>.azurewebsites.net
SCMOS_API_PROXY_KEY    = @Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/Auth--ProxyKey/)
NEXT_PUBLIC_SITE_URL   = https://<your domain>
```

Then turn on **Authentication** on the *web* App Service with Microsoft as the
provider, requiring authentication.

### 4. Deploy

Push to `main`. [.github/workflows](.github/workflows) builds each side and
deploys it, applying EF migrations before the API goes out. The workflows need
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and
`SCMOS_SQL_CONNECTION` as secrets, and `API_APP_NAME` / `WEB_APP_NAME` as
variables.

### 5. Long-term storage

Add a lifecycle management rule on the storage account: Cool after 30 days,
Archive after 180. It is a policy on the account rather than a tier set per
upload, so changing your mind does not need a deployment.

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

## Known gaps

- **Six screens still run on generated data** — Capacity, Billing, Documents,
  Reports, Master Data, Administration. The dashboard badges its three demo
  panels. Supplier, CAR/PAR, Add New Vendor, Annual Evaluation, Rate Quotation
  and AI Assistant now read the API.
- **No agents yet.** The permission layer, the tool catalogue and the approvals
  queue are real and enforced; the six agents that would call them are not built,
  so an Allow tool reports plainly that it is not wired to a real action rather
  than pretending to have done something.
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
