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

**There is no app registration and no client secret.** This section used to ask
for both. The API already holds a system-assigned managed identity and already
calls Graph with it — that is how the Administration screen invites a colleague
— so `Mail.Read` goes on that same identity. Nothing here is a secret, and
there is nothing to send anybody.

### In Entra ID — you run these

1. Find the API App Service's **system-assigned managed identity** (App Service
   → Identity → System assigned). Note its **Object (principal) ID**.
2. Grant it **Microsoft Graph → `Mail.Read` as an application permission**, and
   **admin-consent it**. Delegated permissions are not usable here — there is
   no signed-in user in a background worker.

   A managed identity has no "API permissions" blade, so this is a PowerShell
   grant rather than a portal click:

   ```powershell
   Connect-MgGraph -Scopes AppRoleAssignment.ReadWrite.All,Application.Read.All
   $graph = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
   $role  = $graph.AppRole | Where-Object { $_.Value -eq 'Mail.Read' -and $_.AllowedMemberTypes -contains 'Application' }
   New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId <object-id> `
       -PrincipalId <object-id> -ResourceId $graph.Id -AppRoleId $role.Id
   ```

3. Nothing else. No secret to create, store, rotate, or hand over.

**The cost of this choice, stated rather than buried:** that one identity then
holds both `User.ReadWrite.All` and `Mail.Read`. A separate registration would
keep them apart, at the price of a secret somebody has to look after. The
Exchange scoping below narrows `Mail.Read` either way. If you would rather have
the separation, say so — it is a small change to `GraphAuth`.

### Then scope it, in Exchange Online PowerShell

Without this step, `Mail.Read` as an application permission reads **every
mailbox in the tenant**. For a forwarding company's mail that is not a
theoretical concern.

```powershell
New-ServicePrincipal -AppId <managed-identity-app-id> -ServiceId <object-id> -DisplayName "SCMOS Mail Reader"
New-ManagementScope -Name "SCMOS Mailboxes" -RecipientRestrictionFilter "CustomAttribute1 -eq 'SCMOS'"
New-ManagementRoleAssignment -App <service-principal> -Role "Application Mail.Read" -CustomResourceScope "SCMOS Mailboxes"
```

Then stamp `CustomAttribute1 = SCMOS` on each mailbox SCMOS may read. Confirm
with `Test-ServicePrincipalAuthorization` that a mailbox outside the scope is
refused — that test is the point of the exercise.

### App settings on the API

Two, and neither is a secret.

| Setting | What it is |
|---|---|
| `Graph__Mailboxes` | Comma-separated addresses SCMOS may read. **Empty approves nothing**, deliberately — the alternative reading is how every mailbox in the tenant becomes readable because somebody forgot a setting |
| `Graph__WebhookBase` | The origin Graph will call, e.g. `https://scmos-api-3936.azurewebsites.net`. Origin only — the paths are fixed in code so they cannot be mistyped here |

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

1. `GET /api/integrations/graph/status` — configuration and consent, without
   touching a mailbox. Says which of "no mailboxes set", "not an address", "no
   token" or "no `Mail.Read` consent" is in the way.
2. `POST /api/integrations/graph/test` with `{"mailbox":"ops@leschaco.co.th"}`
   — reads one real message. A 403 here means the **Exchange scoping**, because
   consent was already proved in step 1; the answer says so and names
   `New-ManagementRoleAssignment`.
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
