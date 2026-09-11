import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const screen = readFileSync("app/scmos/screens/CustomerTrainingRegister.tsx", "utf8");
const endpoint = readFileSync("server/Scmos.Api/Endpoints/TrainingEndpoints.cs", "utf8");
const migration = readFileSync(
  "server/Scmos.Api/Data/Migrations/20260903132244_CustomerTrainingRegister.cs", "utf8");

test("Customer Training register preserves the nine workbook columns in order", () => {
  const headings = [
    "ลำดับ", "ชื่อหลักสูตร/ลูกค้า", "ชื่อ", "นามสกุล", "บริษัท",
    "เลขที่ใบขับขี่", "ประเภทใบขับขี่", "Effective date", "Expire date",
  ];
  let after = -1;
  for (const heading of headings) {
    const at = screen.indexOf(`"${heading}"`, after + 1);
    assert.ok(at > after, `${heading} should appear after the preceding workbook column`);
    after = at;
  }
});

test("the register warns only below 60 days and marks today as expired", () => {
  assert.match(endpoint, /<= 0 => TrainingRules\.Expired/,
    "an expiry today must not still read as valid");
  assert.match(endpoint, /< 60 => TrainingRules\.ExpiringSoon/,
    "59 days must warn while exactly 60 days remains valid");
  assert.match(endpoint, /alertBeforeDays = 60/,
    "the API must publish the same warning threshold the table explains");
});

test("the database migration stores every workbook value", () => {
  for (const column of [
    "sequence_no", "course_customer", "first_name", "last_name", "company",
    "driver_license_no", "license_type", "effective_date", "expiry_date",
  ]) {
    assert.match(migration, new RegExp(`\\b${column} = table\\.Column`),
      `${column} is missing from customer_training_records`);
  }
});

test("repeat imports are checked for duplicates before rows are inserted", () => {
  assert.match(endpoint, /if \(!known\.Add\(key\)\)/);
  assert.match(endpoint, /skipped\+\+/);
});

test("manual training rows require ManageTraining, which Operation already holds", () => {
  const roles = readFileSync("server/Scmos.Api/Rules/Roles.cs", "utf8");
  assert.match(roles, /OperationGrants =[\s\S]*?Capability.ManageTraining/);
  const save = endpoint.slice(endpoint.indexOf("private static async Task<IResult> SaveRegisterAsync"));
  assert.match(save, /!user.Can\(Capability.ManageTraining\)/);
  assert.ok(save.indexOf("Capability.ManageTraining") < save.indexOf("db.CustomerTrainingRecords"));
  assert.match(endpoint, /group.MapPost\("\/register"/);
  assert.match(endpoint, /group.MapPut\("\/register\/\{id:long\}"/);
});

test("manual register saves validate dates and write audit atomically", () => {
  const save = endpoint.slice(endpoint.indexOf("private static async Task<IResult> SaveRegisterAsync"));
  assert.match(save, /expiry < effective/);
  assert.match(save, /r.Id != id/);
  assert.match(save, /TrainingRules.ParseDate\(r.EffectiveDate\) == effective/);
  assert.match(save, /IsolationLevel.Serializable/);
  assert.ok(save.indexOf("audit.Stage") < save.indexOf("work.CommitAsync"));
});

test("register editor is permission gated and does not discard rejected input", () => {
  assert.match(screen, /canEdit && <button[\s\S]*?แทรกแถว/);
  assert.match(screen, /method: editingId === null \? "POST" : "PUT"/);
  assert.match(screen, /if \(!response.ok\) \{ setSaveError[\s\S]*?return; \}/);
  assert.match(screen, /บันทึกแถว/);
  assert.match(screen, /คำนวณหลังบันทึก/);
});
