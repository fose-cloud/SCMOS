# Phase 1A — Shared contracts, Operations compatibility

Implemented locally on 14 September 2026 against baseline `a62d35a` (v2.6.4).
This is an implementation/test record, not a production deployment report.

## Changes

- `AiActionLevel` separates action intent (Read, Recommend, ApprovalRequired,
  Restricted) from existing `AiRisk`. The default is Unspecified, not Read.
  These values do not enable recommendations, approvals or writes.
- Existing `ToolRegistry` entries now carry internal `AiToolPolicy`: contract
  version 1, action Read, source `operation_jobs`, typed `OperationsAnswer`,
  maximum 50 evidence rows, server-resolved team/operator scope and a reference
  to the existing `AI:TimeoutSeconds` deadline setting. Existing capability,
  risk, input schema and required-before-and-after audit fields are retained.
- One evidence-limit constant now supplies metadata, input schema and reader
  validation. It does not cap the source scan or change full-result totals.
- `IAgentExecutor<OperationsExecution>` is implemented by `OperationsAgent` and
  consumed by `AgentOrchestrator`. DI resolves it to the same scoped instance,
  not a second runtime. The orchestrator still routes only Operations in live mode.
- Internal policy is excluded from JSON serialization. Public status/chat
  envelopes and actual SDK function declarations retain their existing fields.

Metadata describes the reviewed contracts; it does not replace or generalize
`AiPermissionPolicy`, input validation or audit enforcement. A generic policy
executor, output-schema validator and guarded multi-agent dispatch are deferred
to later slices. No new agent or tool handler is connected by this change.

## Verification

- AI checks: **250 passed** (baseline 229; 21 added), using in-memory providers,
  data/audit fixtures and loopback HTTP only; no live model calls or production data.
- Existing checks cover unknown tools/forged scope, role isolation, audit failure,
  cancellation, status, admin-only switch, emergency stop and disabled write access.
- New checks cover policy descriptors, unchanged evidence cap/schema, actual SDK
  function envelope, public JSON envelopes, typed execution and same-instance DI.
  Existing successful Operations evidence/audit assertions now exercise the interface.
- `npm test`: **485 passed**.
- API Release build: passed, zero warnings/errors.
- `git diff --check`: passed.

The AI test project retains its pre-existing EF1002 warning at
`tests/Scmos.Ai.Checks/AuditChecks.cs:288`; optional SQL integration tests were not run.
No browser/live-account certification is claimed for this local slice.

## Boundaries and next gate

No migration, database mutation, credential read/change, provider replacement,
production switch change, permission expansion, push or deployment. Existing
`next-env.d.ts` worktree changes are preserved and are not part of Phase 1A.

Next proposed slice is **1B: guarded dispatch**, as described in
[the implementation plan](SCMOS_AI_IMPLEMENTATION_PLAN.md). Do not infer approval
for new handlers, business writes or production deployment from completion of 1A.
