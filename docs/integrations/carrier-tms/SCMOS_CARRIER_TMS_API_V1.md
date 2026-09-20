# SCMOS Carrier TMS API — V1 contract

Phase 1 (reads) · live from v2.7.52, 20 September 2026 · base URL `https://scmos-api-3936.azurewebsites.net/api/carrier/v1/` — **the API host, called directly.** The web host's `/api/*` proxy is for the browser and does not pass the `Authorization` header on; a call through it answers `401`. The Carrier API screen shows the exact base URL beside every key it issues.

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
| `request` | `{ id, quotedPrice, requestedAt, respondedAt }` when a supplier request exists, else `null` |

Dates inside `requestedAt`/`respondedAt` are ISO 8601 with offset (UTC).

## Refusals

`Content-Type: application/problem+json`

| `status` | `code` / `type` | When |
| --- | --- | --- |
| 401 | `unauthorized` · `urn:scmos:carrier-api:unauthorized` | no key, a malformed key, a key SCMOS does not hold, a retired key |
| 404 | `not-found` · `urn:scmos:carrier-api:not-found` | the id is not this carrier's |
| 400 | `invalid-request` · `urn:scmos:carrier-api:invalid-request` | `status`, `from`, `to`, page values that cannot be read |
| 429 | `rate-limited` · `urn:scmos:carrier-api:rate-limited` | the minute's allowance is spent; `Retry-After: 60` |
| 409 | `conflict` | reserved for Phase 2 writes |
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

## What is not in V1

Accept / decline / truck details / status events (Phase 2–3, with `Idempotency-Key` and the owner's approval), webhooks (Phase 4), auto-apply for named carriers (Phase 5). The carrier portal and the LINE room keep working exactly as before; this API is a third door onto the same rows.
