# SCMOS controlled tools

Phase D, 2026-09-07. New runtime tool definitions live in `server/Scmos.Api/Ai/ToolRegistry.cs`; the existing `Rules/AiPermissions.cs` catalogue is still an upper policy bound. The existing `/api/ai/tools` and `/api/ai/invoke` contracts are unchanged.

| Contract | Required arguments | Limits | Current implementation |
| --- | --- | --- | --- |
| query_shipments | view: today or risk_today, limit: integer | 1–50 | Connected read-only Operations handler |
| search_shipment | query: string, limit: integer | query 1–120 nonblank characters; limit 1–50 | Connected read-only Operations handler |
| query_delays | limit: integer | 1–50 | Connected read-only Operations handler |

Each definition has name, description, owning specialist, required capability, low/read risk, input schema, nullable handler and `required-before-and-after` audit policy. No unrestricted parameter bag, SQL, owner override or dynamic reflection dispatch exists.

`AiInputSchema` emits strict JSON schema and validates the same field definitions server-side. Unknown/duplicate properties, omitted required values, wrong types, non-integral numbers, invalid bounds, oversized input and nested payloads fail. The provider accepts only one call to an offered tool with valid arguments, returning a proposal rather than executing it.

Authorization must satisfy all of: server user/scope, specialist capability, existing catalogue permission, registered schema/handler, owning specialist allowlist, tool capability, low-risk read classification and durable audit readiness. Phase D supplies the SQL adapter; schema/readiness is checked before provider/business source access and every start/end event must commit.

High-risk entries already in the legacy catalogue report `approval_required` in the new policy. This is **not** a persisted approval and does not execute anything, including for Administrator. Medium/write actions remain disabled; restricted and unknown tools are denied. `AI:WriteToolsEnabled=true` grants nothing.

The path is OperationsReadHandler → OperationsReadService → JobsRepository.ReadAnalysisAsync. The repository performs SQL ownership filtering, then strips raw JSON and unnecessary personal/free-text fields. The service repeats scope checks, uses MonitorRules.Judge and WorkspaceTabs.Delay, counts all matches and retains at most the requested rows. Data is returned to the user as typed evidence, not sent back to the provider for speculative prose. No second model call or recursive tool loop exists.

The risk_today window includes overdue work through two days after the Thai reference date; today means scheduled on that date only. Active My Job work excludes completed/cancelled work and DELIVERY. NoTruck still requires BOTH driver and plate missing; either arrival field removes Monitor risk. Invalid dates/malformed rows are visible counts, not quietly treated as valid dates or zero risks. The reference date is captured once per run.

Persistent before/after auditing is implemented in Phase D. Audit failure before a tool prevents the read; failure after the read withholds its result. Audit stores a server-generated tool ID, known tool/view/limit, low risk, not-required approval, counts and returned source keys, never raw arguments/search text. There is no in-memory production fallback, bypass flag, write handler or production activation. One run supports one tool; multi-tool loops need an explicit audit contract extension.
