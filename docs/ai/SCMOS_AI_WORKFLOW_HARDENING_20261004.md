# Workflow hardening implementation map — 4 October 2026

User scope: finish Approval, Communication Gateway, agent/database separation,
and human ownership/budget configuration. Reuse `AiGateway`, the existing
`Approval` table/queue and business services. Keep all fourteen existing agents.

Before changes, inspection confirms: the policy gateway already blocks unknown
tools and unconfigured manifests; the legacy approval queue records decisions
but is not a bound execution adapter; `AgentScanner`/`BookingMailPass` directly
receive `ScmosDbContext`; communication currently produces drafts, not sends.

Implementation sequence:

1. Relocate scheduled pass database queries behind finite application/data
   operations; never expose DbContext, SQL, IQueryable or credentials to agents.
   Preserve existing finding, deduplication, workflow and billing/SLA rules.
2. Extend the existing approval queue with a typed immutable action/resource/
   before/proposed envelope. Independent human approval, expiration, exact-state
   verification and conditional single-use transitions are required. No arbitrary
   callback or unreviewed execution adapter is allowed.
3. Route typed communication requests through the existing gateway, checking
   both origin and Communication Agent. Preserve draft-only behavior until
   explicit send/action/recipient/network/approval configuration is reviewed.
4. Human owners must be real, eligible accounts. The user's clarified eligibility
   is Supervisor and above. Confirmed financial ceilings are ONE shared USD 0.20
   per day and USD 10 per month across all fourteen agents, not per-agent amounts.
   Do not invent primary/fallback identities, per-agent allocations, expand approval
   rights or enable live AI. The existing accounting calendar remains UTC.

Tests use mock/offline adapters and an isolated LocalDB only; no new key,
Production writes, migration application or deployment is authorized by this
implementation map. Physical per-agent runtime/credential/egress isolation is
distinct from a code-level database boundary and still needs review evidence.

## Implemented in this working-tree increment

- `AgentScanner` and `BookingMailPass` no longer receive `ScmosDbContext` or
  perform LINQ/SQL queries. `AiPassRepository` exposes finite, policy-checked
  operations and refuses saving anything except the current agent's AI decisions.
  Existing register cache, supplier services, assessments and draft rules are reused.
- The existing Operations approval adapter stores a v2 envelope in the existing
  `Approval.Payload`: exact job key, before values, proposed values, requester,
  immutable hash, 30-minute expiry, agent/version, policy version and correlation.
  Confirmation quotes the payload hash, requires an independent current authorized
  staff account with verified MFA, rechecks policy/owners/kill switches/budget,
  consumes the proposal and writes/audits in one serializable transaction, then
  rereads the object AND approval before commit. Public arbitrary ApprovalIds and
  legacy decide/manual-applied routes still cannot authorize this adapter.
- The Communication pass uses a typed draft entry in the existing `AiGateway`.
  It checks initiating and target requests with the same resource/identity/scope/
  correlation; cross-agent requests cannot borrow Communication permissions.
  Body/recipient/network/credentials/transport callbacks are not accepted.
  Existing templates are still MANUAL drafts, not external sends.
- Runtime human-owner validation resolves existing active `Staff.Id` values or
  explicit `email:` references with exact case-insensitive directory matching,
  requires supervisory roles (Supervisor, Assistant Manager, Manager,
  Administrator), nonempty sign-in addresses and distinct primary/fallback
  identities. Missing/deactivated/demoted owners fail closed. The policy panel
  shows owner identifiers; `/api/ai/owner-options` lists eligible existing staff
  only under the existing directory-reading capability. No RBAC is expanded.
- Human-approval-required requests now honor runtime governance, revocation and
  budget checks too. Preparing an approval records evidence with zero execution
  reservation; an actual approved dispatch reserves before execution.
- `scmos-permission-4-candidate` records the human-confirmed shared USD ceilings
  and retains fixed tool bindings as metadata, NOT grants. All fourteen
  candidate manifests retain their prior permissions, nominated primary/fallback
  email references and null per-agent budgets, empty network lists and unapproved
  policy/isolation readiness. Nominations do not certify live account eligibility.
- The durable authorization writer checks shared daily/monthly reservations
  across all agents AND the existing per-agent constraints within the same
  serializable SQL transaction as the reservation insert. API instances and
  policy versions cannot create separate pools. A full pool refuses dispatch or
  preparing approval with durable zero-cost denial evidence. Replays reserve once.
  The read-only policy panel identifies the shared limits and monthly reservations;
  unavailable accounting remains unknown, not invented zero or actual invoiced cost.

## Explicitly unfinished / not enabled

1. No external-send adapter is enabled. Approved channels, recipients, credentials,
   network access, exact-message approval, idempotency and provider receipt/final
   verification must be reviewed before sending. No generic callback is provided.
2. The bound execution adapter is Operations only. Other high-impact actions stay
   forbidden until their existing business services have specific reviewed adapters.
   Existing v1 proposals remain readable but cannot execute under the v2 contract;
   create and review a new proposal instead of implicitly upgrading an old approval.
3. Database/process/credential/egress separation is NOT physically deployed. The
   data-layer boundary is not proof of independent databases or least-privilege
   identities. Production DB targets and reviewed isolation are still required.
4. Human supplied the pair in order: primary `K.nattikorn-fos@hotmail.com`, backup
   `fosfaaylove1@gmail.com`, shared across all fourteen agents. The candidate uses
   explicit email references, NOT guessed Staff IDs or expanded roles. Live account
   existence/Active/Supervisor-or-above checks are still unverified: the read-only
   browser session could not start in this environment. Every affected execution
   rechecks the actual directory; missing, ambiguous, deactivated, demoted or same-
   account aliases are blocked. Role labels and email stems are not identities.
   An authorized reviewer must confirm the two rows via the existing `/api/staff`
   directory before AI enablement. If an address is stored only as a guest UPN, select
   the verified real Staff ID instead; never infer one from a display name or
   silently elevate/create a user to satisfy readiness.
5. Shared scope and both monetary ceilings are confirmed and implemented. Individual
   request/token/tool/runtime/reservation bounds and any per-agent allocations still
   need review; no allocation is guessed. USD 0.20/day means at most USD 6.00–6.20
   over a 30–31-day month even though the monthly upper ceiling is USD 10. Neither
   ceiling is increased to consume the monthly amount. Reservations are conservative
   application accounting, not a settled provider bill or provider-account hard cap.
6. No Production migration, merge, push, deploy, live OpenAI or outbound communication
   was performed in this increment. The candidate remains disabled by missing
   configuration/review; manual operations are unchanged.

## Release decision — 4 October 2026

After the user requested commit/push/deploy, they explicitly approved deploying
the governance increment with AI disabled first. This is approval to release the
fail-closed code through the existing workflow, NOT a policy/isolation approval,
an execution grant or certification of the nominated live accounts. All fourteen
manifests must continue refusing work until their readiness requirements are met.
Manual SCMOS operations must remain available. Integrate the latest Production
branch and verify the combined changes before publishing; record final release
evidence separately from the pre-release test evidence above.

## Verification

The offline suite includes real SQL-backed exact changes, independent approval,
MFA, hash mismatch, expiration, replay/concurrent consumption, stale objects,
policy-version mismatch, audit failure rollback, injected post-save state mutation
with verified rollback, real owner eligibility/revocation, and loopback HTTP guards.
Authorization SQL checks include pending-approval budget and stored-tool revocation,
fourteen-agent concurrent shared daily/monthly limits, policy-version continuity,
UTC day rollover, replay accounting and absent shared daily configuration. Owner
checks include exact email references, case normalization, ID/email aliases,
ambiguous normalized addresses, absent accounts, inactive accounts and demotion.
Only an
explicit offline fixture increases the daily cap to exercise the monthly ceiling;
the embedded candidate retains the user's USD 0.20 daily amount.

Use the hardcoded isolated LocalDB instance only:

```powershell
dotnet run --project tests/Scmos.Ai.Checks -- --permission-local-db --approval-local-db --summary-local-db
```

Scratch databases are freshly named and removed in `finally` after exact LocalDB
target checks. No appsettings, Production connection or live provider is used by
this runner. This evidence does not certify physical isolation or live integrations.

Pre-integration verification (4 October 2026): 1,432 offline AI/API/SQL assertions and
796 frontend tests passed; frontend type checking and focused lint passed;
API Release build passed with zero warnings/errors. Frontend production build
passed. No schema change is introduced by this increment (the previously pending
authorization-audit migration is not applied to Production).

## Integrated release preflight — v2.8.18

Integrated Production branch `ec9e6a0f` without dropping its mail privacy,
supplier audit or Annual Evaluation changes. Booking mail keeps the mailbox's
owner when staging a decision; finite repository reads preserve that projection.
The newly integrated Annual Evaluation summary is a Management Agent entry:
it now authorizes the exact campaign/carrier before reading evidence, calling
the provider or writing a summary. Its tool binding is metadata only; the
candidate grants no `summarize_evaluation` execution. Missing gateway or current
candidate denial is tested without a SQL provider or model calls. Historical
manual evaluation and saved-summary reads are not gated by AI execution readiness.

Combined preflight passed 1,456 AI/API/offline/isolated-SQL assertions and 817
frontend tests; TypeScript and lint passed (three existing frontend warnings).
Local lint excluded unrelated `.worktrees` artifacts, which are absent from CI.
API Release build passed with zero warnings/errors and Web Production build
passed. The merged EF snapshot matches the runtime model. Production migration
application and deployment are not claimed by this preflight: their final status
must be verified from the existing GitHub release workflows after merge.

## Owner verification — 5 October 2026 (`scmos-permission-5-candidate`)

Item 4 above, done read-only in Production as AD-01 through the existing
`/api/staff` directory and `/api/ai/owner-options`, by the same exact-address rule
`AiOwnerDirectory` applies:

- Primary `email:K.nattikorn-fos@hotmail.com` matched exactly one row: AD-01,
  Administrator, active, nonempty sign-in address. Eligible.
- Fallback `email:fosfaaylove1@gmail.com` matched no row. It would fail as
  `owner_not_eligible`. The near address `fosfaaylove@hotmail.com` (SC-02) is a
  different address and a Subcontractor; it was not substituted.
- Eligible accounts at the time: AD-01, AM-01 (Assistant Manager), SV-01
  (Operation Supervisor).

The user chose AM-01 as the fallback for all fourteen agents. The candidate now
names the verified Staff ID `AM-01`: staff addresses were being moved to
leschaco.com on 4–5 Oct, and an ID does not change with the address.
AD-01 and AM-01 are two accounts of the SAME person: the code-level independence
check (distinct Staff IDs and addresses) passes, but no second human can act for
the owner. The user chose this knowingly; record it at the policy review.

Only `fallbackOwner`, the version fields and the reason changed. No permission,
tool, budget or isolation value changed, `approvalReference` is still null, and
all fourteen agents still refuse with `policy_review_required` until the review,
per-agent bounds and isolation evidence exist. Every execution rechecks both rows
live, so a later deactivation or demotion of either account blocks again.

Verification: offline 1,383 checks; focused SQL suites 1,456 on the isolated instance
(the first run on a freshly started instance failed once at "write SQL: concurrent
confirmation has one effect"; two consecutive reruns passed in full, so this is a race in
that check to watch); 818 frontend tests, TypeScript, lint (three existing warnings),
API Debug/Release builds with warnings as errors, 35 rule flags.

## Reservation per model call — 5 October 2026

The user's decision after the per-agent bounds draft: set money aside per call to the model,
not per authorization. Until now every allowed authorization reserved the agent's
`MaxReservationCost`, so a one-tool chat run reserved four times for one model call, a
rule-only pass reserved every 15 minutes for no model call at all, and the Booking mail pass
reserved about five times per round for up to ten model calls it never authorized one by one.

Constitution sections 24 and 41 were applied: the existing gateway, writer and tool registry
are reused; no new policy path, tool, grant or scope is added.

- `AiAuthorizationRequest.ModelCall` marks the authorization asked immediately before one
  model call. Only such an authorization reserves; `SqlAiExecutionAudit` records
  `modelCall` in the evidence row's metadata.
- Unchanged: every allowed authorization still counts toward the agent's `DailyRequests`
  (attempted calls, now counted from `Decision = Allow` rather than from reserved rows), and
  every one is still refused once the agent's or the fleet's remaining money cannot cover
  one more call. Preparing an approval still reserves nothing; an approved Operations write
  now reserves nothing either (it calls no model) but keeps both checks.
- Where the reservation now sits: `ToolRegistry.AuthorizeRunAsync` (the first call of every
  chat run; all seven chat agents call the model next), `ReserveModelCallAsync` before each
  further Engineering round, the paste path of the Booking Agent, each mail of the Booking
  mail pass (newly authorized per mail), `ExtractionRun` (document reading, incl. Billing's
  document kind), Billing analysis, and the Annual Evaluation summary.
- Not changed: the Orchestrator's run gate, tool-call and tool-read authorizations, the
  scheduled passes' data reads and the Communication pass's per-row authorizations reserve
  nothing now; the Communication pass then still authorized twice per register row (reduced
  to once per round the same day — below). LINE photo/message reading and the monthly report
  commentary still call the model outside the gateway (found 5 Oct; not in this change).

Verification: offline 1,384 checks; focused SQL suites 1,461 on the isolated instance, twice,
including four new reservation checks (a read reserves nothing; a model call reserves the
per-call amount; zero-cost authorizations still count toward daily requests; a read is still
refused when no call fits); tests/aiModelReservation.test.mjs pins every governed
`provider.CompleteAsync` call site to a reviewed list and its reservation.

## Communication pass authorized once per round — 5 October 2026

The scheduled Communication pass asked the gateway twice for every row of the register (origin,
then the Communication Agent as target, bound to that job): about 9,700 serializable
authorization writes a round, roughly 930,000 a day at the 15-minute cadence. The user asked
for one authorization per round.

- `AiGateway.CommunicationPassAsync` asks the same two checks once, bound to the round
  (`Pass(communication-agent, CommunicationDraft, scan_communication)`, system identity, team
  scope), before the round's context is read; only then does it return the judge, which is
  the existing deterministic templates (`CommunicationDrafts.Assess`) and nothing else. A
  refused round reads nothing and judges nothing. `AgentScanner` uses it.
- Kept: the scanner's own pass gate and every finite data read still authorize as before, and
  `AiPassRepository.SaveAsync` rechecks policy, owners and kill switches immediately before
  the round writes its decisions. `DraftCommunicationAsync`, the per-object entry with its
  shipment binding checks, is unchanged for any other caller; both entries share one
  origin-then-target helper. No resource type is a policy dimension, so the decision is the
  one each row used to get; only its evidence is per round instead of per job.

Verification: offline 1,387 checks, including three new ones (two gateway requests however many
rows are judged; the same result as the templates for every row; a refused round reads no
context); focused SQL suites 1,464, twice; tests/communicationPass.test.mjs.
The older `--write-local-db` suite stops before its scan section at "write SQL: concurrent
confirmation has one effect" (the second confirmation gets `audit_unavailable`, not
`already_applied`). The code before both of today's changes (70d0dfae) fails the same way, so it
is not caused by them; the end-to-end scan run of this pass is therefore not evidenced here.
