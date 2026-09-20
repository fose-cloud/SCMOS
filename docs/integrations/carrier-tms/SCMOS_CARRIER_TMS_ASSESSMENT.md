# SCMOS Carrier TMS Integration V1 — Discovery and Assessment

Phase 0 (discovery only) · 20 September 2026 (Asia/Bangkok) · repository baseline v2.7.51 on `azure-dotnet-migration` · production live on Azure since 16 August 2026.

**Nothing in this phase changes code, schema, configuration or data.** It reads the repository as it is and says what a carrier-facing API would keep, reuse, extend, add, and must not touch. Every claim below names the file it comes from; where the live Azure state matters it is what `az` and the workflows showed on 14–20 September, not what the README's end-state guide describes.

Companion documents: [SCMOS_CURRENT_ARCHITECTURE](../../ai/SCMOS_CURRENT_ARCHITECTURE.md), [SCMOS_DOMAIN_MAP](../../ai/SCMOS_DOMAIN_MAP.md), [LINE integration](../line/), [AI platform assessment](../../ai/SCMOS_AI_PLATFORM_ASSESSMENT.md).

---

## 1. Current architecture

| Layer | What exists | Evidence |
| --- | --- | --- |
| Web | Next.js 16 App Router, `output: standalone`, one client root (`SCMOSApp.tsx`) selecting 52 screens; same-origin `/api/*` proxy adds `X-Scmos-Proxy-Key` | `app/`, `app/api/[...path]/route.ts` |
| API | .NET 10 ASP.NET Core **minimal APIs** (no controllers), EF Core 10 SQL Server, scoped services, pure rules in `Rules/` | `server/Scmos.Api/Program.cs`, `Endpoints/`, `Services/`, `Rules/` |
| Database | Azure SQL serverless (`scmos-sql-3959/scmos`, GP_S_Gen5_2, auto-pause 60 min), 42 EF migrations, snake_case tables, every `id` an identity | `Data/ScmosDbContext.cs`, `Data/Migrations/` |
| Files | Azure Blob, private container `operation-files` | `Services/BlobFileStore.cs`, `Rules/BlobPaths.cs` |
| Identity | Entra ID through App Service Web App Login on the web app; the API trusts the forwarded principal only with the proxy key (`Auth:Mode=Proxy`); recognition against the `staff` table | `Auth/UserAccessor.cs`, `Services/StaffService.cs` |
| Background | Hosted services: report scheduler, LINE worker/reminder/chase, mail worker, Graph subscription renewal | `Program.cs` lines 60–126 |
| Errors | `{ "error": "…" }` JSON with a status from `ApiResults.Error`; `AddProblemDetails()` + `UseExceptionHandler()` only for unhandled exceptions | `Endpoints/ApiResults.cs`, `Program.cs` 185/317 |
| Health | `GET /health` with an EF `DbContext` check | `Program.cs` 184/320 |
| Telemetry | `ILogger`; Application Insights wired **only if** a connection string is configured — none is on production | `Program.cs` 154–157; live app settings |

The register is one table, `operation_jobs`: filtered columns (category, owner id, work date, customer, trucker, job code, container, status) plus the whole job as JSON in `data`, which is how new cells arrive without a migration. There is **no history table** for it — the audit trail (`audit_events`) is the only record of previous values (memory: *never test writes on the live register*).

## 2. Booking flow

A "booking" in SCMOS is a row of the register keyed by the operator (My Job grid, or the plan-workbook import). The flow, as enforced in code rather than as drawn in the WI:

1. **Register row** — `PUT /api/jobs` (`JobsEndpoints.cs:199`), guarded by `EditOwnJobs`/`EditAnyJob`, the data standard (`Rules/Formats.cs`), and the calendar-date guard (`Rules/JobDateInputGuard.cs`).
2. **Workflow** — `Rules/Workflow.cs` defines 21 stages (Received → Reviewed → DocumentVerification → SupplierSelection → CapacityRequested → SupplierAssigned → PreRunVerification → … → Delivered → ContainerReturned → PodCollected → BillingVerified → KpiCalculated → Closed), five gates with hold reasons. `WorkflowService` derives the stage from `workflow_events` ("the history is the truth"); a job with no events starts from its register status.
3. **Carrier selection** — `POST /api/workflow/{jobKey}/supplier-request`, `/assign-carrier`, `/supplier-response` (`WorkflowEndpoints.cs:82–94`), enforced by `Rules/CarrierAssignment.cs`.
4. **Carrier answer** — either an operator keys it (`supplier-response`), or the carrier's own account answers in the portal (`POST /api/carrier/{jobKey}/accept|decline`, `CarrierEndpoints.cs`), or — since 16 Sep — the carrier's LINE room says it and the job's owner approves (`LineReviewEndpoints.cs`).
5. **Execution** — status moves on the register (`Rules/JobStatus.cs` ladders), arrival stamped into `arrDate`/`arrTime` (what OTD is measured from, `Rules/JobRules.cs`), pre-run check (`/api/pre-run`), monitor (`Rules/MonitorRules.cs`).

Observation: the workflow stage model and the register status are **two vocabularies**. The operators work the register status daily; `workflow_events` is sparsely populated for the 2,102 migrated jobs and the imported ones. A carrier API must be anchored to the **register status + supplier_requests**, which is what the LINE integration already does, not to the workflow stage.

## 3. Carrier model

| Concept | Where | Notes |
| --- | --- | --- |
| Supplier master | `suppliers` (`Data/SupplierEntities.cs`: Id, Code, Name, Status draft/…, VendorNo, TaxId, Address, ServiceArea, AbsNo, ListType ASL/BSL…) | one row per haulier |
| Spellings | `supplier_aliases` | the register writes the carrier the way the plan workbook spelt it; `CarrierDirectory` and `LineAuthority.SameCarrier` resolve through aliases |
| Contacts / trucks / drivers | `supplier_contacts`, `supplier_trucks`, `supplier_drivers` | master data kept by SCMOS staff, not by the carrier |
| Capacity | `supplier_capacity` (`/api/capacity`) | daily truck counts by type |
| Evaluation / compliance | `supplier_evaluations`, `Rules/SupplierCompliance.cs`, `Rules/SupplierRegister.cs` | five held documents, annual evaluation |
| Carrier account | `staff` row with `role = Subcontractor` and `supplier_id` | `CarrierService.CompanyOfAsync` — the company is **derived from the authenticated user, never from the request** |
| Carrier on a job | `operation_jobs.trucker` (a name) | matched by name/code/alias, not by id |
| Carrier's LINE room | `line_groups` (LineGroupId → SupplierId, GroupType VENDOR) | since 16 Sep 2026 |

**Gap:** the job's carrier and the supplier request's carrier are **strings**, joined to `suppliers` by name and alias at read time. An external TMS integration keyed on `supplier_id` must go through that same resolution (`CarrierDirectory`/`LineAuthority.SameCarrier`) — it must not add a second one.

## 4. Assignment logic

`Rules/CarrierAssignment.cs` — sequential, one carrier at a time, in priority order:

- `OneAtATime`, `CloseBeforeNext`, `PriorityOrder`, `ConfirmedBeforeAssign`, `NoRepeatCarrier`; each breach returns a reason for the operator.
- `WorkflowService.PriorityForAsync` ranks carriers (on-time rate only above `MinimumSample = 5` runs — *statistical honesty*).
- The **request** is the unit of assignment: `supplier_requests` (JobKey, Rank, Carrier, QuotedPrice, Outcome `pending|confirmed|rejected|cancelled`, Reason, RequestedBy, RequestedAt, RespondedAt). Accepting writes `trucker/licence/driver/contact/status=SUPPLIER_CONFIRMED` onto the job (`CarrierService.AcceptAsync`), records the previous values in the audit row, and cancels every other open request for the job.
- **Assignment history already exists** as: the `supplier_requests` rows (never deleted), `workflow_events`, and `audit_events` (action `update`, field `trucker, licence, driver, contact`, old/new values). The spec's "do not delete assignment history" is already the behaviour; it must stay so.

## 5. Status model

- **Register status** — `Rules/JobStatus.cs`: controlled codes (DRAFT … DISPATCHED, PICKED_UP, LOADING, IN_TRANSIT, DELIVERED, CONTAINER_RETURNED, DOCUMENT_PENDING, BILLING_PENDING, COMPLETED; exits CANCELLED, HOLD) on **three ladders** (IMPORT, EXPORT, DELIVERY). `LineAuthority.Rank/Move` validates a transition (forward only, closed/held refused, "ถึงโรงงาน" resolved per category by `ResolveSite`). The jobs PUT (grid) accepts any controlled code — an operator may correct backwards; a machine must not.
- **Workflow stage** — `Rules/Workflow.cs` (see §2), separate vocabulary, derived from events.
- **Arrival** — `arrDate` + `arrTime` cells; `LineAuthority.AwaitingArrival` is the one rule for "still waiting" (chase, photos).
- **Operational risk** — `Rules/MonitorRules.cs` (overdue / unassigned / no carrier / no truck, two-day window), `Rules/PreRun.cs` SLA.

A carrier API status vocabulary must **map onto `JobStatus` by ladder** and validate with `LineAuthority.Move` — the same rule LINE uses — not introduce a fourth vocabulary (memory: *duplicated rules drift*).

## 6. Truck and driver

- On the job (JSON cells): `licence`, `driver`, `contact`, `container`, `seal`, plus `type` (vehicle type, controlled vocabulary in `vehicle_types`, `Rules/JobVehicleType.cs`).
- Master: `supplier_trucks`, `supplier_drivers` (per supplier), `drivers` + training tables (customer training control) — two different driver entities, deliberately not merged.
- Writes from outside are **only into empty cells** (LINE approval, `LineReviewEndpoints.DetailsWrite`; carrier portal `AcceptAsync` overwrites because acceptance *is* the assignment). Plates are compared through `LineParser.PlateKey` (province stripped); phones normalised to `0XX-XXXXXXX` (`Formats`).

## 7. Documents

- `documents` table + Blob (`DocumentService`, `BlobFileStore`, `Rules/BlobPaths.cs`, `Rules/DocumentChecklist.cs`); CAR/PAR evidence; mail attachments (`email_attachments`); LINE photos (`line_events.image_key`, under `line/{yyyy}/{MM}/`).
- Per-job document upload needs `UploadDocuments` (the Subcontractor role holds it — the portal can already attach a POD).
- No signed-URL issuance for third parties exists; the API streams from Blob after the capability check.

## 8. Authentication and authorisation

- **Humans:** Entra ID (Easy Auth) → `X-MS-CLIENT-PRINCIPAL` → proxy key → `UserAccessor.Current()`; unknown accounts are refused, not given Viewer (memory: *authenticated is not authorised*); guests have two names (memory).
- **Roles/capabilities:** `Rules/Roles.cs` — nine roles, capability flags; `Subcontractor` holds `ViewDashboard`, `EditOwnJobs`, `UploadDocuments` and **not** `ViewRates`, `ViewDirectory`, `ViewTeam`. Selling rates are gated by `ViewRates` everywhere (`RateEndpoints`, `QuoteCardEndpoints`).
- **Second factor:** `ApiResults.NeedsSecondFactor` on the three guarded capabilities (`Auth__SignInPolicy=Record` today).
- **Machines:** there is **no machine credential of any kind** — no API keys, no client-credentials flow, no per-carrier tokens. The LINE webhook is the only inbound machine caller, authenticated by HMAC (`Rules/LineSignature.cs`); Graph notifications by client state. A carrier TMS calling `/api/carrier/v1/` therefore needs a **new credential scheme** — this is the one genuinely new security surface.

## 9. Existing APIs the integration touches

| Route | Guard | Purpose |
| --- | --- | --- |
| `GET /api/carrier` | signed-in + Subcontractor with supplier_id, else 403 | offered + accepted jobs, scoped by `CarrierService` |
| `POST /api/carrier/{jobKey}/accept` · `/decline` | same | one action with plate/driver/contact; audited with before-values |
| `GET /api/workflow/{jobKey}`, `POST …/supplier-request`, `/assign-carrier`, `/supplier-response` | operator capabilities | the request lifecycle |
| `GET /api/jobs/page`, `/since`, `PUT /api/jobs` | `EditOwnJobs`/`EditAnyJob`, `ViewTeam` | the register |
| `/api/integrations/line/*` | webhook HMAC; review endpoints signed-in / owner / `ManageSuppliers` | the LINE counterpart of everything a TMS would post |
| `/api/suppliers/*`, `/api/capacity`, `/api/vehicle-types` | `EditSuppliers`/`ManageSuppliers` | master data |
| `/api/audit` | `ViewAudit` | the trail |

Not found: API versioning, idempotency keys, correlation-id propagation (only `HttpContext.TraceIdentifier` written into audit rows, `AuditService.cs:140`), outbound webhooks, rate limiting (only `Ai/AiRunLimiter.cs` for AI), OpenAPI document exposure for third parties.

## 10. Tables involved

`operation_jobs`, `supplier_requests`, `workflow_events`, `audit_events`, `suppliers`, `supplier_aliases`, `supplier_contacts`, `supplier_trucks`, `supplier_drivers`, `supplier_capacity`, `vehicle_types`, `staff` (carrier accounts), `documents`, `line_groups`/`line_events` (the parallel channel), `job_delegations` (owner approval), `pre_run_checks`, `shipment_milestones`, `delay_records`. Full list: 59 `DbSet`s in `ScmosDbContext.cs`.

## 11. Audit and traceability

- `audit_events` (`Data/AuditEntities.cs`): At, Who, WhoId, Role, Action, Entity, EntityId, EntityLabel, Field, OldValue, NewValue, Reason, plus source (`EventSource` — web, LINE, …) and the request's `TraceIdentifier`. Append-only by policy; `AuditService.RecordAsync` is best-effort, `Stage` joins the caller's transaction.
- Every job write path captures previous values (carrier accept, LINE approval, jobs PUT, transfer).
- `ai_audit_logs` is a separate, stricter store for AI runs — not for the carrier API.
- The LINE ledgers (reminder/chase/summary) are audit rows with `Entity = line-group|job`, `Action = notify|chase`, `Field = slot`.

## 12. Azure components

`rg-scmos` (Southeast Asia): `scmos-plan` Basic **B1, one worker shared by web and API**; `scmos-web-3936` (Node 22); `scmos-api-3936` (.NET 10); `scmos-sql-3959/scmos` serverless (wakes in ~110 s, the whole API goes quiet — memory); `scmosfiles6655/operation-files`. **Not present:** Key Vault, Application Insights, deployment slot, API Management, Front Door/WAF, managed-identity SQL auth (SQL login `scmosadmin`, the password in three places — memory). CI/CD: GitHub Actions with OIDC; `api.yml` deploys on push (with EF migrations), `web.yml` dispatched by hand with `slot=production`; each web deploy takes the site down ~90 s.

## 13. What can be reused

- `CarrierService` — the carrier boundary (company from the principal, alias-aware names, offered/accepted, accept-with-truck, decline, cancel-the-others). This **is** the carrier API's domain service; V1 wraps it, it does not re-implement it.
- `WorkflowService.RequestSupplierAsync / RespondSupplierAsync / AssignCarrierAsync` + `Rules/CarrierAssignment.cs`.
- `JobsRepository.PatchAsync / SnapshotAsync` (JSON cell writes with before-values), `JobRegisterCache`.
- `Rules/JobStatus.cs` + `LineAuthority.Move/Rank/ResolveSite/AwaitingArrival` — transition validation.
- `Rules/Formats.cs` (dates dd/MM/yyyy, times HH:mm, plates, phones, containers), `Rules/ContainerNumbers.cs` (ISO 6346).
- `AuditService`, `EventSource`, `NotificationService` (the bell), `DelegationService`/`MayActOn` (who may approve).
- `LineReviewEndpoints` approval pattern (claim NEED_REVIEW → PROCESSING atomically; write only empty cells; audited under the approver, source tagged).
- `CarrierDirectory` / `supplier_aliases` for name resolution; `Rules/LineSignature.cs` as the model for HMAC-signed inbound calls; `LineNotifier` as the model for an outbound client with quota awareness.

## 14. Gaps

1. **No machine identity** (see §8) — per-carrier credentials, rotation, revocation, scoping to one `supplier_id`.
2. **Carrier keyed by name** on jobs and requests — needs the alias resolver on every call; no `supplier_id` FK on `supplier_requests`.
3. **No Problem Details** on business errors (`{error}` shape) — the existing shape is what the workspace reads and must stay; a new group can answer RFC 9457.
4. **No idempotency** for POSTs (LINE de-duplicates on `LineMessageId` only).
5. **No correlation header** in/out; `TraceIdentifier` only.
6. **No outbound webhooks/events**; the only push channel is LINE.
7. **No API versioning**, no third-party OpenAPI, no rate limiting.
8. **Backward compatibility** with the LINE channel: a truck detail or arrival may arrive by LINE, by the portal and by a TMS — the *empty-cell rule* and *forward-only ladder* are what keep three writers from fighting; both must be applied identically.
9. **B1 plan + serverless SQL**: a TMS polling every minute would keep the database awake (cost) and share one worker with the operators.

## 15. Conflicts with the specification

| Specification | SCMOS today | Resolution |
| --- | --- | --- |
| "Assignment" as a first-class entity with lifecycle + history | `supplier_requests` rows + job cells + audit | Treat a supplier request **as** the assignment; expose it under `/api/carrier/v1/assignments/{id}`; no new table for V1 |
| 404 for another carrier's assignment | Portal answers 400 "งานนี้ไม่ได้ถูกส่งมาให้บริษัทนี้…" | The v1 group returns 404 (existence not confirmed) — a *new* endpoint's behaviour, the portal's unchanged |
| Problem Details | `{error}` | New group only |
| Status transitions validated | validated for LINE (`Move`), not for the grid PUT | Reuse `Move`; never add a grid-style free write |
| Carrier identity from auth only | true for the portal (`CompanyOfAsync`) | keep; a client-credential must map to exactly one `supplier_id` server-side |
| No selling rates | Subcontractor lacks `ViewRates` | keep; the v1 contract carries no price except the request's own `QuotedPrice`, and only if the department says so |
| Idempotency, correlation ids, webhooks | absent | add (§18) |

## 16. Recommended architecture (V1)

```
Carrier TMS ──(client credentials / HMAC)──▶ /api/carrier/v1/*  (new minimal-API group)
                                                │  CarrierApiAuth: principal → supplier_id (never from the body)
                                                │  ProblemDetails, X-Correlation-Id, Idempotency-Key
                                                ▼
                                    CarrierService (existing) · WorkflowService (existing)
                                    JobStatus + LineAuthority.Move (existing validation)
                                    JobsRepository.PatchAsync (empty-cell rule, before-values)
                                    AuditService (source = carrier-api)
                                                ▼
                                    operation_jobs · supplier_requests · audit_events
Outbound (later slice): CarrierWebhookNotifier ← same ledger pattern as LineNotifier/LineChase
```

One common API for every carrier; the carrier is the credential's supplier. The LINE room and the portal keep working — all three are writers through the same services and the same rules.

## 17. Database proposal (additive; none required for the read slice)

- `carrier_api_clients` (id, supplier_id FK, client_id, secret_hash/thumbprint, status, created_at, revoked_at, last_seen_at) — **required** for any authenticated call.
- `carrier_api_requests` (id, client_id, idempotency_key, request_hash, response_status, response_body, created_at; unique client_id+key) — idempotency ledger.
- `carrier_webhooks` / `carrier_webhook_deliveries` — only with the outbound slice.
- Optional, later: `supplier_requests.supplier_id` (nullable, backfilled from the alias resolver) so the join stops depending on spelling. **Not** in V1; the resolver is enough and changing the join changes what the portal shows.
- **Avoid:** any change to `operation_jobs`, `audit_events`, `suppliers`; any rename; any destructive migration (the memory on identity ids applies: never pin an id on insert).

## 18. API proposal — `/api/carrier/v1/`

| Method / route | What | Rules applied |
| --- | --- | --- |
| `GET /assignments?status=offered|accepted&from&to` | the carrier's requests and jobs, paged | `CarrierService.ReadAsync` scope; no other carrier's rows, ever |
| `GET /assignments/{requestId}` | one request with its job's carrier-visible cells | 404 unless the request is this carrier's |
| `POST /assignments/{requestId}/accept` | plate, driver, contact (+ container/seal on export) | `CarrierService.AcceptAsync`; `Idempotency-Key` required |
| `POST /assignments/{requestId}/decline` | reason | `DeclineAsync` |
| `POST /assignments/{requestId}/events` | `{ type: DISPATCHED|PICKED_UP|IN_TRANSIT|ARRIVED|DELIVERED|…, at, remark }` | `LineAuthority.Move` on the job's ladder; arrival written only into empty `arrDate/arrTime`; anything the ladder does not know → job REMARK (the LINE rule); every event **queued for the owner's approval** like a LINE message in V1, auto-apply only once the department says so |
| `PUT /assignments/{requestId}/truck` | correct plate/driver/contact | empty cells only; conflicts return 409 with the current value |
| `GET /vehicle-types`, `GET /me` | vocabularies, the credential's supplier | read-only |
| Errors | RFC 9457 Problem Details (`type`, `title`, `status`, `detail`, `instance`, `correlationId`); never a stack trace | `Program.cs` already registers `AddProblemDetails` |
| Headers | `X-Correlation-Id` echoed/generated; `Idempotency-Key` on every POST/PUT (replay returns the stored response) | new middleware on the group only |

Nothing here duplicates an existing endpoint: the group composes the same services the portal and the LINE approval call.

## 19. Security proposal

- **Credential:** Entra ID *application* per carrier (client-credentials flow, tenant-issued JWT validated by the API — no shared secrets stored by SCMOS) **or** an SCMOS-issued API key stored as a hash in `carrier_api_clients`. Recommendation: Entra client credentials for carriers that have a tenant, hashed API key as the fallback; both map to one `supplier_id`; rotation by adding a second row, revocation by `revoked_at`.
- **Isolation:** every query begins with the credential's `supplier_id` → names via aliases → `CarrierService`; unknown/other rows are 404.
- **Strict transitions:** forward-only by ladder; closed/held refused; no status from the body is trusted without `Move`.
- **No secrets in logs/git/source:** the API key hash only; logs carry the client id and the correlation id, never the key; `appsettings.*` carry no values (the live secrets are App Service settings — Key Vault does not exist, and adding it is a separate infrastructure decision).
- **Rate limit** the group (ASP.NET rate limiter, per client id) so a polling TMS cannot keep the serverless database awake or starve the B1 worker.
- **Human authority preserved:** V1 events are proposals the job's owner approves (the LINE pattern); a later flag may auto-apply ARRIVED/DELIVERED for named carriers.

## 20. Phases

| Phase | Scope | Gate |
| --- | --- | --- |
| 0 | this document | review |
| 1 | `carrier_api_clients` (+ migration), credential validation, `/me`, `GET /assignments*`, Problem Details, correlation id, rate limit; checks (`--check-carrier-api`: isolation, 404, unknown client) | one carrier reads its own rows on production; another cannot |
| 2 | `accept`/`decline`/`truck` with idempotency ledger | portal, LINE and API agree on the same job |
| 3 | `events` queued for owner approval; REMARK fallback | approvals visible in My Job like LINE rows |
| 4 | outbound webhooks (assignment offered/cancelled), retries, delivery ledger | one carrier's TMS receives an offer |
| 5 | auto-apply for named carriers behind `CarrierApi__AutoApply` | department sign-off |

---

## Summary — KEEP / REUSE / EXTEND / ADD / AVOID CHANGING

**KEEP** — `operation_jobs` shape and JSON cells; `supplier_requests` as the assignment record; `JobStatus` ladders; the empty-cell write rule; owner approval; `{error}` shape on existing routes; the LINE channel and the carrier portal as they are.

**REUSE** — `CarrierService`, `WorkflowService`/`CarrierAssignment`, `JobsRepository.PatchAsync/SnapshotAsync`, `LineAuthority.Move/Rank/ResolveSite/AwaitingArrival`, `Formats`, `ContainerNumbers`, `AuditService`/`EventSource`, `CarrierDirectory`/aliases, `DelegationService`, `NotificationService`, the LINE approval flow and its screens.

**EXTEND** — `EventSource` (a `carrier-api` source), `AuditService` (correlation id as a first-class column is optional; the `TraceIdentifier` slot exists), `Roles` (a machine principal shape without adding a human role), `Program.cs` (the v1 group, rate limiter, Problem Details on the group), the LINE screen or a new "Carrier API" card for client management (`ManageSuppliers`).

**ADD** — `carrier_api_clients`, `carrier_api_requests`; `Endpoints/CarrierApiEndpoints.cs`; `Auth/CarrierApiPrincipal.cs`; `Rules/CarrierApi.cs` (pure: status mapping, event validation, Problem Details vocabulary) with `--check-carrier-api`; later `carrier_webhooks*` and a notifier.

**AVOID CHANGING** — the portal's existing three routes and their messages; the jobs PUT; `supplier_requests` columns (until the optional `supplier_id` backfill is reviewed); any rule that would exist in two places (status validation, name resolution, plate/phone normalisation); production data; secrets; the `ApiResults` shape.
