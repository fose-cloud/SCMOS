#!/usr/bin/env bash
#
# A staging slot on both App Services, with its own settings.
#
#   az login
#   RESOURCE_GROUP=rg-scmos WEB_APP=scmos-web API_APP=scmos-api \
#   STAGING_SQL_CONNECTION='Server=...;Database=scmos-staging;...' \
#     ./infra/setup-slot.sh
#
# Run this before the first "Deploy to staging" in GitHub Actions. Idempotent.
#
# The point of the slot is to see the thing running on Azure before anyone
# depends on it, so the two ways a staging slot usually betrays you are closed
# off here:
#
#   1. Slot settings. App settings swap with the slot unless they are marked
#      sticky, so a staging slot inherits production's connection string by
#      default and writes to the live register. Every setting this script sets is
#      marked slot-specific.
#
#   2. Auto-swap. Off, and left off. A slot that promotes itself is not a test.
#
# It does NOT create the staging database — that is a copy-or-create decision
# somebody has to make with the data in front of them. See the end of this file.

set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:?set RESOURCE_GROUP}"
WEB_APP="${WEB_APP:?set WEB_APP}"
API_APP="${API_APP:?set API_APP}"
SLOT="${SLOT:-staging}"
STAGING_SQL_CONNECTION="${STAGING_SQL_CONNECTION:-}"

echo "==> Slot '${SLOT}' on ${API_APP}"
az webapp deployment slot create \
  --name "$API_APP" --resource-group "$RESOURCE_GROUP" --slot "$SLOT" \
  --output none 2>/dev/null || echo "    (already exists)"

echo "==> Slot '${SLOT}' on ${WEB_APP}"
az webapp deployment slot create \
  --name "$WEB_APP" --resource-group "$RESOURCE_GROUP" --slot "$SLOT" \
  --output none 2>/dev/null || echo "    (already exists)"

echo "==> Managed identity on both slots"
API_PRINCIPAL="$(az webapp identity assign \
  --name "$API_APP" --resource-group "$RESOURCE_GROUP" --slot "$SLOT" \
  --query principalId -o tsv)"
az webapp identity assign \
  --name "$WEB_APP" --resource-group "$RESOURCE_GROUP" --slot "$SLOT" \
  --output none

echo "    API slot identity: ${API_PRINCIPAL}"
echo "    Grant it Key Vault Secrets User, Storage Blob Data Contributor,"
echo "    and a contained user in the staging database — same as production."

echo "==> Pointing the web slot at the API slot"
# --slot-settings, not --settings: it sets the value *and* marks it sticky, so a
# swap leaves it behind. Plain --settings would point the staging web app at the
# production API the first time somebody swapped.
az webapp config appsettings set \
  --name "$WEB_APP" --resource-group "$RESOURCE_GROUP" --slot "$SLOT" \
  --slot-settings "SCMOS_API_BASE_URL=https://${API_APP}-${SLOT}.azurewebsites.net" \
  --output none

if [ -n "$STAGING_SQL_CONNECTION" ]; then
  echo "==> Staging database on the API slot"
  az webapp config appsettings set \
    --name "$API_APP" --resource-group "$RESOURCE_GROUP" --slot "$SLOT" \
    --slot-settings "ConnectionStrings__ScmosDb=${STAGING_SQL_CONNECTION}" \
    --output none
else
  echo "==> No STAGING_SQL_CONNECTION given"
  echo "    The slot will read the production connection string it inherits."
  echo "    Set one before deploying, or staging writes to the live register."
fi

echo "==> Making sure auto-swap is off"
az webapp deployment slot auto-swap \
  --name "$API_APP" --resource-group "$RESOURCE_GROUP" --slot "$SLOT" --disable \
  --output none 2>/dev/null || true
az webapp deployment slot auto-swap \
  --name "$WEB_APP" --resource-group "$RESOURCE_GROUP" --slot "$SLOT" --disable \
  --output none 2>/dev/null || true

cat <<EOF

Slots ready.

  web  https://${WEB_APP}-${SLOT}.azurewebsites.net
  api  https://${API_APP}-${SLOT}.azurewebsites.net

Still to do by hand, because each is a decision rather than a step:

  1. A staging database. Either copy production —

       az sql db copy --name scmos --dest-name scmos-staging \\
         --resource-group ${RESOURCE_GROUP} --server <server>

     — or create an empty one and load it with --seed, --migrate-status and
     --seed-suppliers. A copy is the better test: it exercises the migrations
     against real shapes and real volume.

  2. Add SCMOS_SQL_CONNECTION_STAGING to the repository secrets, pointing at it.
     The workflow refuses to migrate a staging slot without it rather than
     falling back to production.

  3. Authentication on the web slot: add
     https://${WEB_APP}-${SLOT}.azurewebsites.net/.auth/login/aad/callback
     as a redirect URI on the app registration, or sign-in will fail on the slot
     while working in production.

  4. Auth__Roles on the API slot, as slot settings, or everybody on staging is
     a Viewer.

Then in GitHub: Actions -> API -> Run workflow -> slot: staging, and the same
for Web. Both smoke-test themselves and fail loudly if the slot does not answer.
EOF
