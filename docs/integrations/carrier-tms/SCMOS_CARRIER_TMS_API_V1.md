# SCMOS Carrier TMS API — V1 contract

Phase 1 (reads) live from v2.7.52 · Phase 2 (accept, decline, truck) live from v2.7.53 · Phase 3 (status events, queued for the owner) live from v2.7.55 · 20 September 2026 · base URL `https://scmos-api-3936.azurewebsites.net/api/carrier/v1/` — **the API host, called directly.** The web host's `/api/*` proxy is for the browser and does not pass the `Authorization` header on; a call through it answers `401`. The Carrier API screen shows the exact base URL beside every key it issues.

The [assessment](SCMOS_CARRIER_TMS_ASSESSMENT.md) says why the API is shaped this way. This page is the contract a carrier's TMS is built against.

## Principles

- **One API for every carrier.** Which carrier a call is about is decided by the key's row in `carrier_api_clients`, on the server. Nothing in the request — no header, query or body — names a carrier.
- **A carrier sees its own rows and nothing else.** Another carrier's assignment is `404`, not `403`: its existence is not confirmed.
- **The same rows as the portal.** `CarrierService.ReadForAsync` serves the carrier's person and the carrier's TMS alike.
- **Nothing commercial** beyond the request's own `quotedPrice`, which the portal already shows the carrier. No rates, no costs, no selling prices.
- **Refusals are RFC 9457 Problem Details** with a stable `type` URN; never a stack trace.
- **Every answer carries `X-Correlation-Id`** — yours if you sent one (1–64 characters of `A–Z a–z 0–9 . - _`), otherwise one SCMOS made. It is written on every audit row the call produces.

## Credential

The department issues a key on the **Carrier API** screen (Integrations → Carrier API; needs `ManageSuppliers`). The key is shown once; SCMOS keeps only its SHA-256. Send it on every call:

```
Authorization: Bearer scmos_ck_<43 base64url characters>
```

A key is retired on the same screen; a retired key answers `401` from the next call. Rotate by issuing a second key before retiring the first.

## Limits

| | |
| --- | --- |
| Requests | 120 per minute per key (`429` with `Retry-After: 60` beyond that); 20 per minute per address without a valid key |
| Window | at most 92 days between `from` and `to`; default a week back to two weeks ahead |
| Page | `pageSize` 1–200, default 50 |

Do not poll faster than once a minute: SCMOS runs on a serverless database that sleeps between uses, and polling keeps it awake for nothing. Phase 4 adds webhooks for offers.

## Endpoints

### `GET /me`

```json
{
  "clientId": "ck_j6nm6jdhgy",
  "clientName": "SHORE TMS",
  "supplier": { "id": 19, "code": "SHORE", "name": "Shore Trans Asia Co., Ltd." },
  "limits": { "requestsPerMinute": 120, "maxPageSize": 200, "maxWindowDays": 92 },
  "correlationId": "…"
}
```

### `GET /assignments?status=&from=&to=&page=&pageSize=`

| Query | Meaning |
| --- | --- |
| `status` | `offered` (a request waiting for this carrier's answer), `accepted` (the register names this carrier as the truck's owner), or absent for both |
| `from`, `to` | work-date window, inclusive, `yyyy-MM-dd` or `dd/MM/yyyy` |
| `page`, `pageSize` | paging, sorted by work date then job code |

```json
{
  "items": [ { …assignment… } ],
  "page": 1, "pageSize": 50, "total": 7,
  "window": { "from": "2026-09-13", "to": "2026-10-04" },
  "correlationId": "…"
}
```

### `GET /assignments/{id}`

`{ "item": { …assignment… }, "correlationId": "…" }` — or `404 not-found` when the id is not an assignment this carrier is offered or holds.

## The assignment

The `id` is the job's register key — the same key the carrier portal's accept/decline routes, the LINE approvals and the audit trail name a job by.

| Field | Meaning |
| --- | --- |
| `id`, `jobKey` | the register key |
| `group` | `offered` or `accepted` |
| `jobCode` | the 12-digit job number (import/export) |
| `category` | `IMPORT`, `EXPORT`, `DELIVERY` — which status ladder applies |
| `booking` | the booking reference (import/export) |
| `customer`, `container`, `type`, `weight` | as the register holds them |
| `date`, `dateText` | the plan date as `yyyy-MM-dd` when the register's cell is a readable date, else `null`; `dateText` is always the cell as written (`"WAIT"` happens) |
| `planTime`, `pickupPlan` | the plan clock and the pickup plan text |
| `destination`, `plant`, `returnLoc`, `cyYard` | where to deliver, load, return the empty, collect |
| `status` | the register's controlled status code (`READY`, `DISPATCHED`, `DELIVERED`, …) |
| `truck` | `{ licence, driver, contact }` — empty strings until known |
| `seal` | the seal, on an export once loaded |
| `arrival` | `{ date, dateText, time }` once the arrival is written, else `null` |
| `request` | `{ id, quotedPrice, requestedAt, respondedAt }` on an `offered` row — the request waiting for this carrier's answer; `null` on an `accepted` row |

Dates inside `requestedAt`/`respondedAt` are ISO 8601 with offset (UTC).

## Writes (Phase 2)

Every write needs an **`Idempotency-Key`** header: 1–128 printable ASCII characters, unique per request (a UUID does). A retry with the same key and the same method, path and body is answered from SCMOS's ledger with `Idempotent-Replayed: true` — the acceptance is never run twice. The same key with a different request is `422 idempotency-key-reused`; the same request while the first is still being processed is `409 in-progress`. Answers are kept for 30 days.

Every write is audited under the key's name (`carrier-api:ck_…`, source `TMS`) with the previous value of each cell it changed.

### `POST /assignments/{id}/accept`

```json
{ "licence": "71-5111 ชบ.", "driver": "เต๋า ใจเงิน", "contact": "085-089-2487", "container": "TEMU5246902", "seal": "SL-9" }
```

`licence`, `driver`, `contact` are required — the acceptance is the truck, as in the portal. `container` and `seal` (an export's) are optional and go **only into empty cells**; a cell the department keyed with a different value is `409 conflict` with the conflicts named, and nothing is written. Values are held to the register's standard: a Thai plate (province optional), a Thai phone written back as `0XX-XXXXXXX`, a container of four letters and seven digits (a check digit that disagrees is written as sent, with a warning — the register may carry the same number from the booking).

What it does: the request waiting for this carrier is confirmed, every other carrier's open request on the job is cancelled, the job takes `trucker`, `licence`, `driver`, `contact` and status `SUPPLIER_CONFIRMED`.

```json
{
  "message": "รับงาน J25 แล้ว · 71-5111 ชบ. · เต๋า ใจเงิน",
  "jobKey": "J25", "status": "SUPPLIER_CONFIRMED",
  "written": { "trucker": "SHORE", "licence": "71-5111 ชบ.", "driver": "เต๋า ใจเงิน", "contact": "085-0892487", "status": "SUPPLIER_CONFIRMED", "container": "TEMU5246902", "seal": "SL-9" },
  "skipped": [], "warnings": [], "correlationId": "…"
}
```

Refusals: `404` when the id is not in this carrier's lists; `409` with `group: "accepted"` when the job is already this carrier's (accepted once) or `reason: "not-offered"` when the request is no longer pending; `400` with `problems[]` for fields that cannot be read.

### `POST /assignments/{id}/decline`

`{ "reason": "รถไม่ว่าง" }` — required, at most 400 characters. The request is answered `rejected`; who is asked next stays the operator's decision. Afterwards the job is in neither list, so a second decline is `404`.

### `PUT /assignments/{id}/truck`

Any of `licence`, `driver`, `contact`, `container`, `seal` (at least one), on a job this carrier **holds** (`accepted`). Each goes only into an **empty** cell: a cell already holding the value sent is `skipped`, one holding a different value is a `409 conflict` — and then nothing at all is written; the register's value stands and the person who keyed it changes it on the grid. A `COMPLETED` or `CANCELLED` job takes nothing (`409`, `reason: "closed"`).

```json
{ "message": "บันทึก เลขซีล S-77", "jobKey": "J26", "written": { "seal": "S-77" }, "skipped": ["contact"], "warnings": [], "correlationId": "…" }
```

## Status events (Phase 3)

A TMS reports what the truck did; **SCMOS does not write it.** The event is queued exactly as a haulier's LINE message is — judged by the same rule (forward only on the job's own ladder, "arrived" resolved by the job's category, the arrival clock only into empty cells) and **approved by the job's owner** from My Job or the LINE screen. Only a `note` is written at once, into the job's REMARK, dated.

### `POST /assignments/{id}/events`

```json
{ "type": "arrived", "at": "2026-09-20T10:20:00+07:00", "remark": "หน้าโรงงาน" }
```

| Field | Meaning |
| --- | --- |
| `type` | `dispatched` · `picked_up` · `loading` · `in_transit` · `arrived` · `delivered` · `container_returned` · `note` (case and dashes forgiven) |
| `at` | when it happened, ISO 8601 with an offset; now when absent; refused more than 10 minutes ahead or 7 days behind |
| `remark` | up to 400 characters; required for a `note`, carried on the queue's line otherwise |

What each type becomes on the job: `dispatched` → DISPATCHED, `picked_up` → PICKED_UP, `loading` → LOADING, `in_transit` → IN_TRANSIT, `delivered` → DELIVERED, `container_returned` → CONTAINER_RETURNED; `arrived` → the site rung of the job's category (DELIVERED on an import or domestic run, DISPATCHED on an export) **with `at` as the arrival date and time**, written only when those cells are empty.

Answers (`Idempotency-Key` required, as for every write):

| Status | `state` | Meaning |
| --- | --- | --- |
| 202 | `queued` | waiting for the owner; `from`, `to` and (for `arrived`) `arrival` say what approving writes |
| 200 | `already-there` | the job is at that rung already; nothing to approve |
| 200 | `remark-written` | a `note`, appended to REMARK as `dd/MM/yyyy HH:mm text` |
| 404 | — | the job is not this carrier's |
| 409 | — | the ladder refuses it: `reason` is `backwards`, `job-closed`, `job-held` or `not-on-ladder`; the event is kept, marked refused |
| 400 | — | the type, the clock or the remark cannot be read |

### `GET /assignments/{id}/events`

This carrier's events on the job, newest first, each with its `state`: `queued`, `applied` (the owner approved), `remark-written`, or the reason it was refused or set aside (`already-there`, `backwards`, `dismissed`…). Poll this — no faster than once a minute — to learn what became of an event; Phase 4 adds a webhook.

## Refusals

`Content-Type: application/problem+json`

| `status` | `code` / `type` | When |
| --- | --- | --- |
| 401 | `unauthorized` · `urn:scmos:carrier-api:unauthorized` | no key, a malformed key, a key SCMOS does not hold, a retired key |
| 404 | `not-found` · `urn:scmos:carrier-api:not-found` | the id is not this carrier's |
| 400 | `invalid-request` · `urn:scmos:carrier-api:invalid-request` | `status`, `from`, `to`, page values that cannot be read |
| 429 | `rate-limited` · `urn:scmos:carrier-api:rate-limited` | the minute's allowance is spent; `Retry-After: 60` |
| 409 | `conflict` · `urn:scmos:carrier-api:conflict` | the write does not fit the assignment's state: already accepted, no longer offered, closed, or a cell already holding another value (`conflicts[]`) |
| 409 | `in-progress` · `urn:scmos:carrier-api:in-progress` | the same Idempotency-Key is still being processed |
| 422 | `idempotency-key-reused` · `urn:scmos:carrier-api:idempotency-key-reused` | the same Idempotency-Key with a different request |
| 503 | `unavailable` | SCMOS could not answer; retry after the `Retry-After` if present |

```json
{
  "type": "urn:scmos:carrier-api:not-found",
  "title": "No such assignment for this carrier",
  "status": 404,
  "detail": "No assignment with this id is offered to or held by this carrier",
  "instance": "/api/carrier/v1/assignments/J25",
  "code": "not-found",
  "correlationId": "tms-2026-09-20_00042"
}
```

## What is not in V1 yet

Webhooks (Phase 4) and auto-apply for named carriers (Phase 5). The carrier portal and the LINE room keep working exactly as before; this API is a third door onto the same rows.
