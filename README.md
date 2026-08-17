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

The data standard, the duplicate matching, the cleanup pass and both Excel
directions stay in TypeScript, in the browser, in `app/scmos/`. They were not
ported to C#, on purpose: they are the asset, they are already tested against the
real July plan, and rewriting three thousand lines of validation into another
language would have risked the one thing this system is trusted for.

The API owns what the browser must not: the register, the blobs, the OpenAI key,
and who is allowed to write.

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

> ⚠️ `public/data/ops.json` and `public/data/rates.json` are served as **static
> public assets**. The first holds real customer names, driver names and driver
> phone numbers; the second holds eighteen subcontractors' negotiated prices,
> which is commercially confidential and would be visible to any of them. Any
> deployment that is not behind Web App Login exposes both to anyone who knows
> the URL.
>
> `ops.json` is only a seed for an empty register — once Azure SQL holds the
> plan, replace it with anonymised data or remove it. `rates.json` should move
> behind the API for the same reason, so it is served to a signed-in caller
> rather than sitting on the public path.

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
| `dotnet ef migrations add <Name> --project server/Scmos.Api --output-dir Data/Migrations` | New migration after editing the model |
| `node migration/export-d1.mjs` | Export the register out of the old D1 database |
| `node migration/build-rates.mjs "<folder>"` | Rebuild `public/data/rates.json` from the subcontractor rate workbooks |

## Layout

- `app/scmos/` — the SCMOS UI and the business rules: data standard, cleanup,
  duplicates, Excel, screens, overlays
- `app/api/[...path]/route.ts` — the proxy to the API; the only server code left
  in the web app besides identity
- `app/auth.ts`, `app/easy-auth.ts` — reading the Web App Login principal
- `server/Scmos.Api/Endpoints/` — jobs, uploads, operations, AI extract, identity
- `server/Scmos.Api/Data/` — EF model, migrations, the register repository, seeder
- `server/Scmos.Api/Auth/` — the staff directory and who is trusted
- `migration/` — moving the register off D1 (git-ignored outputs)

## Transportation rates

Transportation Rates reads the subcontractors' real quotations. Twenty-one
carriers quote on the LESCHACO form — a lane down the page and, across it, a
price per vehicle type repeated once for each diesel price band, because the
contract's fuel clause moves the rate about 3% at every band.

`node migration/build-rates.mjs "D:/…/Transport cost subcon"` reads that folder
and writes `public/data/rates.json`. The parsing rules are in
[app/scmos/rates.ts](app/scmos/rates.ts) with the other business rules, and the
script imports them, so the browser and the build cannot disagree about how a
quotation is read.

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

## Known gaps

Carried over from before the move, and unchanged by it:

- **Nine screens still run on generated data** — Subcontractors, Capacity,
  Billing, Safety, CAR/PAR, Performance, Documents, Reports, Master Data,
  Administration. The dashboard badges its three demo panels.
- **Rates are not joined to jobs.** Booking prices a lane when you ask it to, but
  nothing costs the 2,102 jobs in the register as a whole. Doing that needs the
  carrier spellings reconciled first — see below.
- **The July plan is in the past.** Every loading date is July 2026, so the
  booking queue has no live urgency to sort by and does not pretend to; it orders
  by loading date and shows the date.
- **Four carriers have jobs but no rate card** — PPK (127 jobs), SJ (102),
  JTC (55) and T.O. (26): 310 jobs, 15% of July, that cannot be costed. The
  form's own remarks quote JTC's fuel adjustment, so a card exists somewhere.
- **Carrier spellings do not reconcile.** The register writes TATIYAPOL, TTP and
  TATIYAPON; T.O., TO and TO.; W.A.K and WAK. Only spellings beyond doubt are
  aliased in [app/scmos/rates.ts](app/scmos/rates.ts); the rest need a person,
  because paying the wrong subcontractor's rate is worse than having no rate.
- **SBT quote on their own form** and are not read — 4 jobs.
- **Delivery is empty.** The grid and its KPI exist and are ready.
- **Duplicates within one file are not detected.** The importer compares against
  the register, so a workbook that repeats a row inside itself brings both.
- **`xlsx` carries published advisories** with no fix on the npm registry. It
  only ever parses files a signed-in operator chose, but it is worth replacing.
