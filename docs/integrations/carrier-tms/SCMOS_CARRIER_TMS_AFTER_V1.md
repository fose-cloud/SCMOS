# Carrier TMS API — after V1

Implemented 20 September 2026 · released as v2.7.58 · the three follow-ups the phase records left open that needed no decision from the department: the wording of what waits, the address a delivery is about to connect to, and a webhook that stays dead. The other two follow-ups (an Entra client-credentials option, a per-event-type auto-apply mark) still wait for a carrier that needs them.

## What changed

**The bell, the row badge and the drawer name the door.** One queue holds a haulier's LINE messages and its TMS's status events (Phase 3); until now every surface called all of it "LINE". Now the bell's alert is "ข้อความจากผู้ขนส่งรอการอนุมัติ" with a title that counts each door — `2 ข้อความ LINE รอการอนุมัติ`, `1 สถานะจาก TMS รอการอนุมัติ`, or `2 ข้อความ LINE · 1 สถานะจาก TMS รอการอนุมัติ` (`Notifications.WaitingTitle`); the row's badge reads `MY JOB · LINE 2`, `· TMS 1` or `· LINE 1 · TMS 1` (`pendingMarks`); the drawer's box carries a badge per door present and each message's line starts with its door. The feed's `kind` (`text` / `tms`) was already there; a kind the feed has never sent reads as LINE.

**The address is judged when the connection is made.** Registration checked the URL's name; a name can point anywhere later (a rebind, a split horizon, a mistake). The webhook client's connections now go through `CarrierWebhookDispatcher.ConnectAsync`: the host is resolved at that moment and any private, loopback, link-local or unspecified address (`CarrierWebhooks.AddressProblem`) refuses the whole delivery — `host resolves to a private address (10.0.0.1)` — retried on the schedule like a refused connection, since a name can change back. The loopback is allowed only where insecure URLs are (a developer's machine). Redirects are no longer followed and cookies are not kept: a 3xx is `HTTP 302`, a failure, so a receiver cannot send SCMOS on to an address the rules would refuse.

**A webhook that stays dead is retired.** `FailedInARow` was shown and never acted on. At 30 failed attempts in a row with none delivered (`CarrierWebhooks.RetiresAfter`) — which needs at least one delivery to have run its whole schedule, fifteen hours of a receiver that never once answered 2xx — the dispatcher disables the webhook (`disabled_by = scmos`, `last_error = disabled by SCMOS: 30 deliveries failed in a row`); its queued deliveries close out on the next pass as for any disabled webhook, and a `test` is refused `409`. The carrier registers a new one when the receiver is mended. A single 2xx resets the count.

## Files

- `server/Scmos.Api/Rules/Notifications.cs` — the definition, `WaitingTitle`
- `server/Scmos.Api/Services/NotificationService.cs` — counts by message type
- `server/Scmos.Api/Rules/CarrierWebhooks.cs` — `AddressProblem`, `RetiresAfter`, `Retires`, `RetiredReason`
- `server/Scmos.Api/Services/CarrierWebhookDispatcher.cs` — `Handler`, `ConnectAsync`, the retirement
- `server/Scmos.Api/Program.cs` — the webhook client's handler
- `server/Scmos.Api/Data/CarrierApiCheck.cs` — 5 checks added (142 total)
- `app/scmos/linePending.ts` — `sourceOf`, `sourcesOf`, `pendingMarks` (replaces `pendingCounts`)
- `app/scmos/screens/Workspace.tsx`, `app/scmos/overlays/WorkspaceOverlays.tsx`, `app/SCMOSApp.tsx`
- `tests/linePending.test.mjs` — one test added (579 total)
- `docs/integrations/carrier-tms/SCMOS_CARRIER_TMS_API_V1.md` — the address at send time, no redirects, retirement

No table changed. No permission changed. No new route.

## Verified end to end (LocalDB, 20 Sep)

A ping to a loopback receiver (a developer's machine) is delivered 204. A receiver answering 302 to a `/ok` path: the delivery fails `HTTP 302` and nothing lands on `/ok`. A URL whose name resolves to 10.0.0.1 (`10.0.0.1.nip.io`, accepted at registration as any dotted name is) fails at send time with `host resolves to a private address (10.0.0.1)`. A webhook set to 29 failures and pinged at a receiver answering 500: after the 30th, `disabled` · `disabled by SCMOS: 30 deliveries failed in a row`; its delivery closes out `dead · webhook disabled` on the next pass; a test answers `409 conflict`. Signed in as the owner of a job with two TMS events waiting and two LINE messages elsewhere: the bell reads `2 ข้อความ LINE · 2 สถานะจาก TMS รอการอนุมัติ` over `ข้อความจากผู้ขนส่งรอการอนุมัติ`; the row reads `MY JOB · TMS 2`; the drawer's box reads `TMS · 2 ข้อความรออนุมัติ` with `TMS · SANGJA · …` on each line.

## Known risks

- Retirement counts attempts, not deliveries: five events queued at once to a dead receiver reach 30 in about fifteen hours, the same as one event alone would. That is the intent — the clock is the receiver's, not the queue's.
- A host that resolves to a public and a private address together is refused whole, not connected to the public one. Simpler to state, and a carrier with such a name has something to fix anyway.
- The resolved-address check is inside the HTTP client's connect callback, so it also guards any future caller of the `carrier-webhook` client — and any future caller must accept that a private address is unreachable through it.
