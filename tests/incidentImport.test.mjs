import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

test("historical CAR/PAR import checks role and MFA before reading the file", () => {
  const source = readFileSync("server/Scmos.Api/Endpoints/SupplierEndpoints.cs", "utf8");
  const route = source.slice(source.indexOf('incidents.MapPost("/import"'));
  const read = route.indexOf("ReadFormAsync");
  const role = route.indexOf("!user.Can(Capability.CloseCarPar)");
  const mfa = route.indexOf("users.Refuses(user, Capability.CloseCarPar)");
  assert.ok(role >= 0 && role < mfa && mfa < read);
});

test("CAR/PAR import does not save through the open-only RaiseAsync path", () => {
  const source = readFileSync("server/Scmos.Api/Data/IncidentImporter.cs", "utf8");
  assert.doesNotMatch(source, /RaiseAsync/);
  assert.match(source, /IsolationLevel.Serializable/);
  assert.match(source, /audit.Stage/);
  assert.match(source, /ReferenceKey/);
});
