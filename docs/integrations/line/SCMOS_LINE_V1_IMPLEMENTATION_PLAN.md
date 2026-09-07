# SCMOS LINE Integration V1 — implementation plan

Written 2026-09-07, after reading the repository. This is the discovery
deliverable the specification asks for before any code is written.

**Read section 2 first.** The specification assumes an architecture SCMOS does
not have, and four of its instructions cannot be followed as written. Nothing
below is a refusal — each has a replacement that meets the same requirement in
the architecture that exists.

---

## 1. What SCMOS actually is

| | |
|---|---|
| Front end | Next.js **16.3.1**, App Router, React 19.2.6, TypeScript |
| Back end | **.NET 10 minimal APIs**, `server/Scmos.Api` |
| Data access | **EF Core 10**, `Microsoft.EntityFrameworkCore.SqlServer` |
| Database | **Azure SQL** |
| Identity | **Microsoft Entra ID** via App Service Easy Auth (`X-MS-CLIENT-PRINCIPAL`) |
| Hosting | Azure App Service — one web app, one API app |
| Repo shape | **Not a monorepo.** One Next.js app, one .NET project, one `tests/` folder |
| Tests | `node --test` over `tests/*.test.mjs`, plus C# check-harness flags |
| Commands | `npm run lint`, `npm run typecheck`, `npm test`, `npm run build` |

### The Next.js app is a proxy, not a back end

`app/api/[...path]/route.ts` forwards to the .NET API. There is no ORM, no
database client and no business logic on the Next.js side. **Anything that
touches data belongs in `server/Scmos.Api`.**

This is the single most important finding for this integration.

### Existing concepts the spec asks about

| Spec's term | What SCMOS calls it | Where |
|---|---|---|
| transport job / shipment | `OperationJob` | `server/Scmos.Api/Data/Entities.cs` |
| job number | `JobCode`, and `Key` as the identity | same |
| vendor / subcontractor | `Supplier`, with `SupplierAlias` for spellings | `Data/Entities.cs` |
| customer | a string on the job, not an entity | — |
| status | `Status`, per-category ladder | `app/scmos/theme.ts` `STATUS_LADDER` |
| delay | `DelayRecord`, and `DelayCategory` | `Data/`, `Rules/DelayReasons.cs` |
| audit history | `AuditEvent` (who / what / when / before / after) | `Data/AuditEntities.cs` |
| OTD | `JobRules.IsOnTime`, `IsMeasurable` | `Rules/JobRules.cs` |
| KPI | `KpiEngine`, `MonthlyReport` | `Services/` |
| RBAC | `[Flags] enum Capability` | `Rules/Roles.cs` |
| background worker | `ReportScheduler : BackgroundService` | `Services/ReportScheduler.cs` |

---

## 2. Where the specification and the repository disagree

### 2.1 The job is a JSON blob. This is the big one.

`OperationJob` promotes only eight fields to real columns — `Key`, `Cat`,
`Owner`, `OwnerId`, `WorkDate`, `Customer`, `Trucker`, `JobCode`, `Container`,
`Status`. **Everything else lives in a single `Data` nvarchar column** holding
the whole job as the workspace serialises it.

Consequences:

- **Good:** the spec's new job fields — `eta_delivery`, `actual_arrival_at`,
  `delay_flag`, `delay_minutes`, `truck_plate` — need **no migration**. They are
  already expressible. This is how the return-load and diesel columns were added
  in the last few days.
- **Bad:** none of them is queryable. `WHERE delay_flag = 1` is not available.
  The Live Operations and OTD endpoints the spec asks for cannot be served by a
  SQL query over those fields.

**Plan:** promote exactly the columns the dashboards filter and sort on, and
leave the rest in the blob. Candidates, in order of value: `EtaDelivery`,
`ActualArrivalAt`, `DelayFlag`, `DelayMinutes`, `DelayReasonCode`,
`StatusSource`, `StatusUpdatedAt`. That is one additive migration with
nullable columns and no data loss, and it is the only schema change V1 needs on
the job itself.

Do **not** promote all forty. The blob is deliberate and it has been working.

### 2.2 There is no queue in this project

No `Azure.Messaging.ServiceBus`, no `Azure.Storage.Queues`, no Function App.
The only Azure SDKs present are `Azure.Storage.Blobs`, `Azure.Identity` and
`Azure.Extensions.AspNetCore.Configuration.Secrets`.

There **is** a working background-worker pattern: `ReportScheduler`, an
`IHostedService` that ticks hourly inside the API process and takes its own DI
scope.

**Plan for V1:** skip the queue. The webhook writes the raw event to Azure SQL
with `ProcessingStatus = RECEIVED` and returns; a `BackgroundService` polls for
`RECEIVED` rows and processes them. This meets every requirement the spec gives
for the queue — fast webhook, asynchronous processing, retry, idempotency —
without standing up a Function App and a Storage Queue that nothing else in
SCMOS uses.

The processing loop must be written so the store can be swapped for a real
queue later: one interface, `ILineEventSource`, with a SQL implementation now.

*Caveat to state plainly:* a single API instance polling a table is not the same
as a queue. If App Service scales out, two instances will race. Guard with a
`UPDATE … SET ProcessingStatus='PROCESSING' WHERE Id=@id AND
ProcessingStatus='RECEIVED'` and process only when one row was affected.

### 2.3 The status vocabulary does not match

The spec lists 14 desired statuses (`ON_THE_WAY_PICKUP`, `ARRIVED_DELIVERY`, …).
SCMOS has its own ladder, per category, in `STATUS_LADDER`:

```
DRAFT · RECEIVED · VALIDATING · WAITING_CS · READY_FOR_BOOKING ·
WAITING_SUPPLIER · SUPPLIER_CONFIRMED · TRUCK_ASSIGNED · PRE_RUN · READY ·
DISPATCHED · PICKED_UP · IN_TRANSIT · DELIVERED ·
DOCUMENT_PENDING · BILLING_PENDING · COMPLETED · CANCELLED · HOLD
```

The spec itself says to preserve an established model. **Plan:** LINE maps onto
the existing ladder and adds nothing:

| LINE says | SCMOS status |
|---|---|
| กำลังไปรับ / on the way pickup | `DISPATCHED` |
| ถึงต้นทาง / arrived pickup | `DISPATCHED` |
| โหลดเสร็จ / ออกจากท่า / departed | `PICKED_UP` |
| กำลังไปลูกค้า / on the way | `IN_TRANSIT` |
| ถึงลูกค้า / arrived customer | `DELIVERED` |
| เสร็จแล้ว / completed | `DELIVERED` — **not** `COMPLETED` |

That last row matters. `COMPLETED` in SCMOS is after documents and billing, not
after the truck leaves. A vendor typing "เสร็จแล้ว" means the delivery, and
letting LINE write `COMPLETED` would close jobs nobody has billed.

The spec's own rule — delay is a *condition*, not a status — already matches how
SCMOS works, since `DelayRecord` is separate from `Status`.

### 2.4 No new permission system

The spec proposes `line.view`, `line.manage`, `transport.status.update`. SCMOS
has `[Flags] enum Capability` in `Rules/Roles.cs`, bits 0–16 used, **next free
bit is 17**.

**Plan:** two new bits, not six.

- `ViewLineActivity = 1 << 17` — read the activity and review screens
- `ReviewLineMessages = 1 << 18` — approve, correct or reject a queued message

Updating a job from LINE reuses `EditAnyJob`, because that is what it is. The
LINE user is not the actor for capability purposes — the *system* is, and the
audit records which LINE user caused it.

There is a check-harness flag pinning the bits (`--check-capability`); add these
to it or CI will not notice a collision.

---

## 3. What is genuinely new

Four tables. Names follow the EF Core convention already in `Data/`.

### `LineGroup`
`Id, LineGroupId (unique), GroupName, SupplierId, GroupType, IsActive, CreatedAt, UpdatedAt`

`SupplierId` joins to the existing `Supplier`. `SupplierAlias` already solves
"DGT" versus "DGT Cross Haul Co., Ltd.", so the mapping is group → supplier id,
never group → supplier name.

### `LineUser`
`Id, LineUserId (unique), DisplayName, SupplierId, StaffId, Role, IsActive, …`

`StaffId` joins to the existing `StaffMember`.

### `LineEvent` — the raw evidence
`Id, WebhookEventId, LineMessageId, LineGroupId, LineUserId, MessageType,
RawText, RawPayload, ReceivedAt, ProcessingStatus, ProcessedAt, ErrorCode,
ErrorMessage, RetryCount, JobKey, ParserConfidence, ParsedResult`

Unique index on `LineMessageId` — this is the idempotency key. Index on
`(ProcessingStatus, ReceivedAt)` for the worker's poll.

### `JobStatusHistory`
`Id, JobKey, FromStatus, ToStatus, EventTime, ReceivedAt, Source, LineEventId,
ChangedByUserId, ChangedByLineUserId, DelayMinutes, DelayReasonCode, Remark`

**Check first whether `WorkflowEvent` already covers this.** It exists and was
not read closely during this pass. If it does, extend it with `Source` and
`LineEventId` rather than adding a second history table — this repository has a
documented habit of two tables that disagree.

`DelayReasonCode` reuses the existing `DelayCategory`, not a new master.

---

## 4. Files

**New — C#**

```
server/Scmos.Api/Rules/LineParser.cs          pure: normalise, job no, status, time, delay
server/Scmos.Api/Rules/LineAuthority.cs       pure: may this group update this job
server/Scmos.Api/Data/LineEntities.cs         the four tables
server/Scmos.Api/Data/LineParserCheck.cs      --check-line, wired in Program.cs
server/Scmos.Api/Services/LineSignature.cs    HMAC-SHA256 over the raw body
server/Scmos.Api/Services/LineEventWorker.cs  BackgroundService, polls RECEIVED
server/Scmos.Api/Services/LineReplyService.cs the confirmation back to the group
server/Scmos.Api/Endpoints/LineEndpoints.cs   webhook + activity + review
```

**New — TypeScript**

```
app/scmos/lineActivity.ts                     leaf module, list shaping
app/scmos/screens/LineActivity.tsx            replaces the placeholder
tests/lineParser.test.mjs                     the spec's nine message cases
```

**Changed**

```
server/Scmos.Api/Data/ScmosDbContext.cs       four DbSets
server/Scmos.Api/Rules/Roles.cs               two capability bits
server/Scmos.Api/Program.cs                   MapLine(), worker, --check-line
app/scmos/externalSystems.ts                  LINE stops being a placeholder
app/scmos/nav.ts                              LINE gains its own tabs
```

The parser must be **pure and in `Rules/`**, with no EF and no HTTP, because
that is how everything else testable in this codebase is written and because
`--check-line` has to run it in CI without a database.

---

## 5. Signature verification — the one thing that must not be got wrong

1. Read the **raw** body before any JSON parsing. In ASP.NET this means
   `EnableBuffering()` and reading the stream, not the model-bound object.
2. `x-line-signature` header.
3. HMAC-SHA256 with the channel secret, base64.
4. **Fixed-time comparison** — `CryptographicOperations.FixedTimeEquals`.
5. Reject before touching the database.

Never log the secret, the token, or the raw body of a rejected request.

---

## 6. Order of work

1. Discovery and this document ✅
2. `LineParser.cs` + `tests`/`--check-line` — pure, no infrastructure
3. The four tables + migration
4. Signature verification + the webhook endpoint (persist and return)
5. `LineEventWorker` + the claim-one-row guard
6. Vendor and job authority
7. The job update through the existing domain path, in one transaction with the
   history row
8. `LineReplyService`
9. Activity and Review APIs
10. Activity and Review screens
11. Promote the seven job columns; then Live Operations and OTD
12. Documentation

**Not** starting with the dashboard, as the specification requires.

---

## 7. Rollback

Every step is additive. The four tables are new, the seven job columns are
nullable, and the capability bits are new. Rollback is: turn off the feature
flag, and the webhook returns 200 without persisting.

Feature flag: SCMOS has no flag framework, so an app setting —
`Line__Enabled` — read at startup, consistent with `__` mapping to `:` on App
Service.

---

## 8. What is blocked, and on whom

| Blocked on | Needed |
|---|---|
| **The account team** | Which LINE groups belong to which supplier. This is data somebody must own and keep current; the integration cannot infer it. |
| **The account team** | Twenty or thirty real messages from an existing vendor group, before the parser is written. Writing keyword rules against invented examples is how a parser passes its tests and fails on the first real day. |
| **You** (I cannot handle credentials) | LINE Official Account, channel secret, channel access token, and the webhook URL registered in the LINE Developers Console. |
| **You** | The app settings on the API — `Line__ChannelSecret`, `Line__ChannelAccessToken`. Changing app settings restarts the container. |

Nothing in sections 1–7 is blocked. The parser, the tables, the worker and the
screens can all be built and tested before a single credential exists.
