import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { authorOf, keyingLabel, nextKeyingFor } from "../app/scmos/keyingFor.ts";

/*
 * Whose name a new job gets while somebody is covering a colleague's leave.
 * The bug this exists for: Uthai delegated to Watsana, Watsana keyed his
 * jobs, and every one of them came back owned by Watsana.
 */

const me = { name: "Watsana", opId: "OP-01" };
const uthai = { id: "OP-02", name: "Uthai", until: "20/09/2026" };
const ananya = { id: "OP-03", name: "Ananya", until: "18/09/2026" };

test("with nothing chosen, a job is one's own", () => {
  assert.deepEqual(authorOf(me, "", [uthai]), { name: "Watsana", opId: "OP-01", forSomeoneElse: false });
});

test("keying for a colleague puts their name and id on the job", () => {
  assert.deepEqual(authorOf(me, "OP-02", [uthai]), { name: "Uthai", opId: "OP-02", forSomeoneElse: true });
});

test("a choice that outlived its grant is oneself, not somebody nobody is covering", () => {
  // Remembered from last week; the grant ended; the covers list no longer has him.
  assert.deepEqual(authorOf(me, "OP-02", []), { name: "Watsana", opId: "OP-01", forSomeoneElse: false });
  assert.deepEqual(authorOf(me, "OP-99", [uthai]).opId, "OP-01");
});

test("the button cycles: oneself, each person covered, oneself again", () => {
  assert.equal(nextKeyingFor("", [uthai]), "OP-02");
  assert.equal(nextKeyingFor("OP-02", [uthai]), "");
  assert.equal(nextKeyingFor("", [uthai, ananya]), "OP-02");
  assert.equal(nextKeyingFor("OP-02", [uthai, ananya]), "OP-03");
  assert.equal(nextKeyingFor("OP-03", [uthai, ananya]), "");
  assert.equal(nextKeyingFor("OP-02", []), "", "and with no covers there is nothing to cycle to");
});

test("the button always names who the job will belong to", () => {
  assert.equal(keyingLabel(authorOf(me, "", [uthai])), "ลงงานให้: ฉัน (Watsana)");
  assert.equal(keyingLabel(authorOf(me, "OP-02", [uthai])), "ลงงานให้: Uthai (แทน)");
});

test("every path that creates a job reads the same author", () => {
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  const me = readFileSync(new URL("../server/Scmos.Api/Endpoints/MeEndpoints.cs", import.meta.url), "utf8");
  // Inserted row, duplicated row, add form, Excel import — none may write me.name.
  assert.equal((app.match(/op: author\.name, opId: author\.opId/g) ?? []).length, 2, "insert and duplicate");
  assert.match(app, /const owner = addForm\.op \|\| author\.name;/);
  assert.match(app, /opIdForName\(owner\) \|\| author\.opId/);
  assert.match(app, /setAddForm\(\{ cat, op: author\.name/);
  assert.match(app, /parseWorkbook\(file, author\.name/);
  assert.doesNotMatch(app, /op: me\.name, opId: me\.opId/);
  // The names come from the same grants the API enforces, not a browser-side list.
  assert.match(me, /covering = \(await delegations\.CoveringForAsync\(user\.OperatorId, token\)\)/);
  assert.match(app, /covering: body\.covering \?\? \[\]/);
});

test("somebody covering a leave can hand a row between themselves and the person covered, and nobody else", () => {
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  const workspace = readFileSync(new URL("../app/scmos/screens/Workspace.tsx", import.meta.url), "utf8");
  // The dropdown, for a delegate, offers only themselves and the people they cover…
  assert.match(workspace, /const offered = canAssign \? M\.operators : \[me\.name, \.\.\.p\.covering\.map\(\(one\) => one\.name\)\];/);
  // …and only on rows they may edit.
  assert.match(workspace, /const handingOver = !canAssign && p\.covering\.length > 0 && canEditJob\(j\);/);
  // The app refuses any other name without the authority to assign, and any row they cannot edit.
  assert.match(app, /\(target === me\.opId \|\| actingFor\.includes\(target\)\)/);
  assert.match(app, /\.filter\(\(j\) => able\("AssignJobs"\) \|\| canEditJob\(j\)\);/);
});
