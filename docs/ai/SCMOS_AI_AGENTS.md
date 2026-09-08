# SCMOS agent registry

Phase D, 2026-09-07. All specialists default off. Operations has three source-backed read handlers plus durable SQL auditing. Activation requires reviewed migration/deployment, working audit storage, provider configuration and existing feature flags. Other specialists remain unconnected.

| Specialist | Page hints | Additional existing capability |
| --- | --- | --- |
| operations-agent | operations, workspace | ViewDashboard |
| vendor-agent | vendors, suppliers | ManageSuppliers |
| rate-agent | rates, quotation | ViewRates |
| kpi-agent | kpi | ViewDashboard |
| incident-agent | incidents | ViewDashboard |
| billing-agent | billing | ViewRates; no invoice source exists |
| compliance-agent | compliance, training | ManageTraining |
| management-agent | management, dashboard | ViewDashboard |

All also require recognized server identity, a known internal role and a resolvable read scope. A carrier must wait for a separately reviewed carrier adapter. `ViewTeam` grants team scope; otherwise a nonblank server-resolved OperatorId is required. This is a conservative new-AI boundary, not a change to existing SCMOS role grants or screens. Future domain adapters may require stricter record/field projection.

The master router is deterministic inside `AgentRegistry.Resolve`: explicit registered agent wins, then known page, then default Operations only if no hint exists. Unknown values are refused. These identifiers are API page hints, not a promise that every existing UI menu currently sends them. Capability checks follow routing, so choosing an agent or page cannot grant its permissions.

Registry declarations include allowed tool names and purpose. Only three Operations contracts currently exist in the new ToolRegistry. Other allowed names refer to the existing catalogue and remain unavailable until reviewed schemas, handlers and audit are added. The Billing/Compliance tool lists are intentionally empty rather than invented.

OperationsAgent performs one provider-assisted tool selection followed by application-owned validation, authorization, audited read and deterministic answer composition. Its response exposes full totals, capped evidence, date window, data-quality counts, source keys and existing rule explanations. A provider response without an eligible tool produces clarification_required; its free-form numerical claims are never accepted. It does not auto-assign vendors, contact anyone or redefine risk/KPI.
