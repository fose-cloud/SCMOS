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
