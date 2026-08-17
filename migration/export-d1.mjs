/**
 * Lifts the register out of the local D1 database.
 *
 * D1 is SQLite on disk, so this reads the Miniflare file directly rather than
 * going through wrangler — the dev server can stay up while it runs, and a live
 * server holding the file open is exactly the situation this has to survive.
 *
 *   node migration/export-d1.mjs                    # finds the file itself
 *   node migration/export-d1.mjs path/to/file.sqlite
 *
 * The output is the same shape as the app's own GET /api/jobs response, which is
 * what `dotnet run -- --seed` reads.
 */

import { DatabaseSync } from "node:sqlite";
import { readdirSync, writeFileSync, copyFileSync, rmSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const D1_DIR = join(here, "..", ".wrangler", "state", "v3", "d1", "miniflare-D1DatabaseObject");
const OUT = join(here, "register-export.json");

function findDatabase() {
  const explicit = process.argv[2];
  if (explicit) return explicit;

  const candidates = readdirSync(D1_DIR)
    .filter((name) => name.endsWith(".sqlite") && name !== "metadata.sqlite")
    .map((name) => join(D1_DIR, name));

  if (candidates.length === 0) throw new Error(`No D1 database under ${D1_DIR}`);
  if (candidates.length > 1) throw new Error(`More than one D1 database under ${D1_DIR} — name the one you want`);
  return candidates[0];
}

const source = findDatabase();

// A running dev server keeps the file locked for writing and mid-transaction.
// Reading a copy sidesteps both, and the copy is thrown away afterwards.
const scratch = join(here, ".export-copy.sqlite");
copyFileSync(source, scratch);

let jobs = [];
let skipped = 0;
try {
  const db = new DatabaseSync(scratch, { readOnly: true });
  const rows = db
    .prepare(
      `SELECT data, updated_at FROM operation_jobs
       ORDER BY CASE WHEN work_date IS NULL OR work_date = '' THEN 1 ELSE 0 END, work_date, key`,
    )
    .all();

  for (const row of rows) {
    try {
      jobs.push(JSON.parse(row.data));
    } catch {
      // A row that will not parse is skipped rather than stopping the move,
      // the same as it always was on load.
      skipped++;
    }
  }
  db.close();
} finally {
  rmSync(scratch, { force: true });
}

writeFileSync(OUT, JSON.stringify({ jobs, count: jobs.length }, null, 0), "utf8");

const owners = {};
for (const job of jobs) owners[job.op || "(none)"] = (owners[job.op || "(none)"] ?? 0) + 1;

console.log(`Read   ${source}`);
console.log(`Wrote  ${OUT}`);
console.log(`Jobs   ${jobs.length}${skipped ? ` (${skipped} unreadable rows skipped)` : ""}`);
console.log(`Owners ${Object.entries(owners).map(([name, n]) => `${name} ${n}`).join(" · ")}`);
