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

So `POST /api/webhooks/microsoft-graph` **cannot** be a Next.js route handler
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

## 4. Authentication — and the part I cannot do

Application permissions, client credentials, `Mail.Read`. No user sign-in, no
stored password. Tokens live server-side only and never reach the browser; the
Communication Center calls SCMOS's own API, which calls Graph.

**Restrict the app to approved mailboxes with Exchange Online RBAC for
Applications.** Without it, `Mail.Read` as an application permission reads
*every* mailbox in the tenant. That is not a theoretical concern for a
forwarding company's mail.

The following are yours to run — **I cannot enter or handle credentials, and
this holds even if you paste them to me:**

1. Register the application in Entra ID; note the tenant id and client id.
2. Grant `Mail.Read` as an **application** permission and admin-consent it.
3. Create a client secret or, better, a certificate.
4. Scope it with `New-ServicePrincipal` / `New-ManagementRoleAssignment` in
   Exchange Online so it can read only the approved mailboxes.
5. Put the values in the API's app settings or Key Vault:
   `Graph__TenantId`, `Graph__ClientId`, `Graph__ClientSecret`,
   `Graph__Mailboxes`. `__` maps to `:` on App Service, and changing app
   settings restarts the container.

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
Microsoft's network. The API app currently sits behind Easy Auth, which will
reject an unauthenticated Graph POST. Either the webhook path is excluded from
Easy Auth, or Graph is pointed at a separate ingress. This must be settled
before anything else works, and it is an Azure change — yours to make.

---

## 6. Order of work

The spec's own sequence, adjusted for the architecture:

1. Discovery and this document ✅
2. Seven tables + migration ✅ (20260908170406_CommunicationCenter)
3. `GraphAuth` — client credentials, token cache
4. **Mailbox connection test** — one endpoint that reads one message and reports
   what came back. This is the step that proves sections 4 and 5 before any
   pipeline exists, and it is where this work will actually get stuck.
5. Message retrieval
6. Subscription create / renew / delete + the renewal loop
7. Webhook + validationToken + clientState + Easy Auth exclusion
8. The worker
9. Persistence and deduplication
10. Deterministic entity extraction — job code, container, booking, B/L
11. Matching, with the spec's confidence thresholds ✅ (`EmailMatching`)
12. Inbox UI, then the detail page
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
| **You** | The Entra app registration, `Mail.Read` admin consent, and the Exchange RBAC scoping. I cannot handle credentials. |
| **You** | An Azure decision: how Graph reaches the webhook past Easy Auth. |
| **The operations team** | Which shared mailboxes, and how long attachments are kept. |
| **The operations team** | Twenty real emails per category, before the extractor is written. |

Sections 2, 3 and 7 — the schema, the capability bits, the deterministic
extractor and its tests — are not blocked and can be built now.
