# Phase 1B — guarded dispatch, first increment

Local implementation only; not pushed or deployed.

- Extracted QueryPolicyGuard: existing role/scope/audit checks plus explicit
  read-action metadata, reviewed contract version/source/output type and row cap.
- Extracted ToolExecutor: revalidates the call immediately before reading and
  derives scope from the authenticated server identity, never tool arguments.
- Request-owned, atomic one-tool-call budget. Failures do not refund a consumed
  budget. Existing orchestrator cancellation/deadline remains unchanged.
- OperationsAgent retains mandatory durable audit before/after execution and
  existing clarification when the provider selects zero or multiple tools.
- No new tools, model/provider/API change, write switch change, SQL migration,
  business mutation, or expansion of agent readiness.

This is the dispatch extraction increment, not completion of all Phase 1B:
the next increment is explicit deterministic intent/clarification routing and
per-agent readiness projection over connected tools. The current model selector
and AgentRegistry routing remain intact to preserve compatibility.

Verification uses fixture providers and source data, without live API requests.
The Agents skill guided preservation of the existing runtime and bounded tools;
no hosted Agents API migration was introduced.

## Clarification increment — verified 15 September 2026

The write-command UI now routes supported Thai change prefixes (for example
เลื่อนงานพรุ่งนี้) to the read-only interpretation endpoint. Missing fields,
duplicate fields and invalid date/time/assignee/status return bounded question
codes. The client renders only allowlisted questions, never raw server text.
No relative date, job key or assignee is inferred. The user must resubmit the full
explicit command; cross-message conversational memory is not implemented.
Unrecognized free-form phrasing still uses the existing read-only model route.

Verification: 333 AI/Operations/audit checks including isolated LocalDB and
517 frontend tests passed; TypeScript and scoped lint passed. Clarification
requests create no proposal, job change or edit audit. No live provider call or
production business-data test was made. This verification did not push/deploy.
## Per-agent readiness display — 15 September 2026

Added a conservative per-agent display projection over the existing server status
contract. Disabled/control-unavailable/emergency/not-connected/configuration/
provider/mock/audit/tool-runtime states are distinguished with Thai explanations.
Only the current connected Operations executor can display ready; future registry
descriptors cannot inherit its readiness. Mock is not live-ready, and write flags
do not grant readiness. The server continues to revalidate each request.

No new API envelope, provider call, capability, write switch or database change.
This is not a live connectivity probe or a replacement for server authorization.
Frontend tests: 520 passed; TypeScript and scoped lint passed.
General natural-language planning and conversational memory remain out of scope;
the clarification increment requires full explicit-command resubmission.
No push/deploy was performed for this display increment.
