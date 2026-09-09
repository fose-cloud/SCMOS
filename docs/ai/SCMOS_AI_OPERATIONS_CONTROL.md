# Operations AI switch

Implementation and local verification updated 2026-09-09, against shared HEAD 72d6e65.
The main switch implementation is in 75a4a09; this follow-up adds the existing administrator MFA gate and mobile confirmation coverage.

## User behaviour

- AI Control Tower shows an explicit Open / Close Operations AI button only when the server identifies a recognised Administrator.
- A second confirmation is required. Opening warns about provider usage charges. Cancelling sends no state change.
- The switch applies to Operations AI for all otherwise authorised accounts, not to one browser. It grants no job-editing rights and does not enable other agents or the legacy Assistant.
- Opening the switch does not submit a question. Existing account/data permissions and read-only tool validation still apply to each question.
- Closing rejects new runs; already accepted runs may finish and record their audit. It is not cancellation of work already in flight.
- The control endpoint honours the existing AdministerData second-factor policy. This change does not turn that policy on or off.

## Persistence and safety

- Migration 20260908131414_OperationsAiControl creates only ai_operations_control and seeds id=1, enabled=false, revision=0.
- In the registered production runtime this durable state controls Operations independently of the older AI:Enabled / ChatEnabled / OperationsAgentEnabled switches. Other specialists retain their own existing flag checks.
- Missing row/table or an unreadable state refuses new Operations runs. No process-local cache can leave another API instance enabled after a successful change.
- AI__OperationsEmergencyDisabled=true is the server-side emergency override. It always wins over an enabled database switch. Applying App Settings follows the normal application restart/configuration lifecycle.
- Enabling requires valid limits, a configured non-mock provider, connected read tools and a ready execution-audit schema. This is readiness inspection, not a live provider connectivity/quota test.
- Changes use an optimistic revision check. Concurrent/stale commands return 409 instead of silently overwriting another administrator.
- One SaveChanges transaction commits the switch and its audit_events entry together. Audit failure rolls the switch back. The existing Audit menu holds actor, before/after values and timestamp; ai_audit_logs remains reserved for AI execution runs.
- The POST accepts only enabled and revision, bounds the body to 1 KB, requires JSON and a custom header, and verifies administrator identity server-side. The web proxy rejects cross-origin browser requests while handling its public Host correctly behind a reverse proxy.
- Provider keys never enter the UI. The previously authorised key is unchanged; no new key was created or copied during this work.

## Verified locally

- Release API build: zero warnings/errors with warnings-as-errors.
- AI/Operations/audit checks: 229 without SQL; 276 including disposable LocalDB integration tests. Provider calls use fixtures only.
- SQL checks cover initial off state, persistence across fresh contexts, administrator-only changes, old revisions, concurrent changes, emergency override, actor/before/after audit, and atomic rollback on forced audit failure.
- Web production build passes; 420 Node tests pass.
- Isolated Edge browser checks pass: confirm/cancel, opening without automatic chat, persisted fixture state after reload, closing, readiness refusal, stale-revision error and hidden controls for non-management metadata. Existing Control Tower scenarios remain passing.
- Mobile confirmation at 390 px has no horizontal page overflow and was visually inspected. Images under outputs/ai-qa contain synthetic data only.
- The existing EF1002 warning is in an older isolated SQL test helper, not the API build.

## Release boundary

This follow-up did not push/deploy, apply a production migration, change Azure App Settings, enable Operations AI, grant write capabilities, or send a live provider request.
Do not infer deployed version or current production switch state from these local tests.

Before release, re-check the current shared branch and review pending migrations (other workstreams have added Communication Center migrations). Deploy the approved code/schema using the established release workflow, retain the disabled seed, then verify the Administrator UI and a non-administrator account against the real session. Enabling remains a separate explicit administrator decision after acceptance testing.
