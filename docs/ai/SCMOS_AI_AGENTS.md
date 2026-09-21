# SCMOS agent registry

Phase D, 2026-09-07. All specialists default off. Operations has three source-backed read handlers plus durable SQL auditing. Activation requires reviewed migration/deployment, working audit storage, provider configuration and existing feature flags. Other specialists remain unconnected.

| Specialist | Page hints | Additional existing capability |
| --- | --- | --- |
| operations-agent (tools: query_shipments, search_shipment, query_delays, and since Phase 3 query_followup — [record](SCMOS_AI_PHASE_3.md)) | operations, workspace | ViewDashboard |
| vendor-agent | vendors, suppliers | ManageSuppliers |
| rate-agent | rates, quotation | ViewRates |
| data-agent (the specification's Data Agent; the former `kpi-agent` descriptor, connected in Phase 2 — [record](SCMOS_AI_PHASE_2.md)) | kpi, reports | ViewDashboard |
| communication-agent (the specification's Communication Agent, connected in Phase 4 — [record](SCMOS_AI_PHASE_4.md); tool `query_messages`; reads the LINE and mail ledgers, sends nothing) | line, mail, communications | ViewMailbox |
| document-agent (the specification's Document & Invoice Agent, connected in Phase 5 in the former billing descriptor's place — [record](SCMOS_AI_PHASE_5.md); tool `query_documents`; the Workspace's document reader is audited under it as `extract_document`; opens no file, approves nothing) | documents, verification, compliance, billing | UploadDocuments |
| incident-agent | incidents | ViewDashboard |
| compliance-agent | compliance, training | ManageTraining |
| management-agent | management, dashboard | ViewDashboard |

All also require recognized server identity, a known internal role and a resolvable read scope. A carrier must wait for a separately reviewed carrier adapter. `ViewTeam` grants team scope; otherwise a nonblank server-resolved OperatorId is required. This is a conservative new-AI boundary, not a change to existing SCMOS role grants or screens. Future domain adapters may require stricter record/field projection.

The master router is deterministic inside `AgentRegistry.Resolve`: explicit registered agent wins, then known page, then default Operations only if no hint exists. Unknown values are refused. These identifiers are API page hints, not a promise that every existing UI menu currently sends them. Capability checks follow routing, so choosing an agent or page cannot grant its permissions.

Registry declarations include allowed tool names and purpose. Only three Operations contracts currently exist in the new ToolRegistry. Other allowed names refer to the existing catalogue and remain unavailable until reviewed schemas, handlers and audit are added. The Billing/Compliance tool lists are intentionally empty rather than invented.

OperationsAgent performs one provider-assisted tool selection followed by application-owned validation, authorization, audited read and deterministic answer composition. Its response exposes full totals, capped evidence, date window, data-quality counts, source keys and existing rule explanations. A provider response without an eligible tool produces clarification_required; its free-form numerical claims are never accepted. It does not auto-assign vendors, contact anyone or redefine risk/KPI.
