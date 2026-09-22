# Phase 9 — production hardening

Status: first read-only hardening increment implemented locally; not pushed or deployed by this change. Phase 8 was deployed to the API and web at `b7fdd1c` on 22 September 2026, and `AI__ManagementAgentEnabled=true` was verified on Production with the API healthy. An authenticated Management question has not yet been verified in Production.

## Evidence completeness

- The Management Agent checks that each specialist's `Truncated` flag agrees with `Total > Returned`, and does not choose one job when the search reports more than one total match.
- A one-job paperwork finding uses the document reader's full checklist standing, not just its first 50 display rows. Many held files can no longer hide a missing folder and make the summary say the checklist is complete. Unclear files and truncated display rows are named.
- In the late-paperwork plan, the intersection of two capped lists is labelled as a *minimum found in the rows read*, never as the full overlap. It remains a set overlap, not a cause of delay.
- The late-paperwork job list must match the document evidence rows whose identifiers go into the audit; a disagreement fails the run without releasing a partial finding.

The existing specialist reads, user scope, per-step authorization, audit sequence and result contracts remain unchanged. No database change, migration, write permission, new model call or secret is introduced.

Offline regression checks cover a job with more than 50 held files and missing checklist folders, plus a partial two-specialist intersection. Remaining Phase 9 gates include authenticated Production question/role checks, latency under working-hours load, audit reconciliation for real runs, and a reviewed rollback drill. The register-read redesign in `docs/REGISTER_READ_PLAN.md` follows Phase 9 rather than being folded into this patch.
