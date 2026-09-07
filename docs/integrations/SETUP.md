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
underscore maps to a colon**, so `Graph__ClientId` in Azure is `Graph:ClientId`
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

### In Entra ID — you run these

1. **App registrations → New registration.** Single tenant. Note the
   **Directory (tenant) ID** and **Application (client) ID**.
2. **API permissions → Microsoft Graph → Application permissions → `Mail.Read`.**
   Then **Grant admin consent.** Delegated permissions are not usable here —
   there is no signed-in user in a background worker.
3. **Certificates & secrets** — a certificate if you can, a client secret if not.

### Then scope it, in Exchange Online PowerShell

Without this step, `Mail.Read` as an application permission reads **every
mailbox in the tenant**. For a forwarding company's mail that is not a
theoretical concern.

```powershell
New-ServicePrincipal -AppId <client-id> -ServiceId <object-id> -DisplayName "SCMOS Mail Reader"
New-ManagementScope -Name "SCMOS Mailboxes" -RecipientRestrictionFilter "CustomAttribute1 -eq 'SCMOS'"
New-ManagementRoleAssignment -App <service-principal> -Role "Application Mail.Read" -CustomResourceScope "SCMOS Mailboxes"
```

Then stamp `CustomAttribute1 = SCMOS` on each mailbox SCMOS may read. Confirm
with `Test-ServicePrincipalAuthorization` that a mailbox outside the scope is
refused — that test is the point of the exercise.

### App settings on the API

| Setting | What it is |
|---|---|
| `Graph__Enabled` | `true` / `false` |
| `Graph__TenantId` | Directory (tenant) ID. Not secret |
| `Graph__ClientId` | Application (client) ID. Not secret |
| `Graph__ClientSecret` | Key Vault reference |
| `Graph__Mailboxes` | Comma-separated addresses SCMOS may read |
| `Graph__NotificationUrl` | The public webhook URL Graph will call |
| `Graph__ClientState` | Key Vault reference. A random string SCMOS checks on every notification |

### An Azure decision that blocks everything else

The API sits behind **Easy Auth**, which will reject an unauthenticated POST
from Microsoft. Graph will not sign in. So either:

- exclude `/api/webhooks/microsoft-graph*` from Easy Auth, and let `clientState`
  plus the subscription id be the authentication; or
- give the webhook a separate ingress that is not behind Easy Auth.

This has to be settled before a single notification arrives. It is an Azure
change and it is yours to make.

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
