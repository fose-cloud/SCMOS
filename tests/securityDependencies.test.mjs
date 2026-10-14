import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = path => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const manifest = JSON.parse(read("package.json"));
const lock = JSON.parse(read("package-lock.json"));

test("Next.js stays pinned past the known Critical and September 2026 security ranges", () => {
  const version = manifest.dependencies.next;
  assert.match(version, /^\d+\.\d+\.\d+$/);
  const [major, minor, patch] = version.split(".").map(Number);
  assert.ok(major > 16 || (major === 16 && (minor > 3 || (minor === 3 && patch >= 8))), "Next.js must not regress below 16.3.8");
  assert.equal(lock.packages["node_modules/next"].version, version);
  assert.equal(manifest.devDependencies["@next/eslint-plugin-next"], version);
  assert.equal(lock.packages["node_modules/@next/eslint-plugin-next"].version, version);
  assert.equal(lock.packages["node_modules/@next/env"].version, version);
});

test("the Next image dependency is patched and security checks cannot be bypassed for release", () => {
  const sharp = lock.packages["node_modules/sharp"].version;
  const [major, minor, patch] = sharp.split(".").map(Number);
  assert.ok(major > 0 || minor > 35 || (minor === 35 && patch >= 5));
  assert.match(read(".github/workflows/web.yml"), /npm audit --audit-level=critical/);
  assert.doesNotMatch(read("next.config.ts"), /ignoreBuildErrors\s*:\s*true/);
});
