#!/usr/bin/env bash
#
# Storage for SCMOS documents: the account, the private container, the app's
# access to it, and the ten-year lifecycle.
#
# Run once per environment by somebody signed in to the subscription:
#
#   az login
#   RESOURCE_GROUP=rg-scmos STORAGE_ACCOUNT=scmosfiles API_APP=scmos-api ./infra/setup-storage.sh
#
# Every step is idempotent, so re-running it after a change is safe.
#
# What it deliberately does NOT do: grant public access, create a shared access
# key for the app, or add a delete rule to the lifecycle. See infra/storage-lifecycle.json.

set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:?set RESOURCE_GROUP}"
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:?set STORAGE_ACCOUNT}"
API_APP="${API_APP:?set API_APP (the App Service running Scmos.Api)}"
LOCATION="${LOCATION:-southeastasia}"
CONTAINER="${CONTAINER:-operation-files}"

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> Storage account ${STORAGE_ACCOUNT}"
# Hot by default because this year's paperwork is read often; the lifecycle
# moves it down. Versioning is on so an overwrite is recoverable — uploads
# already never overwrite, but a mistaken az CLI call is a different matter.
az storage account create \
  --name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku Standard_LRS \
  --kind StorageV2 \
  --access-tier Hot \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false \
  --https-only true \
  --output none

az storage account blob-service-properties update \
  --account-name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --enable-versioning true \
  --enable-delete-retention true \
  --delete-retention-days 30 \
  --output none

echo "==> Private container ${CONTAINER}"
az storage container create \
  --name "$CONTAINER" \
  --account-name "$STORAGE_ACCOUNT" \
  --auth-mode login \
  --public-access off \
  --output none

echo "==> Managed identity on ${API_APP}"
az webapp identity assign \
  --name "$API_APP" \
  --resource-group "$RESOURCE_GROUP" \
  --output none

PRINCIPAL_ID="$(az webapp identity show --name "$API_APP" --resource-group "$RESOURCE_GROUP" --query principalId -o tsv)"
SCOPE="$(az storage account show --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --query id -o tsv)"

# Storage Blob Data Contributor, not Owner: the app writes and reads blobs and
# has no business changing the account's configuration.
echo "==> Granting Storage Blob Data Contributor"
az role assignment create \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" \
  --scope "$SCOPE" \
  --output none 2>/dev/null || echo "    (already granted)"

echo "==> Lifecycle policy — Hot 1y, Cool to 3y, Archive to 10y, no deletion"
az storage account management-policy create \
  --account-name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --policy "@${here}/storage-lifecycle.json" \
  --output none

echo "==> Pointing the API at it"
az webapp config appsettings set \
  --name "$API_APP" \
  --resource-group "$RESOURCE_GROUP" \
  --settings \
    "Storage__ServiceUri=https://${STORAGE_ACCOUNT}.blob.core.windows.net" \
    "Storage__Container=${CONTAINER}" \
  --output none

cat <<EOF

Done.

  account    ${STORAGE_ACCOUNT}
  container  ${CONTAINER}  (private)
  identity   ${PRINCIPAL_ID}
  lifecycle  Hot 365d -> Cool 1095d -> Archive, no delete action

Check it by uploading through the app; if Storage is misconfigured the API
answers 503 with the reason rather than failing quietly.

Retention end raises a review at GET /api/documents/retention. Nothing in the
policy or the code deletes a document — that stays a decision a person records
and then carries out here.
EOF
