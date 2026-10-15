# Transport cost subcon — basic online editor (14 October 2026)

Status: built by the Codex session; released as v2.8.26 (14 Oct 2026) with editing widened to the upload right.
**Withdrawn from use 15 Oct 2026 (v2.8.29), at the user's request:** files are downloaded only; see
`TRANSPORT_COST_SUBCON_20261014.md`. Code, table and stored versions are kept behind `SharedFiles.OpenInBrowser`.
This is a manual file feature. No AI permissions, policies, owners, budgets, credentials or kill switches are changed.
No external document service, public file URL, SAS link or new cloud resource is introduced.

## User workflow

Click the filename or **เปิดออนไลน์**, edit, then **บันทึกฉบับใหม่**. Close/Escape asks before discarding unsaved changes.
Recognized internal staff can read. Saving a new version requires UploadDocuments only: any colleague with the upload
right may edit any live file (the user's choice, 14 Oct 2026), since every save keeps the bytes it replaces. Renaming and
moving to the trash keep the existing rule (the uploader's stable identity or Supervisor-or-higher); restoring stays
Supervisor-or-higher. Carrier accounts remain excluded at the API boundary and in the service; an unknown identity/role
does not acquire access through the new endpoints.

- **XLSX:** inspect visible sheets and change values/types in existing scalar cells, up to 500 changes per save.
  The basic view lists cell addresses, raw values and formulas, rather than reproducing Excel's visual sheet layout.
  Preview/edit area is the first 200 rows and 60 columns. Everything outside it remains in the package.
  Formulas, merged cells, new rows/columns and protected/validation sheets are not editable.
  Macros, signatures, external-data connections/links and pivot packages are refused for edits.
  XLS/XLSM/XLSB remain download-only; the editor never converts them silently.
  Dates remain Excel serial numbers; the original cell number-format remains intact.
  Numeric values use a decimal point, without thousands separators. Text beginning `=` stays literal, not an executable formula.
- **Calculations:** OOXML patches retain original styles, drawings, print settings and untouched package parts.
  Formula expressions are retained, but old cached results are removed and full recalculation is requested.
  There is no browser/server formula recalculation in this editor. Open the saved workbook in Excel and recalculate before
  relying on derived prices. Protected/unsupported features fail closed instead of being rewritten through SheetJS.
- **PDF:** display pages in a self-hosted PDF.js canvas and add Thai/English text to a chosen unrotated page.
  Page, font size and percentage offsets are explicit; text must fit the crop box. Preview must render before saving.
  Existing text is not modified; there is no OCR, form editing or digital signing. Signed/signature-field/XFA/encrypted
  documents are read-only. More than 200 pages require a desktop viewer. PDF actions/scripts are not executed by the canvas.
  Noto Sans Thai and its OFL license are bundled locally; PDF.js worker/fonts/CMaps/WASM/ICC resources come from the pinned
  npm package and are copied to public assets during npm install/dev/build, never loaded from a third-party CDN.
- **JPG/JPEG/PNG/WebP:** rotate by 90 degrees and crop by percentages relative to the rotated image.
  Dimensions are checked before decoding (24 million pixels, 16,000 per side). Exports keep their extension/MIME;
  unsupported browser encoding is an error, not silent conversion. JPG/WebP are re-encoded at 0.95 quality; edited copies
  strip metadata/EXIF. The original retains its original bytes. Other image formats retain existing open/download controls.

## Persistence and migration

Additive migration: `20261014083600_SharedFileEditing`.

- `shared_files`: revision (default 1 for existing files), SQL rowversion, edited-by and edited-at.
- `shared_file_revisions`: immutable previous metadata/object key, unique `(file_id, revision)`, restrictive foreign key.
- A save uploads a new private blob key; it does not overwrite/delete old bytes. In one existing retry-safe SQL transaction,
  reload and recheck authority/live ancestry/version, archive the previous revision, advance the file and stage its audit.
  SQL rowversion prevents stale writes, including after rename/trash. Two writers produce one winner and one HTTP 409.
- Historical downloads go through the same authenticated API and live-file/live-folder guard as the current file.
  Renaming does not rewrite historical names. Trash hides both the current and old contents until normal restoration.
- Storage and SQL are not a distributed transaction. A conflict/audit/SQL failure after blob upload can leave an unreferenced
  private blob, but cannot advance the current file or commit its history without the audit. Do not automatically purge it.
- Migration Down refuses dropping evidence. Roll back application code while retaining this additive schema/history.
  Apply the migration before deploying the API; deploy the Web afterward. Existing revision-1 records need no blob move.
- History adds storage usage; this is not a claim of zero additional storage cost. No extra editor server/subscription is needed.

## Verification

- API Release build, zero warnings/errors (separate scratch output to avoid a pre-existing running API binary lock).
- Deterministic OOXML checks: scalar values, formula/merge/new-cell/DTD/invalid-number refusal, protection/macro/signature/
  external-link guards, unchanged package bytes, retained styles and print settings, invalidated caches and recalculation.
  A standard XLSX created and reopened in memory with the existing ClosedXML dependency retains number formats/formulas
  and calculates the new expected price. No customer workbook is used.
- Full migration + service checks on a fresh GUID database in fixed `(localdb)\ScmosAiAuditCheck_20260907`, with an in-memory
  Blob store. Checks include two simultaneous writers, historical exact bytes, unauthorized writes, rename conflicts,
  audit failure rollback, XLSX preview/save and rejection of arbitrary XLSX replacement. Only the owned scratch DB is removed.
- Existing 1,392 offline API/AI regression checks pass, including model-snapshot parity. No live provider/Production calls.
- Frontend regression, typecheck, lint and optimized Web build. Three pre-existing lint warnings remain.
- Computer Use browser QA uses a disposable local harness/API: XLSX edit/reopen/history, Thai PDF canvas preview/save,
  image rotate/crop/save. The harness is removed before delivery. Screenshots contain synthetic data, not customer documents.

## Release follow-up / existing dependency risk

The user directed the release on the evening of 14 Oct 2026, ahead of the dependency review below; the migration is applied
by the API deploy workflow, not by the local checks.
The initial audit reported 11 issues (10 High, 1 Critical), including Next.js 16.3.1, already in Production before this feature.
The authorized follow-up patches Next.js and its ESLint plugin to 16.3.8 (v2.8.27), preserving the latest v2.8.26 editor/RBAC
and .NET Pkcs patch from the remote branch. The post-patch audit reports 0 Critical and no `next` advisory; 9 other High
entries remain and are not represented as fixed. See `docs/security/NEXTJS_20261014.md` for scope and release evidence.
Do not run an unreviewed blanket `npm audit fix --force`.

Font source: https://github.com/google/fonts/tree/main/ofl/notosansthai
Font SHA256: `5A1C559BB539583C8A1FD99D1C5B9491E5E14478C9CD2BD0970D5C3096CC9EF8`.
