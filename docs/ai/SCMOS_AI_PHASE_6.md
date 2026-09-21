# SCMOS AI platform — Phase 6, first Engineering read

Implemented locally 21 September 2026 and merged onto the v2.7.70 production code for release. **Partial Phase 6.** Phase 5's newer Document & Invoice Agent reads paperwork, billing timeliness and expiry under SCMOS's existing rules. It does not compare invoice amounts: there is no authoritative invoice ledger or approved rate/terms source, so amount reconciliation remains deferred.

## Boundary

- `engineering-agent` has one read-only tool, `query_repository`, behind `AI:EngineeringAgentEnabled` (default `false`). Only an Administrator may use it. The UI choice is hidden when the server omits the agent, but the API enforces the role independently.
- The server fixes the public repository to `fose-cloud/SCMOS`. The model may select only `open_issues`, `open_prs` or `recent_commits` and a limit of 1–20. It cannot provide a repository, URL, ref, command, mutation or credential. The server uses GET only and disables redirects.
- GitHub's issues listing includes pull requests, so the issue view excludes those entries. Each response contains only number/SHA, title/commit subject, state, timestamp and a server-constructed canonical public link. Issue bodies may be present in GitHub's REST response; they are never projected into tool evidence, audit or model context. Diffs and source files are not fetched.
- Results are limited to the first API page, with at most 20 displayed rows. `total` and `returned` are counts of that sample, **not** the repository-wide totals. Titles are untrusted display data. The provider selects the read but never receives the returned titles, and its free-form prose is not used as the factual summary.
- Existing AI identity, capability check, typed schema, read policy, per-run budget and durable audit apply. Audit failure withholds evidence. No PR/issue creation, comment, merge, push, deploy, code execution, source-file read or SCMOS business-data write is in this slice.

## Verification and release boundary

Offline tests cover the fixed GET path, PR filtering, Administrator-only routing, default-off flag, typed tool rejection, malicious title/body isolation, canonical-link validation and audit failure. Client parsing rejects arbitrary links, body fields, invalid counts and agent/evidence confusion. On the integrated v2.7.70 base, 654 offline AI checks and 587 Node tests passed; the API Release build passed with warnings treated as errors. No live OpenAI invocation was part of these checks.

Release must confirm the live GitHub endpoint, successful API/Web deployments, an authenticated Administrator read and a non-Administrator denial. The user separately authorized Production enablement; the flag must remain off until the deployed code and health checks pass. No migration is needed for this read-only slice. Future phases must address GitHub API rate limits, paging, full-history totals and any repository write approvals separately.
