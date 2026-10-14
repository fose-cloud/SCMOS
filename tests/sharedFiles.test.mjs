import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { CARRIER_SCREENS, NAV, NAV_GROUPS, META } from "../app/scmos/nav.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("Transport cost subcon is its own staff menu and keeps Document Center", () => {
  assert.equal(NAV.find(([key]) => key === "files")?.[1], "Transport cost subcon");
  assert.equal(META.files, "Transport cost subcon");
  assert.ok(NAV_GROUPS.some((group) => group.keys.includes("files")));
  assert.ok(NAV.some(([key]) => key === "documents"));
  assert.ok(!CARRIER_SCREENS.includes("files"));
  assert.match(read("app/SCMOSApp.tsx"), /screen === "files" && <SharedFiles onToast=\{setToast\}/);
  assert.match(read("app/scmos/navIcons.tsx"), /files: P\(/);
});

test("file space supports multiple uploads, server limits, breadcrumb navigation and restore", () => {
  const ui = read("app/scmos/screens/SharedFiles.tsx");
  assert.match(ui, /type="file" multiple/);
  assert.match(ui, /for \(const \[index, file\] of chosen.entries\(\)\)/);
  assert.match(ui, /listing.limits.maxBytes/);
  assert.match(ui, /listing.limits.extensions/);
  assert.match(ui, /aria-label="ตำแหน่งโฟลเดอร์"/);
  assert.match(ui, /\/trash\/restore/);
  assert.match(ui, /maxFolderName/);
  assert.match(ui, /maxFileName/);
  assert.match(ui, /\/content\?inline=1/);
});

test("folder navigation ignores stale loads and prevents duplicate active writes", () => {
  const ui = read("app/scmos/screens/SharedFiles.tsx");
  assert.match(ui, /ticket !== requestId.current/);
  assert.match(ui, /busyRef.current = true/);
  assert.match(ui, /setListing\(null\)/);
  assert.match(ui, /catch \{\s*onToast\("ติดต่อ API ไม่ได้/);
});

test("storage remains private, carrier-excluded and uses the existing store and permissions", () => {
  const service = read("server/Scmos.Api/Services/SharedFilesService.cs");
  assert.match(service, /IFileStore files, AuditService audit/);
  assert.match(service, /user.Recognised/);
  assert.match(service, /Roles.Find\(user.Role\)/);
  assert.match(service, /role.Name != Roles.Subcontractor/);
  assert.match(service, /Capability.UploadDocuments/);
  assert.match(service, /InlineViewing.TypeFor\(name\) \?\? "application\/octet-stream"/);
  const endpoints = read("server/Scmos.Api/Endpoints/SharedFilesEndpoints.cs");
  assert.match(endpoints, /SharedFilesService.CanSee\(user\)/);
  assert.match(endpoints, /X-Content-Type-Options/);
  assert.match(endpoints, /Content-Security-Policy/);
  assert.match(endpoints, /Results.File\(content!, file!.ContentType, file.Name/);
  assert.doesNotMatch(endpoints, /GenerateSas|public-access|blob\.core\.windows/);
});

test("trash is soft-delete only and rollback cannot destroy folder/file metadata", () => {
  const service = read("server/Scmos.Api/Services/SharedFilesService.cs");
  assert.match(service, /folder.DeletedAt = DateTimeOffset.UtcNow/);
  assert.match(service, /file.DeletedAt = DateTimeOffset.UtcNow/);
  assert.doesNotMatch(service, /DeleteAsync|Remove\(|RemoveRange\(/);
  const migration = read("server/Scmos.Api/Data/Migrations/20261014070615_SharedFiles.cs");
  assert.match(migration, /throw new NotSupportedException/);
  assert.doesNotMatch(migration, /DropTable|DropColumn|DeleteData/);
});

test("create/upload use the existing atomic audit helper and CI runs offline storage rules", () => {
  const service = read("server/Scmos.Api/Services/SharedFilesService.cs");
  assert.equal((service.match(/EvaluationWrites.InOneAsync/g) ?? []).length, 2);
  assert.doesNotMatch(service, /audit.RecordAsync/);
  assert.match(read(".github/workflows/api.yml"), /queue links review shared-files; do/);
  const checks = read("server/Scmos.Api/Data/SharedFilesCheck.cs");
  assert.match(checks, /if \(!args.Contains\("--local-db"\)\)/);
  assert.match(checks, /Guid.NewGuid\(\)/);
  assert.match(checks, /Unsafe storage test cleanup target/);
  assert.doesNotMatch(checks, /\[connection string\]|args\[flag \+ 1\]/);
});
