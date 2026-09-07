# SCMOS Current Architecture — Phase A Discovery

ตรวจเมื่อ 7 กันยายน 2026 (Asia/Bangkok) จาก repository จริง ไม่ใช่จากภาพหน้าจอหรือสเปคตัวอย่าง

Workspace: `D:\Leschaco\Dashboard\scmos-report-dashboard`

Snapshot: branch `azure-dotnet-migration`, HEAD `9b31ab49aa86a148fcb0d65deca0f4f6b06c36dc` ณ จุดจัดทำรายงาน งานใน workspace เปลี่ยนระหว่างตรวจจาก `5350264` เป็น `9b31ab4`; commit หลังรวมการปรับ Cargo Receipt และการแก้สถานะ Integrations ด้วย รายงานนี้อธิบายโค้ดในเครื่อง ไม่ได้ยืนยันว่า HEAD นี้ deploy แล้ว

## 1. Executive finding

SCMOS เป็น Next.js frontend + ASP.NET Core API + Azure SQL อยู่แล้ว และมีรากฐาน AI บางส่วนจริง ได้แก่ `AiGateway`, `AiPermissions`, `ai_tools`, `approvals`, หน้าผู้ช่วย AI และ OpenAI document extraction ต้องต่อยอดส่วนเหล่านี้ ไม่ตั้งระบบ TypeScript backend, ORM หรือฐานข้อมูลชุดใหม่คู่ขนาน

สิ่งที่ยังไม่มี: conversational orchestrator, read-tool handlers ที่ผูกกับ AI invoke, persistent AI run/tool-call telemetry, conversation storage, unified AI feature flags, per-tool input schemas และ Control Tower ตามสเปค V1

## 2. Evidence and limits

- ตรวจ source, package manifests, API routes, services, rules, EF model/migrations, auth และ GitHub workflows
- ตรวจ Azure resource metadata แบบอ่านอย่างเดียวใน `rg-scmos` และสถานะฐาน `scmos`; ไม่อ่าน customer/job rows จาก Production
- ไม่ได้ query `sys.tables`, `sys.columns` หรือ `__EFMigrationsHistory` บน Production ดังนั้น schema ด้านล่างคือ schema ที่ประกาศใน repository ไม่ใช่การรับรอง schema drift ว่าไม่มี
- `.env*` ตรวจเฉพาะชื่อ setting; ไม่แสดงค่าคีย์ connection string หรือ token
- ไม่ทำ migration, seed, deploy, push, เปลี่ยน permission หรือเรียกโมเดลใน Phase A

## 3. Application

| รายการ | สิ่งที่พบ | หลักฐาน |
| --- | --- | --- |
| Framework | Next.js `16.3.1`, React/React DOM `19.2.6` | `package.json` |
| TypeScript | `5.9.3`, `strict: true`, bundler resolution | `package.json`, `tsconfig.json` |
| Router | App Router; `app/page.tsx` โหลด principal แล้ว render `SCMOSApp` | `app/page.tsx`, `app/auth.ts` |
| Navigation | หน้า business ส่วนมากเปลี่ยน `Screen` ใน React state ไม่ใช่แยก URL ต่อเมนู | `app/SCMOSApp.tsx`, `app/scmos/nav.ts` |
| UI | component ของโครงการ, `css()` helper, global CSS, Tailwind 4; มี shadcn-style Card ที่เก็บ source เอง | `app/scmos/theme.ts`, `ui/Card.tsx`, `globals.css` |
| Charts / files | Recharts `3.10.1`, SheetJS `0.20.3` จาก CDN tarball, PptxGenJS | `package.json` |
| State | React hooks, save queue, in-memory/server cache และ sessionStorage page cache keyed ตามผู้ใช้ | `SCMOSApp.tsx`, `store.ts`, `pageCache.ts` |
| Validation | typed DTOs + business validation ใน C# และ TS; ไม่พบ Zod/FluentValidation ที่เป็นมาตรฐานกลาง | `Rules/`, `Endpoints/`, package manifests |
| API calls | fetch ผ่าน `app/scmos/api.ts` และ same-origin proxy; ไม่พบ axios | `app/scmos/api.ts`, `app/api/[...path]/route.ts` |
| Build | Next standalone; server เป็น .NET 10 | `next.config.ts`, `Scmos.Api.csproj` |

โฟลเดอร์จริงระดับ root: `app/`, `server/`, `infra/`, `migration/`, `tests/`, `public/`, `.github/` และ generated/runtime folders

ไม่พบ root `src/`, `pages/`, `components/`, `lib/`, `services/`, `api/`, `prisma/`, `drizzle/`, `database/`, `db/`, `types/`, `hooks/`, `utils/`, Dockerfile, Docker Compose หรือ `.openai/hosting.json` ไม่พบ Next middleware หรือ Server Actions ใน source ที่ตรวจ

## 4. Actual request path

```text
Browser
  -> App Service Web App Login / Entra principal
  -> Next app/page.tsx -> SCMOSApp / existing screens
  -> app/scmos/api.ts (same-origin fetch)
  -> app/api/[...path]/route.ts (server proxy)
  -> ASP.NET Core Minimal API Endpoints
  -> IUserAccessor + capability/ownership guards
  -> Services / Rules / JobsRepository
  -> ScmosDbContext / Azure SQL
  -> IFileStore / private Blob when documents are involved
```

Proxy forwards a fixed list of principal/request headers, adds server-side proxy key, streams bodies and replies with `cache-control: no-store`. Production trust depends on Web App Login being in front of Next and API restrictions/shared-key verification remaining intact. The new AI layer must not accept a role, owner scope or proxy identity from a model's arguments.

### Backend organization

- `server/Scmos.Api/Program.cs`: dependency injection, configuration, compression, health, OpenAPI in Development, endpoint registration and explicit administrative command modes
- `Endpoints/`: 27 `*Endpoints.cs` files; ASP.NET Core Minimal APIs, not Azure Functions
- `Services/`: business services and integration adapters
- `Rules/`: deterministic rules for roles, jobs, KPI, notifications, training, rates and workflow
- `Data/`: EF entities, `ScmosDbContext`, `JobsRepository`, `JobRegisterCache`, migration history and rule-check commands
- Repository pattern is selective (`JobsRepository`), not universal: many services query the EF context directly. Several security checks live at endpoints, not inside services. Calling a service from an AI tool must therefore repeat/reuse the same authorization guard explicitly.

## 5. Database

Engine: Microsoft SQL Server / Azure SQL. ORM: EF Core SqlServer `10.0.11`; target framework `net10.0`. No Prisma, Drizzle, PostgreSQL or Supabase integration found in active application code.

`ScmosDbContext` maps **48 tables**. There are **33 migration files** excluding designers and snapshot, from `20260816144608_InitialAzureSql` through `20260906161115_ReportArchive` in `server/Scmos.Api/Data/Migrations/`.

### Main entity map

These arrows are **logical joins by keys**, not claims that foreign-key constraints exist in the live database. No `HasForeignKey`/`ForeignKey(...)` declarations were found in the model/migration sources inspected.

```text
staff.Id -> operation_jobs.owner_id
staff.Id -> job_delegations.OwnerId / DelegateId
operation_jobs.key -> workflow_events.JobKey
                   -> supplier_requests.JobKey
                   -> pre_run_checks.JobKey
                   -> shipment_milestones.JobKey
                   -> delay_records.JobKey
                   -> operational_issues.JobKey
                   -> documents.JobKey

suppliers.Id -> supplier_aliases / supplier_contacts
             -> supplier_trucks / supplier_drivers
             -> supplier_capacity / supplier_evaluations
             -> drivers.SupplierId / rate_lanes.SupplierId

drivers.Id + training_courses.Id -> driver_training
customer + training_courses.Id -> customer_training_requirements
rotation_assignments -> customer names / assigned staff / carrier choices

rate_inquiries.Id -> rate_inquiry_lanes.InquiryId
rate_inquiry_lanes.Id -> rate_inquiry_prices.LaneId
                     -> rate_lanes.FromInquiryLaneId (promotion provenance)
rate_lanes.Id -> rate_prices.LaneId + Vehicle + BandPosition
fuel_bands -> band-based rate selection

customer_rate_lanes.Id -> customer_rate_prices.LaneId
customer_rate_bands -> customer-specific fuel bands
ai_tools.Name <-> AiPermissions.Catalogue
approvals.Tool -> named proposed AI action
audit_events.Entity + EntityId -> logical target reference
```

Additional tables: `vehicle_types`, `type_migration_backup`, `journey_distances`, `quote_vehicle_rates`, `quote_extras`, `quote_settings`, `rate_surcharges`, `customer_training_records`, `incident_cases`, `report_archive`, `cargo_form_templates`, `operation_uploads`, `operation_entries`, `report_uploads`.

### Important storage semantics

- `OperationJob.Key` is the job PK (max 80); job code/container are not safe substitutes for unique identity.
- `operation_jobs` stores query columns (`cat`, `owner_id`, `work_date`, `customer`, `trucker`, `status`, etc.) alongside full job JSON in `data` as `nvarchar(max)`.
- Jobs include legacy text dates and no-date values. Reuse `Formats`, `WorkspaceTabs` and existing date logic; do not force all text through a new SQL DATE conversion.
- JSON is serialized text, not PostgreSQL JSONB. Do not design migrations against a different database type.
- `ai_tools` and `approvals` already exist through `20260817070005_SuppliersRatesAndAiPermissions`.
- `audit_events` already stores actor, role, entity, before/after text, reason, source and sign-in method, but not full AI run/conversation metadata.
- `PlanSeeder`, administrative normalization commands and `--migrate` are not discovery tools. Do not execute them during inspection.

## 6. Authentication / authorization

Source: `app/easy-auth.ts`, `app/auth.ts`, `Auth/UserAccessor.cs`, `Auth/AppUser.cs`, `Rules/Roles.cs`, `Endpoints/MeEndpoints.cs`.

- Existing auth is App Service Web App Login / Microsoft Entra, not NextAuth. Session cookie belongs to the platform; UI obtains capabilities and operator ID from `/api/me`.
- API supports `Auth:Mode` = Proxy, Platform, Development. Proxy validates shared secret with fixed-time comparison. Development header is accepted only in a Development host.
- Staff directory and configured role overrides resolve server-side identity. `AllowUnknown` defaults false. Browser names and UI controls are not authorization.
- Actual roles: Administrator, Manager, Assistant Manager, Operation Supervisor, Subcontractor, Operation User, CS, Management, Viewer.
- Capabilities include ViewDashboard, ViewTeam, EditOwnJobs, EditAnyJob, AssignJobs, UploadDocuments, ViewRates, EditRates, QuoteToSheet, ManageSuppliers, CloseCarPar, ApproveAi, ViewAudit, ViewDirectory, ApproveRetention, AdministerData, ManageTraining.
- `QuoteToSheet` is distinct from `EditRates`. `ViewAudit` is distinct from `ViewDirectory`. Do not collapse either pair in AI policies.
- Ownership/delegation is checked during job writes. Carrier accounts use carrier-scoped routes; the generic register denies Subcontractor accounts.
- MFA evidence uses the existing SignIn policy; discovery does not change enforcement or grant new rights.

## 7. Existing AI — implemented versus scaffold

| Component | Current state | V1 gap |
| --- | --- | --- |
| `Services/AiGateway.cs` | Registered service; allow/approval/deny lookup; persists drafts and decisions | No per-tool business capability or schema in definition; no orchestrator; no run-level audit |
| `Rules/AiPermissions.cs` | 21 tool definitions and forbidden actions; six agent category strings | Categories are not eight executable specialist agents |
| `GET /api/ai/tools` | Reads DB roster and code policy | Response must become caller-filtered before real tools are exposed |
| `POST /api/ai/invoke` | Authenticated; deliberately passes a null executor | Read tools refuse as not connected; approval tools can create a draft |
| `GET /api/ai/approvals` | Returns up to 200 approval rows | Currently lacks caller/entity scoping beyond authentication |
| Approval decision / applied routes | Record decision and audit; applied marks state | Marking applied is not actual tool execution; no payload version lock or exactly-once execution contract |
| `screens/Assistant.tsx` | Tool catalogue, approval queue, rule-based risk answer | Not a free-text model conversation or V1 Control Tower |
| `RiskService` / `/api/risk` | Deterministic risk groups with evidence and rule basis | No configurable 0–100 score; scope and total semantics need review |
| `DocumentExtractor` / `/api/ai-extract` | Actual OpenAI SDK document extraction, strict schema, user review before job creation | Separate from gateway; no unified AI run audit/rate limit flags |

Existing provider configuration is **`OpenAI:ApiKey`, `OpenAI:Endpoint`, `OpenAI:Model`**, bound in .NET. Default model in code is `gpt-4.1`; this is a code fact, not a new model recommendation. SDK package is `OpenAI 2.13.0`. A new `OPENAI_API_KEY` alias, if desired, must be mapped server-side deliberately; the present code does not automatically bind that name to `OpenAI:ApiKey`.

## 8. Deployment — repository and live metadata

GitHub repository: `fose-cloud/SCMOS`.

Verified read-only in Azure resource group `rg-scmos`, region `southeastasia`:

| Resource | Observed |
| --- | --- |
| Web App | `scmos-web-3936` |
| API App | `scmos-api-3936` |
| App Service plan | `scmos-plan` |
| SQL server / DB | `scmos-sql-3959` / `scmos`; Online, GeneralPurpose, SQL_Latin1_General_CP1_CI_AS |
| Storage | `scmosfiles6655` |
| Other SQL DB | `scmos-pre-perf-20260821-095112`; not touched |

Key Vault and Application Insights are supported conditionally by code but were **not returned in this resource group's inventory**. Do not state they are deployed or configured; they could exist outside the inspected scope. No Azure Functions, Service Bus, Static Web Apps or container resource was returned in this scope. Cloudflare/D1 references in migration documentation are historical, not the active ORM/database.

- Web workflow: push to `main` deploys production; manual dispatch selects staging/production. Package contains deployment SHA and the workflow checks it on Azure.
- API workflow: **pushes changing `server/**` on `azure-dotnet-migration` can automatically deploy and apply production migrations**. Manual dispatch also defaults migrate to true. Therefore a server-code push is a production-affecting operation, not merely backup.
- API migration command explicitly calls `Database.MigrateAsync`; ordinary host startup does not automatically migrate.
- `ReportScheduler` is an in-process hosted background service, not an Azure Function. It is useful precedent for future workers, but not a durable distributed event bus.
- Last Web run observed during discovery: success at `f070c09`, run `34079460688`. At the document snapshot HEAD is five commits ahead of the tracked remote. No assertion that the new HEAD is live.

## 9. Safe integration points and risks

### Safe direction

Add AI orchestration **inside the existing .NET API**, behind `IUserAccessor`, reusing named services through explicit adapters. Keep Next as UI/proxy. Preserve existing `/api/ai/*` routes and `assistant` screen contracts. Add a new Control Tower screen with optional `/ai-control-tower` entry using the existing authenticated shell; do not create a second identity system.

Model function calls are requests to application-owned handlers, not authority to execute arbitrary code. OpenAI's function-calling guide describes application-side execution and strict schemas; SCMOS must still apply business permissions independently. [Official OpenAI function-calling documentation](https://developers.openai.com/api/docs/guides/function-calling)

### Risks requiring a decision or test before activation

1. **Authorization gap:** current `AiPermissions.Allow` says what AI may do in general, not what this caller may read. Never attach query handlers without capability, entity scope and field projection checks.
2. **Blank-owner fail-open risk:** `/api/risk` passes OperatorId for users without ViewTeam; `RiskService.TodayAsync` filters only for a nonblank value. A recognized account with no operator ID can reach an unscoped read. Confirm intended Management/Subcontractor scope before reuse; fail closed in any new adapter. Do not change role grants automatically.
3. **Approval confidentiality/concurrency:** queue currently exposes payloads to any recognized caller; decisions/mark-applied do not constitute a transactional execution ledger. Keep write tools disabled; changes to existing access policy need explicit approval.
4. **Audit completeness:** `AuditService` logs and returns false on audit failure to preserve ordinary edits; before/after text is truncated to 400 characters. This alone cannot prove complete AI execution or support exact payload replay.
5. **Counts/risk semantics:** `RiskService` takes 12 groups before summing a headline and contains unused today calculation. Reuse authoritative rules, but distinguish total matches from displayed examples; do not present a capped list as the whole register.
6. **Multiple existing risk/KPI views:** `RiskService`, `MonitorRules`, `Notifications`, `KpiEngine` serve different questions. V1 must name its basis instead of silently replacing any definition. `KpiMeasures` currently defines six measures despite older comments saying eight.
7. **Latency:** `JobRegisterCache` stores the full parsed register for five minutes and invalidates on writes; some services still perform full scans. AI requests must be bounded, cancellation-aware and scope-filtered before provider transmission. Never send full `OperationJob.Data` to a model.
8. **Missing sources:** no invoice table/backend found; Billing KPI explicitly cannot be measured. Customer rules/doc versions and approved-rate validity must be verified before those agents promise answers.
9. **PII and prompt injection:** job JSON, files, notes and future messages contain untrusted text/PII. Do not log raw prompts or tool results by default; project allowed fields and preserve evidence IDs. No SQL/code/shell tools.
10. **Operational baseline:** the workspace was being edited concurrently. Earlier transient Cargo Receipt lint/import failures are not a final assessment of `9b31ab4`; rerun the whole baseline at the exact release SHA. No deployment readiness claim is made here.
11. **Migration/deployment coupling:** do not push server changes or run the API workflow until a migration/release proposal is reviewed.
12. **Knowledge and integrations:** document metadata/private Blob are reusable. ABS/CCS/Outlook/LINE pages currently probe endpoints not implemented in the API; Graph staff invitation support is not an Outlook mailbox integration.

## 10. Proposed additive database changes — not implemented

| Phase | Proposal | Compatibility / rollback |
| --- | --- | --- |
| B/C internal foundation | No business table migration needed for interfaces and read-only adapters; live tool execution still requires an audit strategy | Disable feature flags/remove new registrations; keep core routes untouched |
| D before enabling live agent runs | Add `ai_runs`, `ai_audit_logs` for actor/scope/agent/tool/risk/evidence/model/status/timing/usage | New tables only; old app ignores them; disable AI on rollback and retain audit rows |
| Conversation persistence when needed | Add `ai_conversations`, `ai_messages` with owner checks and retention | No hidden reasoning; do not store provider secrets; old app unaffected |
| H before any writes | Reuse `approvals`; add execution linkage/version/expiry only after an approved design | Do not create a duplicate approval authority; keep old states readable; retain history |

Use current SQL Server naming conventions and `nvarchar(max)` for validated structured JSON where appropriate; prefer typed indexed scalar columns for identity/status/time. Approvals already use PascalCase EF column conventions in places, so inspect the exact mapping instead of globally renaming columns. New migrations must be generated from the actual EF snapshot and reviewed alongside idempotent SQL, live applied migration history, lock impact and backup/restore strategy. Rollback should disable new features rather than drop tables holding audit records.

## 11. Proposed files — names are proposals, not existing modules

Add after checkpoint/credential and policy decisions:

- `server/Scmos.Api/Ai/AiOptions.cs`, `AiContracts.cs`, `IAiProvider.cs`
- `server/Scmos.Api/Ai/AgentRegistry.cs`, `ToolRegistry.cs`, `AgentOrchestrator.cs`
- `server/Scmos.Api/Ai/AiScopePolicy.cs`, `AiRequestValidator.cs`
- `server/Scmos.Api/Ai/Providers/OpenAiProvider.cs`, `MockAiProvider.cs`
- `server/Scmos.Api/Ai/Tools/OperationsReadTools.cs`
- `server/Scmos.Api/Endpoints/AiChatEndpoints.cs` for new chat/run contracts only
- `server/Scmos.Api/Data/AiRunEntities.cs` and additive EF migration in the audit phase
- `app/scmos/screens/AiControlTower.tsx`, typed client contracts under `app/scmos/ai/`, authenticated route entry later
- `tests/Scmos.Ai.Checks/` using existing .NET check style; deterministic tests and provider test doubles, no live credentials required
- Remaining `docs/ai/SCMOS_AI_ARCHITECTURE.md`, `SCMOS_AI_TOOLS.md`, `SCMOS_AI_AGENTS.md`, `SCMOS_AI_SECURITY.md`, `SCMOS_AI_DATABASE.md` as the relevant phases are designed

Existing files potentially modified, narrowly: `Program.cs` (DI/routes/config), existing `AiGateway.cs` and `AiPermissions.cs` (reuse/delegate, do not replace), `SupplierEndpoints.cs` (compatible AI route wiring only), `ScmosDbContext.cs` and snapshot in audit phase, `SCMOSApp.tsx`/`nav.ts` for Control Tower later. Do not change `Roles.cs`, authentication or production settings automatically. Keep document extraction working independently.

## 12. Recommended execution and checkpoint

```text
DISCOVERY COMPLETE — source and scoped Azure metadata
Architecture: Next.js 16.3.1 + React 19.2.6; .NET 10 Minimal API
Database: Azure SQL / EF Core; 48 mapped tables, 33 repository migrations
Authentication: App Service Web App Login / Entra + API capability/ownership checks
API: same-origin Next proxy -> existing endpoint/service/rule layers
Deployment: separate Web/API GitHub workflows -> Azure App Service
Existing SCMOS modules: see SCMOS_DOMAIN_MAP.md
Safe integration points: .NET AI adapters around existing services, authenticated UI shell
Proposed files to ADD: section 11
Existing files requiring modification: section 11
Database migrations required: NO for discovery; YES for persistent AI run audit/conversations later
Breaking changes expected: NONE by design; permission fixes and write execution require explicit decisions
```

Recommended order:

1. Phase A: accept this snapshot and resolve intended read scope for AI; verify live schema before a migration proposal.
2. Phase B: credential choice, provider abstraction/config, registries, validation, caller scope, bounded rate limits/timeouts, feature flags default off. Do not expose broad tools from the existing catalogue.
3. Phase C: read-only Operations adapter. Start with existing `search_shipment`/`query_shipments` names; allow only bounded reads and explanations with evidence. No duplicate business rules. Test auth, blank owner, carrier isolation, tool arguments, injected notes, cancellation and provider failure.
4. Phase D: persistent audit before enabling real user agent runs; record safe summaries, no hidden reasoning, no raw secrets. Audit writes are not business write tools.
5. Phase E: Control Tower composed from real services; unknown metrics render N/A. An AI outage must not stop existing pages.
6. Later F/G: Vendor + KPI + Rate; identify source/basis/date/currency/equipment and refuse unsupported approved-rate claims.
7. H/I: approval execution and low-impact write pilot only after explicit approval and transactional/idempotency tests. No automatic vendor assignment, commercial rate edit, invoice approval, CAR/PAR closure or external messages.

Phase B/C may proceed under the specification's additive authority only after their prerequisites are satisfied. This Phase A report is not permission to turn on a provider, broaden data access, push a production-triggering branch or execute a migration.
