# Phase 1C — rule metadata, first increment

Started after Web release 481825e succeeded on 15 September 2026.
This increment is local only, not pushed or deployed.

BusinessRuleRegistry provides four version-1 descriptors anchored to existing
code: zero-grace arrival KPI, strictly-greater-than default 30-minute lateness,
the workspace DELAY bucket, and MonitorRules risk priority. Threshold metadata
uses the existing constants. No business formula or data filter was changed.

Important distinctions:
- IsOnTime measures on/before plan, not a 30-minute allowance.
- LateBeyond returns false when unmeasurable; that cannot establish on-time.
- DELAY uses delayed status or a nonblank reason, excluding cancellation.
- Monitor flags are not an arrival KPI. Unassigned checks occur before the
  two-day carrier/truck window; caller-specific windows may further restrict it.
- Customer contracts are not verified: resolving a customer-specific contract
  returns unknown, never the system rule as an assumed SLA.

Descriptors are metadata only: not added to prompts, tool responses or audit
payloads yet. Next work maps source grain/scopes and validates boundary examples
against the original rule functions before exposing this metadata to AI.
No SQL migration, credential change, new agent, or production write.

## Source scope and boundary verification

Added SourceScopeRegistry for today/risk_today/delays/search. Grain is one
authoritative operation_jobs key, not distinct job code. Team/owner scope comes
from the server and is rechecked in the adapter. All source rows are scanned for
totals, but evidence is capped; malformed/undated counters cover the authorized
scan rather than only the selected result. DELIVERY is excluded by the current
category predicate (this is not an explicit IMPORT/EXPORT allowlist).

Raw rules and adapter scope are separate: raw DELAY may include completed work
with a reason; Operations excludes completed/cancelled first. Raw MonitorRules
can flag an unassigned job beyond two days; risk_today adds its own <= today+2
window. Any nonblank arrival suppresses the monitor, even if not a valid date.

Boundary fixtures call existing functions: exact plan time, +1/+30/+31 minutes,
early arrival, midnight, leap day, missing fields, cancelled/completed DELAY,
day+2/day+3, risk priority and driver/plate AND rather than OR.

### Calendar discrepancy — follow-up correction, not yet deployed

The previous DateNumber accepted impossible dates such as 31/02/2026.
The authorized 15 September read-only census found zero affected measurable
rows out of 3,537 jobs; see SCMOS_INVALID_DATE_IMPACT.md for full scope/limits.
The follow-up correction makes IsDate, DateNumber and PartsOf calendar-aware.
The on-time descriptor is version 2. Regression checks reject impossible dates.
The jobs PUT endpoint rejects newly introduced impossible DATE/ARRIVAL DATE/
CLOSING DATE values before saving the batch; unchanged legacy invalid dates
and free-text placeholders are preserved. This is not automatic data remediation
or a blanket ban on existing import placeholders. No Production deploy occurred.
