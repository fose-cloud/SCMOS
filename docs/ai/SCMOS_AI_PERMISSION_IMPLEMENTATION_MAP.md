# AI permission enforcement implementation map

Date: 2026-10-02. Branch: `codex/ai-governance-constitution`.
This map was prepared before runtime modifications. The Constitution remains
authoritative; this document does not grant permissions or approve a release.

## Existing components and reuse

| Boundary | Existing component | Enforcement change |
| --- | --- | --- |
| Identity/routing | `AgentRegistry`, fourteen immutable IDs | Preserve all IDs, including `document-agent` and `vendor-agent`; no replacement agents |
| Central authority | `Services/AiGateway` | Extend this gateway with `IAiPolicyGateway`; do not add a second gateway |
| Chat | `AgentOrchestrator` | Authorize before provider calls; reject absent gateway |
| Reads | `ToolRegistry`, `QueryPolicyGuard`, application read services | Wrap registered handlers with gateway authorization, preserving schemas and business rules |
| Collaboration | `ManagementAgent`, fixed `ManagementPlans` | Carry origin and chain; require explicit origin AND specialist grants |
| Non-chat AI | `ExtractionRun`, `BookingDraftService`, `BookingMailPass`, `BillingAiService` | Authorize before reads/provider calls; no nullable fail-open governance |
| Scheduled work | `AgentScanner` | Authorize each pass before loading the register or creating recommendations |
| Writes/approvals | `OperationsChangeService`, `ApprovalPolicy`, approval queue | Keep exact-state transaction protections; close requester self-confirmation gap; never introduce an arbitrary write executor |
| SQL/data | Existing application services, `ScmosDbContext` | Keep database access in application/audit services; never expose SQL/credentials as tools |
| Audit | `SqlAiExecutionAudit`, `ai_audit_logs` | Add a separate authorization-event shape to the same durable writer; failed audit means DENY, including unknown identities |
| Switches | `IAiGovernance`, `OperationsControlService`, options | Existing agent/global switches plus restrictive tool/group/write/external kills; no AI modification endpoint |
| UI | `AiGovernancePanel`, `AiGovernanceEndpoints` | Read-only policy/budget/security visibility under existing audit/admin RBAC |
| Integrations | OpenAI, bounded GitHub readers, platform monitoring, manual carrier/billing services | No new credentials, outbound integrations, agent sends, runbooks or deployment tools |
| Tests | `tests/Scmos.Ai.Checks`, frontend test/build commands | Offline authorization matrix, failure, scope, delegation, audit and approval checks; existing regression suite |

## Policy design

Keep persisted `AiAutonomy` and `AiActionLevel` values unchanged. Introduce a
separate typed action/permission vocabulary with explicit restriction precedence.
Version-controlled JSON is the grant authority. Database tool rows may revoke,
never expand a grant. Unknown IDs/actions/tools/scopes/enum values deny.

Existing read/analyze/draft contracts are the conservative candidate matrix.
Every other action defaults to forbidden; registering an action is not a grant.
No Engineering Production writes, automated financial approvals, carrier
assignment, free-form SQL or arbitrary communication executor will be added.

## Missing approved configuration / safe rollout

The supplied prompt is not a complete, approved fourteen-agent matrix. Named
human owners/fallback owners, per-agent financial budgets, approved network
destinations and isolated-runtime readiness have not been supplied. Do not
invent them. Candidate manifests remain CONFIGURATION_REQUIRED and refuse
runtime execution until reviewed configuration exists. Core manual SCMOS
operations and the governance read/kill-switch UI must remain available.

Physical egress/credential isolation, approved SRE runbooks, deployment approval
and a reviewed Production matrix are release dependencies, not claims made by
unit tests. Keep acceptance criteria explicitly incomplete wherever evidence is
missing. Do not apply migrations or deploy this governance change as part of
inspection/implementation without its required release review.
