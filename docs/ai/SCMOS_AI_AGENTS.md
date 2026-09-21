# SCMOS agent registry

Originally Phase D, 2026-09-07; updated 21 September 2026. The currently connected read specialists are Operations, Data, Communication, Document & Invoice, and the Phase 6 Engineering reader. Each requires its server flag, role permission, provider and durable audit. The Engineering flag defaults off; listing an agent does not activate it.

| Specialist | Page hints | Additional existing capability |
| --- | --- | --- |
| operations-agent (tools: query_shipments, search_shipment, query_delays, and since Phase 3 query_followup — [record](SCMOS_AI_PHASE_3.md)) | operations, workspace | ViewDashboard |
| vendor-agent | vendors, suppliers | ManageSuppliers |
| rate-agent | rates, quotation | ViewRates |
| data-agent (the specification's Data Agent; the former `kpi-agent` descriptor, connected in Phase 2 — [record](SCMOS_AI_PHASE_2.md)) | kpi, reports | ViewDashboard |
| communication-agent (the specification's Communication Agent, connected in Phase 4 — [record](SCMOS_AI_PHASE_4.md); tool `query_messages`; reads the LINE and mail ledgers, sends nothing) | line, mail, communications | ViewMailbox |
| document-agent (the specification's Document & Invoice Agent, connected in Phase 5 in the former billing descriptor's place — [record](SCMOS_AI_PHASE_5.md); tool `query_documents`; the Workspace's document reader is audited under it as `extract_document`; opens no file, approves nothing) | documents, verification, compliance, billing | UploadDocuments |
| engineering-agent (Phase 6 — [record](SCMOS_AI_PHASE_6.md); tools `query_repository` (fixed public GitHub metadata) and, behind its own switch, `read_source` (a bounded, read-only read of the repository's source with the model's analysis labelled as its own); no run, edit, commit or deploy exists) | engineering | AdministerData (Administrator only) |
| incident-agent | incidents | ViewDashboard |
| compliance-agent | compliance, training | ManageTraining |
| management-agent | management, dashboard | ViewDashboard |

All also require recognized server identity, a known internal role and a resolvable read scope. A carrier must wait for a separately reviewed carrier adapter. `ViewTeam` grants team scope; otherwise a nonblank server-resolved OperatorId is required. This is a conservative new-AI boundary, not a change to existing SCMOS role grants or screens. Future domain adapters may require stricter record/field projection.

The master router is deterministic inside `AgentRegistry.Resolve`: explicit registered agent wins, then known page, then default Operations only if no hint exists. Unknown values are refused. These identifiers are API page hints, not a promise that every existing UI menu currently sends them. Capability checks follow routing, so choosing an agent or page cannot grant its permissions.

Registry declarations include allowed tool names and purpose. Connected contracts are the four Operations reads, Data's `query_kpi`, Communication's `query_messages`, Document & Invoice's `query_documents` and audited `extract_document`, and Engineering's `query_repository`. Other allowed names remain unavailable until reviewed schemas, handlers and audit are added.

OperationsAgent performs one provider-assisted tool selection followed by application-owned validation, authorization, audited read and deterministic answer composition. Its response exposes full totals, capped evidence, date window, data-quality counts, source keys and existing rule explanations. A provider response without an eligible tool produces clarification_required; its free-form numerical claims are never accepted. It does not auto-assign vendors, contact anyone or redefine risk/KPI.
