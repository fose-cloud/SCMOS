# SCMOS Communication Center — Microsoft Graph implementation plan

Written 2026-09-07, after reading the repository. This is the architecture
assessment the specification asks for before any code is written.

Read [the LINE plan](../line/SCMOS_LINE_V1_IMPLEMENTATION_PLAN.md) section 1
alongside this — the discovery is the same repository and is not repeated here.

---

## 1. The headline: this is not a Next.js application

The specification is written for Next.js App Router route handlers with an ORM
and Prisma-style migrations. SCMOS is:

- **Next.js 16.3.1 as a proxy only.** `app/api/[...path]/route.ts` forwards to
  the API. No ORM, no database client, no business logic.
- **.NET 10 minimal APIs + EF Core 10 + Azure SQL** as the actual back end.

So the webhook — `POST /api/integrations/graph/notify`, as it was eventually
built — **cannot** be a Next.js route handler
that writes to a database, because from Next.js there is no database to write
to. It becomes an endpoint in `server/Scmos.Api/Endpoints/`, and the Next.js
proxy forwards to it — or, better, Graph is pointed straight at the API host and
the proxy is not in the path at all.

Every other instruction in the specification survives this change. The pipeline,
the state machine, the idempotency key, the confidence thresholds and the
security rules are all architecture-neutral and all stand.

---

## 2. What already exists that the spec asks for

Do not build these again.

| Spec asks for | SCMOS has | Where |
|---|---|---|
| Azure Blob for attachments | `Azure.Storage.Blobs`, a private container, `StoredDocument` | `Data/`, `Services/` |
| structured logging → App Insights | `Microsoft.ApplicationInsights.AspNetCore` | `Scmos.Api.csproj` |
| Key Vault compatible config | `Azure.Extensions.AspNetCore.Configuration.Secrets` | already wired |
| audit log | `AuditEvent` — who, what, when, before, after | `Data/AuditEntities.cs` |
| RBAC | `[Flags] enum Capability`, bits 0–16 used | `Rules/Roles.cs` |
| background worker | `ReportScheduler : BackgroundService` | `Services/ReportScheduler.cs` |
| existing auth | Entra ID via Easy Auth, `IUserAccessor` | `Auth/` |
| an AI client | `OpenAI` 2.13.0, already used by `ReportWriterService` | `Services/` |
| job matching keys | `JobCode`, `Container`, `Key` are real columns | `Data/Entities.cs` |

The last row is worth dwelling on. The spec's matching order — job number,
container, booking, B/L — maps onto columns that are **already promoted out of
the JSON blob and already indexed**. Deterministic matching is cheap here.

---

## 3. Changes to the specification

### 3.1 Service Bus → the existing worker pattern

No Service Bus package, no Function App. The spec's requirement is that the
webhook be fast and processing be asynchronous; that is met by persisting the
notification and letting a `BackgroundService` pick it up, exactly as proposed
for LINE. One `IHostedService`, one claim-one-row guard, no new Azure resource.

Revisit if volume justifies it. Two shared mailboxes will not.

### 3.2 Permissions: two bits, not six

The spec lists `MAIL_VIEW`, `MAIL_LINK_JOB`, `MAIL_ASSIGN`,
`MAIL_CREATE_INCIDENT`, `MAIL_RATE_REQUEST`, `MAIL_ADMIN`. Four of those are
existing capabilities wearing a new name — linking a job is `EditAnyJob`,
creating an incident is what the incident screen already checks, raising a rate
request is `ViewRates`/`EditRates`.

Next free bit after the LINE plan's two is **19**.

- `ViewMailbox = 1 << 19` — see the Communication Center
- `AdministerMailbox = 1 << 20` — connect a mailbox, manage subscriptions

### 3.3 Ten tables → seven

The spec lists `mailboxes, emails, email_participants, email_attachments,
email_entities, email_job_links, email_ai_analysis, email_actions,
graph_subscriptions, email_processing_logs`.

Three of those are already covered:

- `email_actions` → `AuditEvent`
- `email_processing_logs` → a status column on the email plus App Insights
- `email_attachments` binary → `StoredDocument` + Blob, which exists

`email_ai_analysis` is Phase 2 by the spec's own sequencing and should not be
created in the MVP.

Leaving: `Mailbox`, `Email`, `EmailParticipant`, `EmailAttachment`,
`EmailEntity`, `EmailJobLink`, `GraphSubscription`.

That is seven, not six, and the heading said six until the tables were written.
The arithmetic counted `email_attachments` as covered by `StoredDocument` +
Blob, but what moved to Blob is the **bytes**: a row is still needed to say the
file's name, size and type, which message it arrived on, and which stored
document holds it. The spec also listed ten, not eleven.

**Built 2026-09-08**, migration `20260908170406_CommunicationCenter` — seven
tables, seventeen indexes, nothing existing touched. Applied to LocalDB and the
`(MailboxId, GraphMessageId)` unique key exercised: a redelivered Graph message
is refused, and the same id in a second mailbox is still a separate message.
Nothing else is wired to these tables yet; they are step 2 of the order of work
below.

### 3.4 Keep the unique constraint exactly as specified

`(MailboxId, GraphMessageId)` unique. This is the whole of idempotency and the
spec is right about it. Graph will deliver the same notification more than once.

---

## 4. Authentication — no secret, and the part I cannot do

Application permissions, `Mail.Read`. No user sign-in, no stored password.
Tokens live server-side only and never reach the browser; the Communication
Center calls SCMOS's own API, which calls Graph.

**Built 2026-09-09** as `Services/GraphAuth.cs`, checked by `--check-graph`.

### What changed, and why it is now shorter

This section used to ask for a new app registration with a client secret. **It
does not need one.** The API already holds a system-assigned managed identity
and already calls Graph with it — that is how the Administration screen invites
a colleague (`SignInAccountService`, `User.ReadWrite.All`). Granting `Mail.Read`
to that same identity means there is no secret to create, store, rotate, or hand
to anybody, and no `Graph__ClientSecret` app setting at all. A secret that does
not exist cannot leak and cannot expire on a Sunday.

The plan also asked for a token cache. There is not one and should not be:
`DefaultAzureCredential` caches what it fetches and renews it near expiry, and a
second cache in front of it is a second opinion about when a token dies.

**The cost, stated rather than buried:** one identity then holds both
`User.ReadWrite.All` and `Mail.Read`. A separate registration would keep them
apart, at the price of a secret somebody has to look after. Exchange RBAC
narrows `Mail.Read` to the approved mailboxes either way, so the separation buys
less than the secret costs — but it is a security posture, and it is yours to
overrule. Overruling it means adding a `ClientSecretCredential` to `GraphAuth`
and a secret to Key Vault; nothing else changes.

### Two fences, not one

**Restrict the identity to approved mailboxes with Exchange Online RBAC for
Applications.** Without it, `Mail.Read` as an application permission reads
*every* mailbox in the tenant. That is not a theoretical concern for a
forwarding company's mail.

`Graph__Mailboxes` is the second fence, in code: the list of addresses this
deployment may read at all, checked by `GraphMailboxes` before anything is asked
of Graph. It does not replace the RBAC scoping — a wrong answer still gets
refused by Exchange — but it means a mistaken row in the `mailboxes` table
cannot reach a mailbox the deployment never approved. **An empty list approves
nothing**, deliberately: the other reading is how every mailbox in the tenant
becomes readable because somebody forgot an app setting.

### Yours to run — I cannot enter or handle credentials

1. Grant `Mail.Read` as an **application** permission to the API App Service's
   system-assigned managed identity, and admin-consent it. There is no app
   registration to create and no secret to generate.
2. Scope it with `New-ServicePrincipal` / `New-ManagementRoleAssignment` in
   Exchange Online, against that identity's object id, so it can read only the
   approved mailboxes.
3. Set one app setting: `Graph__Mailboxes`, the approved addresses, separated by
   commas. `__` maps to `:` on App Service, and changing app settings restarts
   the container.

### How you will know it worked

`GraphAuth.ReadyAsync()` reads the `roles` claim off the token and says which of
the several faults it is — nothing configured, an entry that is not an address,
no token at all, or a token without `Mail.Read` — **without touching a
mailbox**. That matters because a 403 from a mailbox read means missing consent,
or missing RBAC scope, or a mailbox that is not there, and the three look
identical. Step 4 is what tells those apart; this tells apart the two that come
before them.

---

## 5. The subscription problem

Graph change notifications expire — about 3 days for mail — and the webhook must
answer a `validationToken` handshake within 10 seconds on creation.

This needs a renewal loop. The `ReportScheduler` ticks hourly and is the pattern
to copy: renew anything expiring within 24 hours, recreate anything already
gone, and record every attempt.

The lifecycle endpoint (`reauthorizationRequired`, `subscriptionRemoved`,
`missed`) is not optional. `missed` in particular means the only correct
response is a delta re-read of the mailbox, not ignoring it — otherwise mail
goes silently unprocessed and nothing says so.

**A caveat the spec does not raise:** the webhook must be reachable from
Microsoft's network. This document said for months that the API sits behind
Easy Auth, which would reject an unauthenticated Graph POST, and called it the
Azure change blocking everything else.

**Measured on 2026-09-09, and it was wrong.** An unauthenticated POST from the
public internet to `/api/integrations/graph/notify?validationToken=…` returns
200 with the token echoed as plain text — the application's own answer. A gated
endpoint returns the application's own `Sign in is required` rather than a
platform challenge. Nothing intercepts these requests before the application
sees them: identity arrives through the web app's proxy key, and each endpoint
decides for itself.

So there is no Azure change needed here, and `clientState` is the
authentication, as intended. If Easy Auth is ever put in front of this App
Service, the two webhook paths must be excluded — written down in
`docs/integrations/SETUP.md`.

---

## 6. Order of work

The spec's own sequence, adjusted for the architecture:

1. Discovery and this document ✅
2. Seven tables + migration ✅ (20260908170406_CommunicationCenter)
3. `GraphAuth` — the managed identity, and the approved-mailbox fence ✅
   (`GraphAuth`, `GraphMailboxes`, `GraphToken`). No client secret and no
   token cache — see section 4 for why both turned out to be unnecessary.
4. **Mailbox connection test** — one endpoint that reads one message and reports
   what came back ✅ (`GraphEndpoints`, `GraphDiagnosis`). `GET
   /api/integrations/graph/status` answers without touching a mailbox; `POST
   /api/integrations/graph/test` reads the newest message in one approved
   mailbox. Administrator only — `AdministerMailbox = 1 << 20`, which is also
   the first capability added since the mail work began.

   The reason this step exists is that **three faults return 403** — no
   consent, no Exchange scope, wrong mailbox — and an administrator reading
   "403 Forbidden" learns nothing. Reading the `roles` claim removes the first
   before any call is made, so a 403 that arrives afterwards means the RBAC
   scoping, and the answer names `New-ManagementRoleAssignment` rather than a
   status code.

   Verified locally as far as it can be without consent: both routes refuse an
   anonymous caller, refuse an unknown account, refuse a Supervisor, answer an
   Administrator, and refuse a mailbox that is not on the approved list
   *before* a token is requested. The live Graph leg is what the two rows in
   section 8 are blocking.
5. Message retrieval ✅ (`GraphMessages`, `GraphMailReader`). A page from a
   mailbox oldest-first from a point in time, one message by the id a
   notification names, and an attachment list. Nothing is saved — that is step
   9, and keeping the fetch separate is what lets the same message be fetched
   twice and stored once.

   Two things worth naming. **Every value is cut to the width of its column**
   by the parser, against `MailText`, which the model now reads too: a
   forwarded subject on its fourth hop runs past 500 characters, and a value
   one character too long fails at `SaveChanges` after the work is done rather
   than at the parser. And **`@odata.nextLink` is checked before it is
   followed**, because following it attaches the bearer token to whatever host
   it names.
6. Subscription create / renew / delete + the renewal loop ✅
   (`GraphSubscriptions`, `GraphSubscriptionService`, `GraphSubscriptionScheduler`).
   Hourly, the `ReportScheduler` shape: renew anything inside a
   twenty-four-hour window, recreate anything already gone, and say why when it
   can do neither.

   **The clock is believed over the row.** Graph never says it has stopped, so
   a subscription still marked ACTIVE goes on claiming to be active long after
   it is not — and a mailbox that has quietly stopped being read looks exactly
   like a mailbox nobody has written to. Expiry is checked before status for
   that reason, and `--check-subscriptions` walks a clock past every case.

   One app setting, `Graph__WebhookBase`. The two paths are constants, so a
   path cannot be mistyped into a portal — `/api/integrations/graph/notify` and
   `/api/integrations/graph/lifecycle`, which step 7 must answer. The scheduler
   is only registered once that setting exists, so a deployment without it runs
   no loop at all.

   **Creating a subscription will fail until step 7 exists**, because Graph
   calls the notification URL during the create and wants the
   `validationToken` back within ten seconds. That 400 is reported as a
   sentence naming the handshake and the Easy Auth exclusion rather than as a
   status code.
7. Webhook + validationToken + clientState + Easy Auth exclusion ✅
   (`GraphNotifications`, `GraphWebhookEndpoints`). Two paths, mapped at the
   same constants the subscription is built from, so the URL Graph is handed
   and the route this API serves cannot drift into a 404 nobody would ever see.

   **The handshake runs before anything else** — before the body is read,
   before the database is touched. Graph waits ten seconds during a create and
   every later step depends on that answer, so nothing that can fail happens
   ahead of it. The token is checked for being a token first: this repeats an
   unauthenticated caller's own input back at them, and the smallest version of
   that is the safest.

   `clientState` is checked **per item, not per request** — a delivery is a
   batch, each item carrying its own subscription and secret. A failure is
   counted and never explained; an unauthenticated caller told why they failed
   is being told how to get closer. Everything answers 202 either way.

   The webhook writes a stub row in `emails` rather than a queue table of its
   own, because the schema already has one: `(MailboxId, GraphMessageId)`
   unique is the idempotency the specification asked for, and `emails_queue_idx`
   is described in the model as what the worker asks for.

   **`missed` is left for step 8 on purpose.** Nothing at the webhook can say
   which messages were lost, so nothing there can fetch them; the recovery is
   the catch-up read forward from `Mailbox.LastSyncedAt`, which the worker
   owns. It is logged loudly because it is the one fault this integration
   cannot otherwise detect — mail that silently never arrived. **Step 8 owes
   that catch-up, or `missed` is only a log line** — and step 8 has since
   delivered it.

   Easy Auth exclusion paths are now written down in
   `docs/integrations/SETUP.md`, whose Graph section was rewritten: it was
   still asking for an app registration, a client secret, and four app settings
   that no longer exist.
8. The worker ✅ (`MailQueue`, `MailWorker`). Claims one row at a time, fetches
   the message the webhook only heard about, and writes it with everybody on it
   and what it had attached. The claim-one-row shape is the LINE worker's,
   including the part that was got wrong there first: **a stale claim is
   measured from the claim, never from arrival**, because a backlog is full of
   rows that arrived long ago and are being worked on right now.

   **The catch-up sweep is here**, on a fifteen-minute clock, which is the
   promise step 7 left behind. It reads each mailbox forward from
   `Mailbox.LastSyncedAt` and writes a stub for anything the webhook never
   brought — recovering a `missed` lifecycle event, a subscription that lapsed
   before the loop caught it, and an App Service that was down when a
   notification was delivered. The mark moves only over messages actually
   written down, so a pass that stops half way costs a repeat rather than a
   gap, and repeats are free because the unique key stores each message once.

   The decision worth naming is **whose fault a failed fetch was**. A message
   that has been deleted finishes the row rather than retrying — three attempts
   and a permanent failed entry, for something nobody did wrong. A missing
   consent or a throttle **stops the pass and spends no retry at all**, because
   it is true of every row equally: a worker that kept going would burn a
   thousand messages' retry budget against a five minute configuration job.
   Only Graph having a bad minute costs an attempt.

   A first read is bounded to seven days rather than the whole mailbox. Moving
   years of history in is a job somebody asks for, not a side effect of
   switching the integration on.
9. Persistence and deduplication
10. Deterministic entity extraction — job code, container, booking, B/L ✅ (`EmailExtraction`)
11. Matching, with the spec's confidence thresholds ✅ (`EmailMatching`)
12. Inbox UI, then the detail page ✅ (`MailReview`, `MailEndpoints`,
    `mailInbox.ts`, `Outlook.tsx`). One screen, master and detail, replacing the
    placeholder that stood at Integrations → Outlook. It opens on what is
    waiting, because that is the reason to visit it.

    **Reading is `ViewMailbox = 1 << 19`**, held from Operation User upward and
    deliberately not by the Subcontractor role — one shared mailbox holds our
    correspondence with every haulier. **Deciding is `EditAnyJob`**, as section
    3.2 said: confirming which job a message belongs to changes what the
    register says about that job. Both bits of that are asserted in
    `--check-capability`.

    The message's status is *derived* from its links after every decision
    rather than nudged, so the badge, the list and the page cannot disagree.
    The identifiers the extractor read are shown beside the suggestion, because
    the first thing anybody checks is whether the number the machine matched on
    is the number in front of them.

    Two faults were found by running it that no pure check could have. The list
    answered 500 because a non-nullable `int` from the query string is required
    in a minimal API, so a caller wanting the first page was refused. And
    confirming a suggestion left the message in the waiting list with a
    confirmed link on it: the status query ran before the save and read the row
    it was about to change, so the message still carried the SUGGESTED it had a
    moment earlier.
13. Attachments to Blob

AI classification is Phase 2 and is not in this plan.

---

## 7. Confidence — keep the spec's numbers

```
>= 0.95  auto-link
0.70–0.94  suggest, a person confirms
< 0.70   unmatched
```

This matches how SCMOS already treats uncertainty — the KPI engine refuses to
score below a minimum sample rather than guessing, and the report writer's
output is a draft nobody has agreed to. An email silently linked to the wrong
job is worse than one left unmatched, because the second is visible.

**Built 2026-09-09** as `Rules/EmailMatching.cs`, checked by `--check-email`.
What each kind of identifier is worth was measured on the register rather than
chosen: of 1,522 distinct container numbers 1,383 sit on exactly one job (91%),
against 802 of 1,038 job codes (77%) and 299 of 448 bookings (67%). A container
is the sharpest identifier the register holds — sharper than our own job number,
because a job number is a booking and a booking can be several containers.

Two rules do the work. A value that reaches several jobs has its weight divided
between them, because the evidence really is split. Independent identifiers
agreeing on one job combine by multiplying what is left to doubt, so two signals
each leaving 4% leave 0.16% together — which is where auto-linking comes from,
and why a single soft signal never gets there.

One consequence worth deciding rather than discovering: **a value the register
holds against two jobs offers neither**, because 0.96 split two ways is 0.48 and
the floor is 0.70. The message stays in the queue with the identifiers it named,
for a person to resolve. Lowering the floor would surface those candidates and
is a change to the specification's numbers, so it is left as a question.

---

## 8. Blocked on

| Blocked on | Needed |
|---|---|
| **You** | `Mail.Read` admin consent on the API's managed identity, and the Exchange RBAC scoping. No app registration and no secret — see section 4. |
| **The operations team** | Which shared mailboxes, and how long attachments are kept. |
| **The operations team** | Twenty real emails per category, before the extractor is trusted. |

Steps 1 to 12 are done; only 13 — attachments to Blob — is left, and none of
them needed any of the above.

That table lost a row on 2026-09-09. "How does Graph reach the webhook past
Easy Auth" was carried as a blocker from the first draft and was never
measured; when it finally was, nothing was in the way. **One thing now blocks
this integration, not two: the `Mail.Read` consent and the Exchange scoping.**

**Step 4 is the one to run first** once the grant is made. `POST
/api/integrations/graph/test` will say which of consent and scoping is still in
the way, in a sentence, rather than leaving somebody to read a 403. Until
then it reports the fault it can already see.
