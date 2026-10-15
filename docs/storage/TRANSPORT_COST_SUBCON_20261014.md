# Transport cost subcon — implementation handoff, 14 October 2026

Continues Claude's unfinished folder/file implementation on Production base
`bbf90b80`, in `codex/transport-cost-subcon`. The original Claude worktree is
untouched. The user explicitly named the new menu **Transport cost subcon**.
Document Center, rates, billing and existing AI policy/grants remain unchanged.

## Functionality

- Separate staff menu with a folder icon and matching page heading.
- Create folders and subfolders, browse with breadcrumbs, rename and upload
  multiple files sequentially or drag/drop; show individual upload failures.
- Open supported PDF/images/text safely or download Office/other permitted files.
- Duplicate uploads get a numbered name, not an overwrite.
- Soft-delete folders/files into trash. Folder deletion hides its descendants;
  restoring the folder makes them accessible again. Restore a trashed parent first.
- Existing private `IFileStore` / Azure Blob store is reused. Object keys are
  `Shared/{folderId|top}/...`, outside the existing `SCMOS/` archive lifecycle.
  No Azure account, role assignment, lifecycle, access key or public URL is added.

## Access and data safety

- Only recognized accounts with an existing staff role and a nonempty identity
  can read; carriers and unknown/unrecognized identities are denied.
- Existing `UploadDocuments` capability creates folders and uploads files.
  Creator/uploader or Supervisor+ with that capability may rename/trash an item.
  Only Supervisor+ may restore. Read-only staff retain read-only behavior.
- Existing ceiling: 32 MiB per file (33,554,432 bytes); Office/PDF/images/text/ZIP
  allow-list; folder names 120 characters, filenames 200, nested depth at most 8.
- All content is served through authorized API routes, never a public/SAS URL.
  Safe inline types are derived from the filename, never upload MIME claims;
  inline responses retain the existing CSP and nosniff protection.
- Folder creation/file metadata insertion and audit commit together using the
  existing retry-safe atomic-write helper. Other mutations stage their audit
  in the same SaveChanges. Audit-failure rollback is tested against actual SQL.
- Blob and SQL are not a distributed transaction. A failed metadata/audit insert
  can leave an unreferenced blob; no automatic cleanup is added because it could
  destroy paperwork. Such blobs need a separate reviewed reconciliation process.
- No physical file deletion or retention purge exists. Generated migration Down
  refuses to drop metadata; rollback must disable/roll forward, preserving files.

## Migration and deployment

New additive migration: `20261014070615_SharedFiles`, creating `shared_folders`
and `shared_files`, with live-name unique indexes and soft-delete fields.
The EF snapshot was generated from the current Production model. No earlier
migration, business rule, AI policy or privileges were changed.

This handoff does **not** claim a Production release. Commit/push/merge, the
existing API workflow's migration application and Web Production dispatch are
the next release steps, followed by signed-in upload/download/permission smoke
checks using the existing storage account. No Production settings/data were
changed, and no live provider or cloud file uploads were made in this increment.

## Verification

- 51 deterministic/SQL-backed storage checks: names, roles, depth, uploads,
  duplicates, rename, trash/restore, intact bytes, durable audit and rollback.
- 1,392 existing offline API/AI checks passed, including snapshot/runtime parity.
- 841 frontend tests passed; lint/typecheck passed (three existing warnings).
- API Release build passed with zero warnings/errors; Web Production build passed.
- Computer Use browser QA: actual new staff menu, folder creation, visible new
  folder and breadcrumb navigation. A loopback-only synthetic fixture was used;
  it is not evidence of real Blob uploads or Production account access. Temporary
  fixture source, processes and browser tab were removed/stopped afterward.

Run deterministic rules (also wired into existing API CI):

```powershell
dotnet run --project server/Scmos.Api -c Release --no-launch-profile -- --check-shared-files
```

Explicit SQL opt-in, fixed isolated LocalDB only; no configurable connection or
Production target, a fresh GUID database and validated finally cleanup:

```powershell
dotnet run --project server/Scmos.Api -c Release --no-launch-profile -- --check-shared-files --local-db
```

This preserves the existing `.vs/` and `ap8.py` files, the Constitution and AGENTS
text. No further AI enablement or cost/infrastructure change is implied.

## Release authorization — v2.8.25

On 14 October 2026 the user explicitly authorized commit/push, migration
application and Production deployment of this feature. Use the existing
review/CI and Azure release workflows. This authorization does not request
new storage resources, changed role grants, AI enablement or data deletion.
Final deployment/migration evidence must be recorded after the workflows finish;
the pre-release/local evidence above is not proof of a Production upload.

## Opening files in their own programs — v2.8.28 (15 October 2026)

The user asked for files that open in Excel, a PDF reader or a picture program and save back on their own. The SCMOS
tenant holds no Microsoft 365 licence and no SharePoint (Graph: "Tenant does not have a SPO license"), so the user chose
the company's existing Microsoft 365: a folder in a company Teams/SharePoint library, synced to File Explorer by OneDrive.
Files there open in any program and save back; SharePoint keeps their versions. SCMOS only links to it.

- The link is configuration, not code: `SharedFiles__TeamsUrl` on the API App Service, set by the user in the Portal.
  This repository is public, so the company's SharePoint address is never committed.
- `SharedFiles.TeamsLink` hands on only an absolute https link on a `*.sharepoint.com` host (no user info, default port);
  anything else, or nothing configured, shows no button. The link goes out in the staff listing only (carriers never
  reach `/api/shared-files`), and the screen opens it in a new tab with `noopener noreferrer`.
- No Graph permission, app registration or SharePoint integration is added; the SCMOS file space, its online editor and
  version history are unchanged. Opening the library still needs the person's own company sign-in.

## Download only — v2.8.29 (15 October 2026)

With the Teams library in place the user withdrew opening files in the browser ("ยกเลิกการเปิดแบบออนไลน์"), and chose
download only for every type, PDF and pictures included.

- `SharedFiles.OpenInBrowser` (false) is the one switch. The listing reports `online` and `viewable` false, so the screen
  shows a file's name as a download link and offers only ดาวน์โหลด (plus rename/trash for those allowed).
- `/editor`, `/cells` and `/edit` answer 410 with the reason; `/content?inline=1` is served as a download.
- Nothing is removed: the editor's code, `shared_file_revisions` and every stored version stay, and a historical
  version still downloads through `/content?revision=`. Turning the switch back on restores the v2.8.26–28 behaviour.
