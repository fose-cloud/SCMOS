# Connecting the four integrations — what SCMOS needs from you

The four Integrations screens (ABS, CCS, Outlook, LINE) each probe an endpoint
that does not exist yet and report honestly that it is not connected. None of
them is blocked on a screen. Each is blocked on an address, a credential, and a
decision about which key joins the other system's records to ours.

**I cannot enter or handle credentials, passwords, API keys, tokens or
connection strings — this holds even if you paste them to me directly.** Every
command below is one for you to run. Nothing in this file contains a secret and
nothing in it should ever be edited to contain one.

---

## How SCMOS reads configuration

App settings on the API App Service, or Key Vault references. **A double
underscore maps to a colon**, so `Graph__Mailboxes` in Azure is `Graph:Mailboxes`
in the application.

**Changing an app setting restarts the container.** Budget about 90 seconds and
do not do it during a deploy.

Prefer Key Vault references for anything secret:

```
@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/<name>/)
```

---

## 1. LINE

### In the LINE Developers Console

1. Create a **Messaging API** channel under your provider.
2. Note the **Channel secret** (Basic settings) and issue a **Channel access
   token** (Messaging API tab).
3. Set the webhook URL to the API host, not the web host:
   `https://<api-app>.azurewebsites.net/api/integrations/line/webhook`
4. Turn **Use webhook** on. Turn **Auto-reply messages** and **Greeting
   messages** off — they interfere with the confirmation replies.
5. Add the bot to one internal test group first. Not a live vendor group.

### App settings on the API

| Setting | What it is |
|---|---|
| `Line__Enabled` | `true` / `false` — the off switch, so the integration can be disabled without a deploy |
| `Line__ChannelSecret` | Key Vault reference. Verifies the webhook signature |
| `Line__ChannelAccessToken` | Key Vault reference. Sends the confirmation reply |

### The part only you can decide

**Which LINE group belongs to which supplier.** This is a table somebody has to
own and keep current. The integration cannot infer it, and it must never infer
it from the message text — a vendor typing another vendor's name is not
authorisation.

### Before the parser is written

Please send **twenty or thirty real messages** from an existing vendor group,
with any personal details you would rather not keep removed. Writing keyword
rules against invented examples is how a parser passes its own tests and fails
on the first real day.

---

## 2. Outlook / Microsoft Graph

**Two tenants (4 Oct 2026).** SCMOS runs in its own Entra tenant
(`e66b7b39-4a09-4b99-8c77-71cc0fb10836`, "Default Directory"); the company's
mail is in leschaco.com's (`b129e0ef-627a-4f14-a0c0-7aab3bf95405`). The API's
managed identity can only ever be given mailboxes of its own tenant, so it
cannot read the company's mail. That needs a multi-tenant app registration,
**SCMOS Mail Reader**, in SCMOS's tenant. The app trusts the managed identity
through a **federated credential**. There is still **no client secret**: the
managed identity vouches for the app, and Entra issues the app a token for
leschaco.com.

**Exchange RBAC is the only grant.** Microsoft's rule is that a `Mail.Read`
consented in Entra and a `Mail.Read` assigned through Exchange RBAC for
Applications are a *union*. An Entra grant reads every mailbox, whatever the
Exchange scope says
([RBAC for Applications, FAQ](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac)).
So nothing is consented in Entra, and Exchange assigns `Application Mail.Read`
on a scope holding only the booking mailboxes. SCMOS enforces this. Across
tenants, a token that carries `Mail.Read` is refused (`too_broad`) and no mail
is read until the consent is withdrawn.

> Earlier versions of this page consented `Mail.Read` in Entra and then scoped
> it with `New-ManagementRoleAssignment`. Under the union rule that scoping does
> nothing. Do not follow them.

### A. In SCMOS's tenant — its administrator

1. Entra admin center → **App registrations** → **New registration**:
   - Name: `SCMOS Mail Reader`.
   - Supported account types: **Accounts in any organizational directory
     (Multitenant)**.
   - No redirect URI.

   Note the **Application (client) ID**.
2. **API permissions**: remove the default `User.Read` and add **nothing**.
3. **Certificates & secrets → Federated credentials → Add credential**:
   - Scenario: **Managed identity**.
   - Identity: the API App Service's system-assigned identity (`scmos-api-3936`).
   - Name: `scmos-api`.
   - Leave the audience as `api://AzureADTokenExchange`.

   Create no client secret.

### B. In leschaco.com's tenant — its administrator

1. Create the app's service principal there, with nothing consented:

   ```powershell
   Connect-MgGraph -TenantId b129e0ef-627a-4f14-a0c0-7aab3bf95405 -Scopes Application.ReadWrite.All
   $sp = New-MgServicePrincipal -AppId <client id>
   $sp.Id   # the service principal's object id, for step 2
   ```

2. Give it the booking mailboxes only, in Exchange Online PowerShell:

   ```powershell
   Connect-ExchangeOnline
   New-ServicePrincipal -AppId <client id> -ObjectId <$sp.Id> -DisplayName "SCMOS Mail Reader"
   New-ManagementScope -Name "SCMOS Mailboxes" -RecipientRestrictionFilter "CustomAttribute1 -eq 'SCMOS'"
   Set-Mailbox <booking mailbox> -CustomAttribute1 SCMOS
   New-ManagementRoleAssignment -App <client id> -Role "Application Mail.Read" -CustomResourceScope "SCMOS Mailboxes"
   Test-ServicePrincipalAuthorization -Identity <client id> -Resource <booking mailbox>    # InScope: True
   Test-ServicePrincipalAuthorization -Identity <client id> -Resource <another mailbox>    # InScope: False
   ```

   The second test is the point of the exercise. If `CustomAttribute1` is
   already used for something else, filter on another attribute. A new
   assignment takes **30 minutes to 2 hours** to reach Graph (Exchange's cache).

3. Enterprise applications → **SCMOS Mail Reader** → **Permissions** must list
   no Microsoft Graph application permission. If `Mail.Read` appears there,
   revoke it. SCMOS refuses to read while it is granted.

### C. App settings on the API

None of these is a secret.

| Setting | What it is |
|---|---|
| `Graph__MailTenantId` | `b129e0ef-627a-4f14-a0c0-7aab3bf95405` — the tenant the mail is in |
| `Graph__MailClientId` | The client id from A1 |
| `Graph__Mailboxes` | Comma-separated addresses SCMOS may read. **Empty approves nothing**, deliberately — the alternative reading is how every mailbox in the tenant becomes readable because somebody forgot a setting |
| `Graph__WebhookBase` | The origin Graph will call, `https://scmos-api-3936.azurewebsites.net`. Origin only — the paths are fixed in code. Set it last (see *Checking it worked*) |

How the two id settings combine:
- **Both set:** SCMOS reads mail through the cross-tenant app.
- **Neither set:** the managed identity reads its own tenant, as before.
- **Only one set:** refused, and the status names the missing setting.

Graph change notifications are not documented for RBAC for Applications. If
SCMOS cannot create subscriptions, the 15-minute catch-up still reads every
active mailbox, so mail arrives at worst 15 minutes late.

### D. Personal mailboxes, and who is read from them (4 Oct 2026)

The department reads the Leschaco mailboxes of Operation, Supervisors and
the Assistant Manager. Exchange grants a mailbox whole, so SCMOS does the
filtering: **from a personal mailbox it reads only the senders listed in
SCMOS**. Each listed sender is a full address; a domain is never accepted.

How it reads a personal mailbox:
- **From anyone else, nothing is fetched beyond the sender, and nothing is
  kept, not even the subject.**
- A personal mailbox with nobody listed reads nothing.
- A shared mailbox is read whole, as before.
- Mail that arrived before a sender was listed is not read back.

For each person's mailbox:
1. Add it to `Graph__Mailboxes` (the Portal).
2. Stamp it `CustomAttribute1 = SCMOS` so it is in the Exchange scope (B2).
3. In SCMOS: **Outlook → ตั้งค่าการอ่าน → กล่องอีเมล**. Choose its owner and
   tick *เปิดอ่าน*. This needs the Administrator, signed in with a second
   factor.

   A member of staff's own address cannot be declared shared: that would read
   their whole mailbox. *ทดสอบ* reads one message to prove access.

The sender list is kept in the same place (**ผู้ส่งที่อ่านจากกล่องส่วนตัว**)
by Supervisor and above. Every change is in the audit trail.

Who sees what is kept from a personal mailbox:
- **The Communication Center, its attachments, and the Booking Agent's
  drafts:** the mailbox's owner, Supervisor and above, and the owner of a job
  the message is linked to.
- **The AI chat:** shared-mailbox mail only. A personal mailbox's mail is never
  given to the model.
- **An attachment filed to a job by a confirmed link:** becomes that job's
  paperwork.

`clientState` is **not** an app setting. SCMOS generates 256 bits per
subscription, stores it, and checks it in fixed time on every delivery — one
secret per mailbox, so a leak from one does not authenticate deliveries for
another.

### Easy Auth — measured, and it turns out not to be in the way

This was written down for months as the Azure decision blocking everything
else: the API sits behind Easy Auth, Graph cannot sign in, so the webhook would
be rejected before it arrived.

**Checked on 2026-09-09 against the deployed API, and it is not true of this
deployment.** An unauthenticated `POST` from the public internet to

```
https://<api>/api/integrations/graph/notify?validationToken=probe123
```

returns `200`, `text/plain`, and the token echoed back — the application's own
answer, not a platform redirect. A request to a gated endpoint returns the
application's `{"error":"Sign in is required"}` rather than an Easy Auth
challenge, which is the same thing said the other way round: **nothing is
intercepting these requests before the application sees them.**

Identity reaches the API through the web app's proxy and the shared
`Auth__ProxyKey`, and each endpoint decides for itself. So:

- **No Azure change is needed for the webhook.** `clientState` is the
  authentication, which is what it was always for.
- If Easy Auth is ever put in front of this App Service, these two paths must
  be excluded — App Service → Authentication → Edit → **Excluded paths**, or
  `globalValidation.excludedPaths`:

  ```
  /api/integrations/graph/notify
  /api/integrations/graph/lifecycle
  ```

Worth being clear about the other half of that measurement: every `/api/...`
route on this App Service is reachable from the internet, and what protects
them is the application's own capability checks rather than the platform. That
is the existing design, not something the mail work introduced — but it is the
reason those checks are not optional.

### Checking it worked, in order

Each of these tells you something the next one cannot, so run them in order.
Both endpoints are Administrator-only.

1. `GET /api/integrations/graph/status` — configuration and identity, without
   touching a mailbox. It says what is in the way: "no mailboxes set", "not an
   address", "no token", a misconfigured id pair, or (across tenants) a grant
   that is too broad. For the managed identity it also checks the
   `Mail.Read` consent. Across tenants consent is not read off the token,
   because Exchange grants per mailbox, so step 2 is the proof.
2. `POST /api/integrations/graph/test` with `{"mailbox":"<booking mailbox>"}`
   — reads one real message. A 403 here means the **Exchange scoping**: the
   mailbox is not in the management scope yet, or the 30-minute to 2-hour cache
   has not caught up. The answer names `New-ManagementRoleAssignment`. Then
   declare the mailbox and switch it on (D).
3. Once both pass, set `Graph__WebhookBase`. The API restarts, and an hourly
   loop creates and renews the subscriptions from then on. If the Easy Auth
   exclusion is missing, the create fails with a message saying exactly that.


---

## 3. ABS

Nothing can start until these are answered:

- Where is the ABS API, and how does a **machine** authenticate to it — a key, a
  client credential, or reachability over an internal network only?
- Which key joins an ABS record to a job in the SCMOS register?
- Does SCMOS read only, or write back?
- Which roles should see the menu?

### App settings, once known

| Setting | What it is |
|---|---|
| `Abs__Enabled` | `true` / `false` |
| `Abs__BaseUrl` | The API root |
| `Abs__ApiKey` | Key Vault reference |

---

## 4. CCS — Custom Clearance System

The login page at `ccs.leschaco…` is **for a person**. A background integration
cannot use it, and SCMOS must not store somebody's password to pretend to be
them. What is needed is a machine credential.

- Is there an API behind CCS, and how does a service authenticate to it?
- Which number joins a CCS record to an SCMOS job — Reference No., Job No., or
  the container number?
- Which fields does the per-customer transport KPI need that the register does
  not already hold?
- Does CCS push when a job changes, or does SCMOS poll on a schedule?

### App settings, once known

| Setting | What it is |
|---|---|
| `Ccs__Enabled` | `true` / `false` |
| `Ccs__BaseUrl` | The API root |
| `Ccs__ClientId` / `Ccs__ClientSecret` | Key Vault references |

---

## 5. Carrier TMS API

Live since v2.7.52 (20 Sep 2026); writes — accept, decline, the truck's details — since v2.7.53; status events, queued for the job owner's approval like a LINE message, since v2.7.55; webhooks (offer, cancellation, event decided — signed, retried) since v2.7.56; auto-apply for a marked key behind `CarrierApi__AutoApply` since v2.7.57. A carrier's TMS calls
`https://scmos-api-3936.azurewebsites.net/api/carrier/v1/` with a key the
department issues on **Integrations → Carrier API** (needs `ManageSuppliers`).
The contract is [carrier-tms/SCMOS_CARRIER_TMS_API_V1.md](carrier-tms/SCMOS_CARRIER_TMS_API_V1.md);
why it is shaped that way is the [assessment](carrier-tms/SCMOS_CARRIER_TMS_ASSESSMENT.md).

Nothing has to be set for it to work. The key is stored as a hash in
`carrier_api_clients` (migration `20260920013104_CarrierApiClients`, applied by
the release's `--migrate`); the supplier a key speaks for is the row's
`supplier_id`, never anything in the request. Writes are kept by their
`Idempotency-Key` in `carrier_api_requests` (migration
`20260920091226_CarrierApiRequests`) for 30 days, so a retried request is
answered, not run again.

### App settings on the API

| Setting | What it is |
|---|---|
| `CarrierApi__BaseUrl` | Optional. The base URL the screen shows beside a new key, when the API is reached under a name other than its own host (a custom domain, API Management). Unset, the screen shows the host the request came in on — the API's own |
| `CarrierApi__AutoApply` | `on` to let a key the department marked ("เขียนทันที" on the Carrier API screen) write its status events onto the job without the owner's approval. Anything else, or absent, is off — every event waits for the owner. The department's mark alone does nothing; the setting alone does nothing |

Webhooks need no setting: `CarrierWebhookDispatcher` runs with the API and
posts to the URLs the carriers registered (https, public hosts only). The
signing secret lives in `carrier_webhooks.secret` — the one secret SCMOS
holds in plain, because it has to sign with it; it is shown to the carrier
once at registration and never logged. The delivery ledger is
`carrier_webhook_deliveries` (migration `20260920102957_CarrierWebhooks`).

### Checking it worked

```bash
curl -i https://scmos-api-3936.azurewebsites.net/api/carrier/v1/me \
  -H "Authorization: Bearer scmos_ck_…" -H "X-Correlation-Id: check-1"
```

`200` with the supplier the key was issued for, and `X-Correlation-Id: check-1`
echoed back. Without the header: `401 application/problem+json`. With one
carrier's key asking for another carrier's job: `404`.

---

## Verifying a connection without a screen

Each integration exposes `/status` before it exposes anything else. Once the
settings are in place:

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://<api-app>.azurewebsites.net/api/integrations/line/status
```

- `404` — not built yet. The Integrations screen says exactly this today.
- `401` / `403` — built, but this caller has no permission.
- `200` — connected.

The Integrations screen runs this same check and reports the same three answers,
so the day an endpoint appears the screen starts saying so without anyone
editing it.
