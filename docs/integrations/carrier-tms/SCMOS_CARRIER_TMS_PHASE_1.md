# Carrier TMS API — Phase 1 record

Implemented 20 September 2026 · released as v2.7.52 (API run 35492107158, web run 35492120037) · scope as set in the [assessment](SCMOS_CARRIER_TMS_ASSESSMENT.md) §20: credentials, `/me`, `GET /assignments*`, Problem Details, correlation id, rate limit, checks. Reads only.

## PHASE COMPLETED — Phase 1

**Files created**

- `server/Scmos.Api/Rules/CarrierApi.cs` — the pure rules: key shape and hash, header, correlation id, rate-limit bucket, window, page, refusal vocabulary
- `server/Scmos.Api/Data/CarrierApiEntities.cs` — `CarrierApiClient` (+ `Configure`)
- `server/Scmos.Api/Data/Migrations/20260920013104_CarrierApiClients.cs` (+ Designer) — one additive table
- `server/Scmos.Api/Services/CarrierApiAuth.cs` — key → supplier, throttled `last_seen_at`
- `server/Scmos.Api/Endpoints/CarrierApiEndpoints.cs` — `/api/carrier/v1/*` and `/api/carrier-api/clients*`
- `server/Scmos.Api/Data/CarrierApiCheck.cs` — `--check-carrier-api` (60 checks)
- `app/scmos/screens/CarrierApiClients.tsx` — the Carrier API screen
- `docs/integrations/carrier-tms/SCMOS_CARRIER_TMS_API_V1.md` — the contract; this record

**Files modified**

- `server/Scmos.Api/Services/CarrierService.cs` — `ReadForAsync(Supplier)` extracted from `ReadAsync(AppUser)`; `CarrierJob` gains category, booking, plan time, plant, return yard, arrival, seal, respondedAt (additive, defaulted)
- `server/Scmos.Api/Program.cs` — `CarrierApiAuth` registration, the `carrier-api` rate-limit policy, `UseRateLimiter`, `MapCarrierApi`, the check
- `server/Scmos.Api/Data/ScmosDbContext.cs` (+ snapshot) — the `DbSet`
- `server/Scmos.Api/Rules/AuditActions.cs` — `Revoke`
- `app/scmos/nav.ts`, `navIcons.tsx`, `externalSystems.ts`, `app/SCMOSApp.tsx` — the screen under Integrations
- `tests/externalSystems.test.mjs` — five systems, not four
- `.github/workflows/api.yml` — the check runs on every release
- `docs/integrations/SETUP.md` — §5

**Existing components reused** — `CarrierService` (the boundary and the rows), `JobsRepository`/`JobRegisterCache` (through it), `supplier_aliases` name resolution, `Formats` (dates), `AuditService`/`AuditActions`, `IUserAccessor` + `Capability.ManageSuppliers` + `NeedsSecondFactor` for the department's side, `ApiResults` (unchanged for existing routes), `AddProblemDetails` (already registered), the LINE screen's card idiom.

**APIs added**

| Route | Who | What |
| --- | --- | --- |
| `GET /api/carrier/v1/me` | key | the credential and its supplier |
| `GET /api/carrier/v1/assignments` | key | offered + accepted rows, window, paged |
| `GET /api/carrier/v1/assignments/{jobKey}` | key | one row, or 404 |
| `GET /api/carrier-api/clients` | `ManageSuppliers` | the keys (never a hash), the base URL |
| `POST /api/carrier-api/clients` | `ManageSuppliers` | issue; the key returned once |
| `POST /api/carrier-api/clients/{id}/revoke` | `ManageSuppliers` | retire |

**Tools / agents added** — none (not an AI change).

**Database changes** — `carrier_api_clients` (id identity, supplier_id, name, client_id unique, key_hash unique, key_prefix, status, created_at/by, revoked_at/by, last_seen_at). No existing table or column touched. **Migration status** — applied to LocalDB on 20 Sep for the end-to-end probe; applied to production by the v2.7.52 API release (`--migrate`). `Down` drops only the new table.

**Security changes** — a new credential class (SCMOS-issued API key, SHA-256 at rest, shown once, revocable); a new rate-limit middleware active only on `/api/carrier/v1`; correlation id on the request trace. No capability added; no existing grant changed. The web proxy does not forward `Authorization`, so the API host is the only door for the key.

**Permissions added** — none. Issuing/retiring rides on `ManageSuppliers` (the LINE-room binding's capability).

**Tests added** — `--check-carrier-api` (60); the externalSystems test now pins five systems. Unchanged suites still pass: `--check-line`, `--check-capability`, `--check-signin`, 343 AI checks, 578 Node tests; API build with warnings as errors; tsc; eslint.

**Build result** — green at c266bd7 (v2.7.52).

**Verified end to end (LocalDB, 20 Sep)** — two keys for two suppliers: `/me` names the right supplier; `/assignments` for SANGJA lists 288 July rows and THREETRANS 3, disjoint; THREETRANS asking for a SANGJA job → 404 problem+json; bad `status`/window → 400; a wrong key of the right shape → 401; a retired key → 401; the admin list carries no hash or key; 22 keyless calls → 401 ×19 then 429 ×3 with `Retry-After: 60`; the screen issues, shows once, lists and retires.

**Known risks**

- `ReadForAsync` reads the whole register (as the portal does) and filters in memory; fine at today's size, and the rate limit keeps a polling TMS from making it a load. Revisit if the register grows past ~50k rows or a carrier polls every call.
- The keyless bucket is per remote address; callers behind one NAT share 20/min. Keyed callers are unaffected.
- `Request.Scheme` is not consulted for the shown base URL (App Service terminates TLS); `https` is assumed for any host but localhost. `CarrierApi__BaseUrl` overrides it.
- Keys are bearer secrets: a carrier that leaks one must have it retired on the screen; there is no automatic expiry in V1.

**Remaining tasks** — Phase 2 (accept / decline / truck details with `Idempotency-Key`), Phase 3 (status events queued for the owner's approval; REMARK fallback), Phase 4 (webhooks), Phase 5 (auto-apply behind a flag), an Entra client-credentials option for carriers with a tenant.

**Recommended next phase** — Phase 2, after one real carrier has read its rows on production with a key issued from the screen.
