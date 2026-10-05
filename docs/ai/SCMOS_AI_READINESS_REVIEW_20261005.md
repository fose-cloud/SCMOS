# AI readiness review SCMOS-AI-RR-2026-10-05

**Decision:** approved, with a time-boxed acceptance of the shared runtime.
**Decided by:** K.nattikorn-fos@hotmail.com — Staff AD-01, Administrator, the policy's primary owner.
**Date:** 5 October 2026. **Acceptance ends:** 31 December 2026 23:59:59 Bangkok (`+07:00`), enforced in code.
**Policy:** `scmos-permission-7` (previous `scmos-permission-6-candidate`).

This record is the evidence `approvalReference` and each `runtimeIsolationApproval` point to. It
records what was reviewed, what was accepted and why, and what has to happen before the end date.
It grants nothing the policy file does not grant.

## Scope

Approved: the nine agents with per-agent bounds — `operations-agent`, `vendor-agent` (Carrier),
`data-agent`, `document-agent`, `management-agent`, `engineering-agent`, `otd-agent`,
`validation-agent`, `sre-agent`.

Not approved, still refused: `rate-agent`, `incident-agent`, `compliance-agent` (not connected) and
`communication-agent`, `booking-agent` (no bounds). Each needs its own bounds and review.

No approved agent holds a write, send or approval permission: every grant is Read, Analyze or
Draft. The Operations write adapter exists in code but its tool (`update_shipment`) is not granted.
The Annual Evaluation summary's tool (`summarize_evaluation`) is not granted to the Management
Agent either, so that summary stays unavailable until a separate grant is reviewed.

## What the Constitution asks, against what runs (sections 3, 6, 17, 18, 40)

| Requirement | Today |
|---|---|
| Isolated runtime per agent | All agents run in the API process (`scmos-api-3936`, one App Service) |
| No agent holds a database credential | Agents reach SQL only through finite, policy-checked operations (`AiPassRepository`, read services); the process connects with the SQL server admin login `scmosadmin` |
| Network default deny | No VNet integration; outbound internet is open. Destinations are checked in code (`networkAllowList`: GitHub for Engineering/SRE) |
| No shared super credential | One OpenAI key serves every agent (and the LINE/report paths); one system-assigned managed identity |

The decision accepts this gap until the end date, for agents that can only read, analyse and draft.

## Compensating controls relied on

- Policy gateway, default deny: unknown agent, tool, action, identity or approval is refused; every
  allow and deny is written to `ai_authorization_logs` before dispatch.
- Per-agent manifests with tool allowlists, data scopes, risk ceilings and fixed owners (AD-01,
  AM-01), rechecked live against the staff directory on every run.
- No direct SQL, no write or send grant; external text and model output are treated as data.
- Money: USD 0.20 a day and USD 10 a month for the whole fleet, set aside before each model call;
  per-agent daily and monthly amounts and request counts (`scmos-permission-6-candidate`).
- Kill switches: `AI__Enabled`, per-agent switches in the Control Tower, tool and group switches,
  write and external-communication stops.
- Durable audit the agents cannot edit; breaker and health from the audit.

## Evidence (Constitution section 38)

Offline 1,391 checks and the focused SQL suites (1,468, twice) on 5 October 2026 cover policy and
permission tables, tool allowlists, prompt-injection handling, unauthorised access and privilege
chaining, the approval contract, failure and fail-closed paths, cost reservation and shared limits,
audit durability, and kill switches. New with this review: only the nine reviewed agents can start;
the acceptance ends by itself at the end date; every Production acceptance carries an end; no
approved agent holds a write, send or approval permission.

## Conditions

1. Before 31 December 2026 the decision is reviewed again. Without a new review every approved
   agent is refused with `runtime_isolation_review_expired` from 1 January 2027.
2. Any new agent, tool, grant, data scope, integration or higher risk level is a new review
   (Constitution sections 36–37); a version update grants nothing.
3. Recommended before the next review, to narrow the accepted gap: a least-privileged SQL login
   for the API in place of `scmosadmin`; a separate OpenAI project or key for the agents with a
   provider-side spending cap; restricted egress; `httpsOnly` on the API.
4. Stopping is always available to the owners: the Control Tower switches, or `AI__Enabled`.
