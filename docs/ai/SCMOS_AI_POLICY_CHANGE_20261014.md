# AI policy change SCMOS-AI-PC-2026-10-14

**Decision:** option A of the bounds proposal for the five agents left unbounded by
[SCMOS-AI-RR-2026-10-05](SCMOS_AI_READINESS_REVIEW_20261005.md): none of them is bounded or enabled,
and the two scheduled passes lose their grants.
**Decided by:** K.nattikorn-fos@hotmail.com — Staff AD-01, Administrator, the policy's primary owner.
**Date:** 14 October 2026.
**Policy:** `scmos-permission-8` (previous `scmos-permission-7`).

This change only narrows. It grants nothing, sets no budget, and changes no owner, scope of an
approved agent, or isolation acceptance. The nine bounded agents and their acceptance until
31 December 2026 stand as recorded in SCMOS-AI-RR-2026-10-05.

## Evidence (Production, read through the application's own routes)

| Agent | What it would do | Measured |
|---|---|---|
| `rate-agent`, `incident-agent`, `compliance-agent` | No tool is connected | No run, ever |
| `communication-agent` — scheduled pass (`scan_communication`) | Draft a carrier reminder per job, every 15 minutes | 995 drafts between 29 Sep and 3 Oct 2026 (up to 361 a day); every one answered *not sent* by a person; none sent |
| `communication-agent` — chat (`query_messages`) | Read what carriers reported | 2 runs, both on 21 Sep 2026 (live tests) |
| `booking-agent` — paste (`draft_booking`) | Read pasted booking text into the add-job form | No run, ever |
| `booking-agent` — mail pass (`scan_booking`) | Read booking mail | No source: the Outlook connection was dropped on 5 Oct 2026 |

The pass has produced nothing since 4 October 2026, when `scmos-permission-6-candidate` refused every
agent without bounds.

## What changed

- `communication-agent`: `scan_communication` and the `CommunicationDraft` grant removed; data scope
  `systemPass` false. It keeps `query_messages` / `CommunicationRead`, unbounded and therefore refused.
- `booking-agent`: `scan_booking` removed; data scope `systemPass` false. It keeps `draft_booking` /
  `BookingCreateDraft`, unbounded and therefore refused.
- `rate-agent`, `incident-agent`, `compliance-agent`: unchanged — no tool, no budget, refused.
- The tool names stay registered in the policy (the code still names them) and are granted to no agent.

## What was not done, and what would reopen it

The proposal also offered bounds for Communication chat (10 a day, USD 0.0015 a model call, 0.02 a day,
0.30 a month) and Booking paste (10 a day, USD 0.02 a model call, 0.06 a day, 0.60 a month). Neither
was chosen. Enabling either, or restoring a pass, is a new review (Constitution sections 36–37): bounds,
an isolation acceptance, a new policy version and the section 38 checks.

## Checks

`tests/aiPolicy.test.mjs` pins the version, the reference and the narrowed manifests;
`Scmos.Ai.Checks` refuses both passes against the Production policy (1,392 offline and 1,469 with the
isolated LocalDB suites, 14 Oct 2026). The offline fixture keeps both passes as test-only grants so the
component checks of their mechanics still run.
