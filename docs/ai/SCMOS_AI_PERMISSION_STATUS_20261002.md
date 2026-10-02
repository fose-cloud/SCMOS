# AI permission enforcement — implementation status

Date: 2 October 2026. Branch: `codex/ai-governance-constitution`.
Status: **local implementation in progress; NOT Production-ready or fully accepted**.

This record follows the user's Permission Enforcement prompt and the original
[Constitution](SCMOS_AI_GOVERNANCE_CONSTITUTION_V1.md). It does not approve policy,
grant privileges, certify isolation or authorize deployment. The earlier
[adoption assessment](SCMOS_AI_GOVERNANCE_ADOPTION_20261002.md) describes the
pre-implementation baseline; this record describes subsequent code changes.

## Delivered boundary

The existing `Services/AiGateway` now implements `IAiPolicyGateway`. There is
no new agent, replacement orchestrator or parallel permission gateway. All
fourteen registered IDs remain, including `document-agent` and `vendor-agent`.

The embedded `Ai/Policy/permission-matrix.json` contains typed, conservative
read/analyze/draft contracts, not an approved new execution matrix. Missing
actions default to Forbidden. All manifests currently report
`CONFIGURATION_REQUIRED`: approval reference, named primary/fallback human
owners, budgets and runtime-isolation review are absent. The candidate refuses
live AI execution; existing manual SCMOS operations are not policy-gated.
Deploying this candidate would disable currently connected live AI features.

Chat orchestration AND direct calls to the seven connected chat executors
authorize before invoking the provider. Registered handlers always carry a
policy decorator, even when a caller omits the gateway (then they deny).
Unknown/malformed/not-offered model tool requests, including rejected batches,
reach the centralized denial/security audit. Batches beyond eight calls get an
aggregate overflow denial in addition to the first eight individual records.
Scheduled passes, extraction, booking drafts, billing AI and the Operations
proposal/confirmation path also have entry gates. Validation/authentication
refusals before an AI authorization request are still distinct from these
authorization-event records; a complete security-attempt inventory is pending.

Cross-agent reads carry origin, original action and a bounded delegation chain.
Both origin and specialist must grant the requested action/tool/API/scope;
restriction merging cannot remove an origin's human-approval requirement.
Network destinations must be permitted by both manifests. Multi-hop and cyclic
delegation deny. The candidate Management manifest does not grant specialist
tools: its legacy collaboration is therefore blocked until explicit review.

## Prompt phase coverage

| Phase | Evidence / remaining work |
| --- | --- |
| 0 | Pre-edit [implementation map](SCMOS_AI_PERMISSION_IMPLEMENTATION_MAP.md), existing architecture and fourteen-agent inventory. Production configuration was not inspected/certified. |
| 1–3 | Separate permission enum, explicit precedence, 69 typed actions, immutable existing IDs. Persisted autonomy/action-level meanings are unchanged. |
| 4 | Fourteen immutable manifests and fail-closed readiness checks; operational configuration and final approved grants still required. |
| 5–6 | Existing gateway extended; typed requests and structured Allow/Deny/HumanApprovalRequired decisions with reasons, correlation, policy version and audit ID. |
| 7 | Identity/tool/API/RBAC/scope/risk/delegation/configuration/governance/audit gates; serializable cost reservation. Exact resource/customer/carrier/date scope, all runtime quotas and action-specific write adapters are not complete. |
| 8 | Explicit restriction precedence, including delegated human-approval restriction; no numeric ordering grants. |
| 9 | **Incomplete.** No general action/resource/before-after/single-use approval consumption adapter. Arbitrary approval IDs deny and no new execution adapter is enabled. Existing approval queue retained, not declared equivalent to the requested general workflow. |
| 10 | Existing human approval protections preserved; Operations additionally rejects the requester and aliases sharing the requester's OperatorId. Real approved-write SQL regression remains pending. |
| 11 | Non-communication agents cannot obtain direct external-send grants; external actions are off. **CommunicationRequest routing/re-authorization and reviewed send adapters are not implemented.** |
| 12 | Origin/specialist intersection, restriction merge, origin network check, chain audit; no automatic trust or multi-hop escalation. |
| 13 | **Incomplete structural isolation.** There is no SQL tool or model credential projection, but `AgentScanner` and `BookingMailPass` still receive `ScmosDbContext`. Extracting their database work behind existing application/data services and proving the boundary remain required. |
| 14 | Engineering reads remain bounded, no Production write/deploy tool; isolated development runtime/credential/egress evidence is not provided. A manifest review reference is not physical isolation. |
| 15 | SRE remains read-only. Approved runbooks, their bounds and human-approved Production changes remain unimplemented/unconfigured. |
| 16 | Append-only authorization table in the existing durable audit writer; denials included; failed/missing audit refuses dispatch. New rows include policy version, scope, chain, risk and reserved cost. Authorization rows say `not_executed`; actual token/tool/result settlement and complete linkage to execution telemetry remain pending. No WORM/database-role isolation certification. |
| 17 | Security flags for unknown actions/tools/agents, invalid tool input, forbidden access, SQL/audit/policy modification, chaining and network violations. Repeated-denial detection, automatic suspension, credential-attempt classification and alert delivery remain pending. |
| 18 | Existing agent/global governance plus restrictive tool/group/write/external configuration switches. Database tool rows may revoke only. Invalid revocation configuration denies. No new AI permission-management endpoint. |
| 19 | Automated default-deny/scope/chain/missing-gateway/audit/budget/switch tests. Rejection of supplied approval IDs is tested; this is **not** proof of a complete expiry/resource/single-use execution workflow. |
| 20 | Independent expected table checks all 14 × 69 = 966 candidate matrix rows. Final human-approved matrix and executable-adapter review remain required. |
| 21–22 | Embedded versioned JSON, schema validation, unknown/duplicate/numeric-alias/unsafe grant rejection, change metadata and audit policy version. Candidate ApprovalReference remains null. Full execution settlement/version linkage still pending. |
| 23 | Read-only `/api/ai/policies` report and Governance panel with all fourteen identities, grant categories, configuration status, budgets and execution/security timestamps. Parser/build/lint tested; signed-in browser visual QA remains pending. |
| 24 | Control Tower cannot override the candidate, scope, audit or gateway checks. Direct executor/handler calls do not bypass the gateway. |
| 25 | Existing deterministic rules and application services reused; regression checks pass. This does not certify every Production integration/business workflow. |

## Budget and audit semantics

SQL serializable transactions reserve `MaxReservationCost` before dispatch and
enforce daily/monthly cost limits and a daily count of positive reservations.
The count currently measures authorized dispatch boundaries, **not unique user
requests**: orchestration, executor and handler checks can each reserve. Failed
or abandoned calls are not refunded. Six-decimal nonzero amounts are required;
canonical hashing prevents SQL decimal scale from breaking idempotent retries.
Identical audit replay reserves once; changed request replay is rejected.

This is conservative reservation accounting, not actual provider billing or a
complete budget runtime. Per-agent distributed concurrency, input/job token
caps, aggregate API/tool limits, deadlines for every direct execution path and
actual-cost settlement must be completed before activation. Unknown agent/tool
identifiers are hashed in audit; raw prompts/arguments/human profiles are not
stored in the authorization row.

## Verification

- API/AI checks: **1,339 passed**, including an independent check of 966 matrix
  rows: `dotnet run --project tests/Scmos.Ai.Checks -- --permission-local-db`.
  The isolated permission SQL suite creates a uniquely named LocalDB test
  database, tests real authorization writes, idempotency, conflicting replay,
  eight concurrent budget reservations, persisted denials and stored tool
  revocation, then removes only that scratch database. No Production connection
  or live OpenAI call is used.
- Existing suites requiring `--local-db` or `--write-local-db` were **not run**.
  Their absence must not be described as completed approved-write regression.
- Frontend: 790 tests passed; TypeScript, Web Production build and focused lint passed. API Release
  build passed with zero warnings/errors. The test project has existing
  duplicate-using/nullability/raw-SQL warnings.
- Full-root lint is not clean: the earlier run also traversed pre-existing
  nested worktree/generated artifacts. Only task-file lint is confirmed clean;
  no unrelated generated directories were deleted or repaired.
- Migration `20261002032018_AiPermissionAuthorizationAudit` adds authorization
  evidence and indexes only. Its rollback refuses to drop audit evidence.
  It was applied only in the isolated SQL test, **not Production**.

## Required next gates

1. Provide/review the complete fourteen-agent matrix; named primary/fallback
   owners; budget currency, units and amounts; allowed API/network destinations;
   requested resource/customer/carrier/date scopes and runtime-isolation plan.
2. Finish the structural database boundary, isolated credentials/egress,
   runtime quotas, security suspension/alerts, approval/execution verification
   and communication/runbook adapters, without silently widening grants.
3. Test the reviewed configuration, approval lifecycle and failure behavior,
   missing-migration behavior, signed-in UI and all Constitution section 38
   categories. Review the rollout effect on existing live AI features.
4. Obtain the required governance/release review before Production release,
   migration and deployment. A source checkpoint on
   `codex/ai-governance-constitution` is not a Production release. Do not merge
   it into `main` / `azure-dotnet-migration` or dispatch their deploy workflows
   while the gates above remain incomplete. No Production migration or
   deployment has been performed for this permission-enforcement change.

## Source checkpoint disposition

The user requested commit/push/deploy on 2 October 2026. Commit/push of the
implementation branch can preserve the work for review; Production deploy
remains blocked by the missing controls/configuration and the stated effect
on existing live AI. Inspected workflows deploy Web from `main` and API from
`azure-dotnet-migration`; pushing only the implementation branch does not
trigger their Production push deployments. No release version is incremented
and no Production branch is merged as part of this checkpoint.

**The acceptance checklist is deliberately incomplete. Passing offline tests
does not make the missing workflows/configuration/isolation Production-ready.**
