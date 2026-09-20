# Carrier TMS API — Phase 4 record

Implemented 20 September 2026 · released as v2.7.56 (API run 35505752612, web run 35505768655) · scope as set in the [assessment](SCMOS_CARRIER_TMS_ASSESSMENT.md) §20: outbound webhooks (offered, cancelled, event decided), retries, delivery ledger, signatures.

## PHASE COMPLETED — Phase 4

**Files created**

- `server/Scmos.Api/Rules/CarrierWebhooks.cs` — the pure rules: the events, URL rules (https, public host, no private address), secret and signature, the retry schedule
- `server/Scmos.Api/Data/CarrierWebhookEntities.cs` — `CarrierWebhook`, `CarrierWebhookDelivery`
- `server/Scmos.Api/Data/Migrations/20260920102957_CarrierWebhooks.cs` (+ Designer) — two additive tables
- `server/Scmos.Api/Services/CarrierWebhookQueue.cs` — queues an event to every subscribed webhook of the supplier(s) a carrier name resolves to; never fails the caller
- `server/Scmos.Api/Services/CarrierWebhookDispatcher.cs` — the hosted sender: due deliveries every 20 s, signed POST, 10 s timeout, retries, dead letters, the offer's assignment snapshot at send time
- this record

**Files modified**

- `server/Scmos.Api/Services/WorkflowService.cs` — `assignment.offered` after a supplier request; `assignment.cancelled` after a cancelled / no-response answer (constructor gains the queue)
- `server/Scmos.Api/Services/CarrierService.cs` — `assignment.cancelled` for every other carrier whose ask closes on acceptance (constructor gains the queue)
- `server/Scmos.Api/Endpoints/LineReviewEndpoints.cs` — `event.decided` after a TMS row is applied or dismissed
- `server/Scmos.Api/Endpoints/CarrierApiEndpoints.cs` — `GET/POST /webhooks`, `DELETE /webhooks/{id}`, `POST /webhooks/{id}/test`, `GET /webhooks/{id}/deliveries`; the department's listing carries the suppliers' webhooks
- `server/Scmos.Api/Program.cs` — the queue, the named HttpClient, the dispatcher
- `server/Scmos.Api/Data/ScmosDbContext.cs` (+ snapshot), `Data/CarrierApiCheck.cs` (27 checks added, 134 total)
- `app/scmos/screens/CarrierApiClients.tsx` — the suppliers' webhooks under the keys, a failing one in amber
- `docs/integrations/carrier-tms/SCMOS_CARRIER_TMS_API_V1.md`, `docs/integrations/SETUP.md`

**Existing components reused** — the supplier request lifecycle (`WorkflowService`, `CarrierService`) as the source of offers and withdrawals; the LINE approval routes as the source of decisions; `CarrierService.ReadForAsync` + `CarrierApiEndpoints.Assignments` for the offer's snapshot; `supplier_aliases` for name → supplier; `IHttpClientFactory` as the other outbound clients use it; the Phase 1 filter, rate limit, idempotency and problem shape.

**APIs added**

| Route | What |
| --- | --- |
| `GET /api/carrier/v1/webhooks` | this supplier's webhooks (never the secret) |
| `POST /api/carrier/v1/webhooks` | register; the secret once; at most 5 active per supplier; `Idempotency-Key` |
| `DELETE /api/carrier/v1/webhooks/{id}` | retire (disabled, kept; pending deliveries close out) |
| `POST /api/carrier/v1/webhooks/{id}/test` | a `ping` delivery |
| `GET /api/carrier/v1/webhooks/{id}/deliveries` | the last 50 deliveries and what the TMS answered |

**Tools / agents added** — none.

**Database changes** — `carrier_webhooks` (supplier, client, url, secret, events, status, created/disabled, last delivery, last status, last error, failed-in-a-row) and `carrier_webhook_deliveries` (webhook, type, key, payload, attempts, next attempt, status, last status/error, created/delivered, correlation id; unique per webhook+type+key). **Migration status** — applied to LocalDB for the probe; applied to production by the v2.7.56 API release. `Down` drops only the two new tables.

**Security changes** — SCMOS's server now makes outbound calls to carrier-chosen URLs: only https on public hosts, never a private, link-local, loopback or metadata address (loopback allowed only in Development), never credentials in the URL; 10 s timeout; a fixed User-Agent. Every delivery is signed (HMAC-SHA256 over timestamp.body) so a TMS can refuse forgeries and replays. The signing secret is the one secret SCMOS holds in plain (it must sign with it); shown once, never logged, not on any listing. Webhooks are scoped to the key's supplier; another carrier's are 404.

**Permissions added** — none.

**Tests added** — 27 checks in `--check-carrier-api` (134 total). Unchanged suites pass: `--check-line`, `--check-capability`, `--check-delegation`, 343 AI checks, 578 Node tests (the new table sits in the shared scroll box, as the table test insists); build with warnings as errors; tsc; eslint.

**Build result** — green at a4b7bd4 (v2.7.56).

**Verified end to end (LocalDB, 20 Sep, against a stand-in TMS on localhost that verified every signature)** — ftp / private / credentialed URLs and an unknown event → 400; register → 201 with the secret once, absent from every later listing; the other carrier lists none and cannot retire it (404); a ping delivered (204) with a valid signature; a supplier request from the operator → `assignment.offered` delivered with the assignment snapshot (`offered`, quoted 6,100); the operator's cancellation → `assignment.cancelled` with the request id and reason; a TMS event approved → `event.decided` (`applied`, `CONTAINER_RETURNED`, by SV-01); a second webhook answering 500 → its deliveries `pending`, attempt 1, next try a minute on; retired → 200 `disabled`, its ping refused (409), its pending deliveries closed out `dead — webhook disabled` on the next pass; the department's listing shows both, the failing one with `failedInARow: 2`.

**Known risks**

- DNS is not resolved at registration: a public hostname that resolves to a private address at send time is not caught (DNS rebinding). The dispatcher runs on the App Service, whose outbound network is Azure's; a follow-up may pin the resolved address at send time.
- A webhook that fails forever keeps costing one delivery attempt per event for ~15 hours each, and the row stays active until the carrier retires it; the screen shows the count. Auto-disable after N dead deliveries is a follow-up if it becomes noise.
- The offer's snapshot reads the whole register once per supplier per pass (as the portal does); fine at today's size.
- Deliveries carry the job's customer name and the request's quoted price — what the portal already shows the carrier; nothing commercial beyond that.

**Remaining tasks** — Phase 5 (auto-apply for named carriers behind a flag); the bell/badge wording for TMS rows; a resolved-address check at send time.

**Recommended next phase** — Phase 5, once a carrier's TMS is receiving deliveries on production.
