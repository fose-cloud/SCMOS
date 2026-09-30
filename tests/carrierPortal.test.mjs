import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { CARRIER_VIEW, filterLanes, isoDay, parseCarrierCapacity, parseCarrierKpi, parseCarrierRates, percentText, registerDate } from "../app/scmos/carrierPortal.ts";
import { CARRIER_NAV, CARRIER_SCREENS, CARRIER_SUB_NAV, HEADINGS, NAV, SUB_NAV } from "../app/scmos/nav.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("a Subcontractor's menu is its own: Dashboard, Workspace (NEW job, My job, Postpone), Rate, Billing, KPI", () => {
  assert.deepEqual(CARRIER_NAV.map(([, label]) => label), ["Dashboard", "Workspace", "Capacity", "Rate", "Billing", "KPI"]);
  assert.deepEqual(CARRIER_SUB_NAV.carrierwork.map(([, label]) => label), ["NEW job", "My job", "Postpone"]);
  assert.ok(HEADINGS.includes("carrierwork"));
  // Every page a carrier may open is one of its own, and each has a view.
  for (const screen of CARRIER_SCREENS) assert.ok(CARRIER_VIEW[screen], screen);
  // None of them is a department screen: those read the whole register.
  const department = new Set([...NAV, ...Object.values(SUB_NAV).flat()].map(([key]) => key));
  for (const screen of CARRIER_SCREENS.filter(one => one !== "carrier")) assert.ok(!department.has(screen), screen);
});

test("the app draws the carrier's menu and its screens only for a carrier account", () => {
  const app = read("app/SCMOSApp.tsx");
  assert.match(app, /carrier=\{isCarrier\}/);
  assert.match(app, /isCarrier && screen !== "carrier" && screen !== "carriermyjob" && screen !== "carrierpostpone" && CARRIER_SCREENS\.includes\(screen\) && <CarrierPortal/);
  const chrome = read("app/scmos/Chrome.tsx");
  assert.match(chrome, /if \(p\.carrier\) return/);
});

test("NEW job carries a live offered-work badge in the carrier rail", () => {
  const app = read("app/SCMOSApp.tsx");
  const portal = read("app/scmos/screens/CarrierPortal.tsx");
  const chrome = read("app/scmos/Chrome.tsx");
  assert.match(app, /counts\.carriernew = carrierNewJobs/);
  assert.match(app, /setInterval\(\(\) => \{ void refresh\(\); \}, 30_000\)/);
  assert.match(app, /visibilitychange/);
  assert.match(app, /onNewJobCount=\{setCarrierNewJobs\}/);
  assert.match(portal, /onNewJobCount\?\.\(portalBody\.offered\.length\)/);
  assert.match(chrome, /const count = p\.navCounts\[key\]/);
});

test("the job drawer opens a prefilled Operation Issue from the job", () => {
  const app = read("app/SCMOSApp.tsx");
  const drawer = read("app/scmos/overlays/WorkspaceOverlays.tsx");
  const issues = read("app/scmos/screens/OperationalIssues.tsx");
  const endpoint = read("server/Scmos.Api/Endpoints/OperationalIssueEndpoints.cs");
  assert.match(drawer, />\s*เปิด Operation Issue\s*</);
  assert.match(drawer, /onClick=\{p\.onRaiseIssue\}/);
  assert.doesNotMatch(drawer, /!p\.carrier && <button className="ghost-btn" onClick=\{p\.onRaiseIssue\}/);
  assert.match(app, /jobKey: drawerJob\.key/);
  assert.match(app, /if \(!isCarrier\) go\("issues"\)/);
  assert.match(app, /<RaiseOperationalIssue/);
  assert.match(issues, /export function RaiseOperationalIssue/);
  assert.match(issues, /render(s|ing)? only the new-issue form/i);
  assert.match(endpoint, /access\.CanUseJobAsync\(user, body\.JobKey, token\)/);
  assert.match(endpoint, /บัญชีผู้ขนส่งไม่มีสิทธิ์ดูสรุปปัญหาของทุกบริษัท/);
});

test("Rate and KPI read the carrier's own endpoints and nothing of the department's", () => {
  const rates = read("app/scmos/screens/CarrierRates.tsx");
  const kpi = read("app/scmos/screens/CarrierKpi.tsx");
  assert.match(rates, /"\/api\/carrier\/rates"/);
  assert.match(kpi, /`\/api\/carrier\/kpi\?year=/);
  for (const source of [rates, kpi]) {
    assert.doesNotMatch(source, /\/api\/rates|\/api\/kpi[/?]|\/api\/jobs/);
    assert.doesNotMatch(source, /method: "(POST|PUT|DELETE)"/);   // read only
  }
});

test("Capacity is the department's screen on the carrier's own route, and a carrier never names the supplier", () => {
  // One screen for both since 30 Sep 2026 — the carrier's branch reads and writes /api/carrier/capacity.
  const board = read("app/scmos/screens/CapacityBoard.tsx");
  assert.match(board, /"\/api\/carrier\/capacity\?days=14"/);
  assert.match(board, /apiFetch\("\/api\/carrier\/capacity", \{\n\s+method: "POST", headers: \{ "content-type": "application\/json" \}, body: JSON\.stringify\(counts\),/);
  assert.doesNotMatch(board.match(/const counts = \{[\s\S]*?\};/)[0], /supplierId/);   // the company is the account's, never sent
  assert.match(read("app/scmos/screens/CarrierPortal.tsx"), /<CapacityBoard carrier canEdit canAdmin=\{false\}/);
});

const rates = (over = {}) => ({
  supplierId: 3, supplierName: "ALPHA TRANSPORT",
  bands: [{ label: "30.00-32.99", min: 30, max: 32.99, position: 0 }, { label: "33.00-35.99", min: 33, max: 35.99, position: 1 }],
  lanes: [
    { id: 1, supplierId: 3, carrier: "ALPHA", service: "FCL", customer: "BASF", from: "LCB", to: "Rayong", county: "", remark: "", prices: { "1X40'": [5000, 5100] } },
    { id: 2, supplierId: null, carrier: "ALPHA", service: "LCL", customer: "SCG", from: "BKK", to: "Saraburi", county: "Kaeng Khoi", remark: "night", prices: { "4W": [1800, null] } },
  ],
  ...over,
});

test("the rates answer is read as sent, and refused when its shape is wrong", () => {
  assert.equal(parseCarrierRates(rates()).lanes.length, 2);
  assert.throws(() => parseCarrierRates(null), /invalid_response/);
  assert.throws(() => parseCarrierRates(rates({ lanes: [{ ...rates().lanes[0], prices: { "1X40'": ["5000"] } }] })), /invalid_response/);
  assert.throws(() => parseCarrierRates(rates({ lanes: [{ ...rates().lanes[0], prices: { "1X40'": [1, 2, 3] } }] })), /invalid_response/);
});

test("a lane search reads service, customer, places and remark, every word", () => {
  const lanes = rates().lanes;
  assert.deepEqual(filterLanes(lanes, "", "").map(one => one.id), [1, 2]);
  assert.deepEqual(filterLanes(lanes, "LCL", "").map(one => one.id), [2]);
  assert.deepEqual(filterLanes(lanes, "", "bkk night").map(one => one.id), [2]);
  assert.deepEqual(filterLanes(lanes, "", "rayong scg").map(one => one.id), []);
});

const measure = { id: "OnTimeDelivery", english: "On-Time Delivery", thai: "ส่งมอบตรงเวลา", kind: "Rate", available: true, value: 66.7,
  base: 3, unit: "%", note: "", breakdown: [], target: 95, meetsTarget: false, trend: [{ period: "2026-08", value: 80, base: 5 }, { period: "2026-09", value: 66.7, base: 3 }] };
const score = { carrier: "ALPHA TRANSPORT", shipments: 3, weighted: 92.5, weightAvailable: 90, ungradedAccidents: 0,
  lines: [{ id: "ontime", english: "On-time", thai: "ตรงเวลา", weight: 10, percent: 66.7, count: 1, base: 3, target: 100, note: "" }],
  tally: { transportAccidentMajor: 0, transportAccidentMinor: 1, loadingAccident: 0, complaints: 0, breakdownNoComplaint: 0 } };

test("the KPI answer carries one company's figures, or nothing for a quiet month", () => {
  const kpi = parseCarrierKpi({ supplierId: 3, supplierName: "ALPHA TRANSPORT", year: "2026", month: "09", onTime: measure, score });
  assert.equal(kpi.score.carrier, "ALPHA TRANSPORT");
  assert.equal(parseCarrierKpi({ supplierId: 3, supplierName: "A", year: "2026", month: "", onTime: null, score: null }).score, null);
  assert.throws(() => parseCarrierKpi({ supplierId: 3, supplierName: "A", year: "26", month: "09", onTime: null, score: null }), /invalid_response/);
  assert.throws(() => parseCarrierKpi({ supplierId: 3, supplierName: "A", year: "2026", month: "09", onTime: null,
    score: { ...score, tally: { ...score.tally, complaints: -1 } } }), /invalid_response/);
  assert.equal(percentText(66.66), "66.7%");
  assert.equal(percentText(null), "—");
});

import { missingFields, parseRequests, requestBody, requestSummary } from "../app/scmos/carrierJobRequests.ts";

const form = { fields: { IMPORT: ["customer", "date", "planTime", "type", "destination"] }, essential: { IMPORT: ["customer", "date", "type", "destination"] } };
const request = (over = {}) => ({ id: 4, supplierId: 3, supplierName: "ALPHA TRANSPORT", category: "IMPORT",
  fields: { customer: "BASF", date: "02/10/2026", type: "1X40'", destination: "LKB" }, note: "", status: "PENDING",
  createdBy: "a@carrier.test", createdAt: "2026-09-29T14:00:00Z", decidedBy: "", decidedAt: null, decisionNote: "", jobKey: "", revision: 0, ...over });

test("a carrier's request sends only the category's own fields, trimmed, and says what is still missing", () => {
  assert.deepEqual(requestBody("IMPORT", { customer: " BASF ", date: "02/10/2026", planTime: "", plant: "X" }, form.fields.IMPORT, " call first "),
    { category: "IMPORT", fields: { customer: "BASF", date: "02/10/2026" }, note: "call first" });
  assert.deepEqual(missingFields("IMPORT", { customer: "BASF", date: "02/10/2026" }, form), ["type", "destination"]);
  assert.equal(requestSummary(request()), "IMPORT · BASF · 02/10/2026 · 1X40' · LKB");
});

test("the requests answer is read as sent, and refused when its shape is wrong", () => {
  assert.equal(parseRequests({ items: [request()], form }).items.length, 1);
  assert.equal(parseRequests({ items: [] }).form, null);
  assert.throws(() => parseRequests({ items: [request({ status: "MAYBE" })] }), /invalid_response/);
  assert.throws(() => parseRequests({ items: [request({ fields: { customer: 5 } })] }), /invalid_response/);
});

test("a request opened in the department is the ordinary add-job form, confirmed only after the job is saved", () => {
  const app = read("app/SCMOSApp.tsx");
  assert.match(app, /const openCarrierRequest = \(request: CarrierJobRequest\) => \{\n {4}startAddJob\(request\.category\);/);
  assert.match(app, /trucker: request\.supplierName/);
  assert.match(app, /void flushNow\(\)\.then\(async \(saved\) => \{\n {8}if \(!saved\.ok\) \{ setToast\("บันทึกงานไม่สำเร็จ — คำขอของผู้ขนส่งยังรออยู่"\); return; \}/);
  assert.match(app, /\/api\/carrier-job-requests\/\$\{asked\.id\}\/approve/);
  assert.match(app, /isWorkspace && !isCarrier && able\("EditOwnJobs"\) && \(\n\s+<CarrierRequestsPanel/);
  const portal = read("app/scmos/screens/CarrierPortal.tsx");
  assert.match(portal, /view === "new" && <CarrierJobRequests/);
});

test("a capacity answer is read as sent — spare is available less committed — and dates convert both ways", () => {
  const cell = { date: "01/10/2026", vehicleType: "40F", reported: true, available: 5, committed: 2, jobs: 1, updatedBy: "a@carrier.test", updatedAt: "2026-09-30T02:00:00Z", spare: 3 };
  const ok = { supplierId: 3, supplierName: "ALPHA TRANSPORT", dates: ["01/10/2026"], vehicleTypes: ["4W", "40F"], cells: [cell] };
  assert.equal(parseCarrierCapacity(ok).cells[0].spare, 3);
  assert.throws(() => parseCarrierCapacity({ ...ok, cells: [{ ...cell, spare: 4 }] }), /invalid_response/);
  assert.throws(() => parseCarrierCapacity({ ...ok, cells: [{ ...cell, date: "2026-10-01" }] }), /invalid_response/);
  assert.equal(registerDate("2026-10-01"), "01/10/2026");
  assert.equal(registerDate("01/10/2026"), "");
  assert.equal(isoDay("01/10/2026"), "2026-10-01");
});

test("a carrier's Dashboard is the department's, fed its own jobs and measures", async () => {
  const { parseCarrierDashboardJobs } = await import("../app/scmos/carrierPortal.ts");
  const { TAB_DEFS } = await import("../app/scmos/nav.ts");
  assert.deepEqual(TAB_DEFS.carrier, ["Executive", "Operational"]);            // TODAY is the department's day
  const body = { supplierName: "ALPHA TRANSPORT", offered: 2, jobs: [{ key: "A-1", id: "A-1", status: "COMPLETED", date: "15/09/2026" }] };
  assert.equal(parseCarrierDashboardJobs(body).jobs.length, 1);
  assert.throws(() => parseCarrierDashboardJobs({ ...body, jobs: [{ key: 5 }] }), /invalid_response/);
  const wrapper = read("app/scmos/screens/CarrierDashboard.tsx");
  assert.match(wrapper, /"\/api\/carrier\/dashboard\/jobs"/);
  assert.match(wrapper, /<Dashboard/);
  assert.doesNotMatch(wrapper, /\/api\/kpi|\/api\/dashboard\/|\/api\/jobs/);
  const tower = read("app/scmos/screens/ControlTower.tsx");
  assert.match(tower, /p\.carrier \? "\/api\/carrier\/dashboard\/measures" : "\/api\/kpi\/measures"/);
  assert.match(tower, /const brief = useBrief\(!p\.carrier\);/);                    // the department's briefing is not read
  assert.match(tower, /\{!p\.carrier && <aside className="ct-rail"/);
  const app = read("app/SCMOSApp.tsx");
  assert.match(app, /isCarrier && screen === "carrier" && \(\n\s+<CarrierDashboard/);
});

test("a carrier's My job is the department's workspace over its own register, editing only the field cells", async () => {
  const { CARRIER_EDITABLE } = await import("../app/scmos/carrierPortal.ts");
  const { TAB_DEFS } = await import("../app/scmos/nav.ts");
  // The server decides what a carrier may change; the screen's list must be the same one, plus the status.
  const service = read("server/Scmos.Api/Services/CarrierRegisterService.cs");
  const serverList = /Editable = \[([^\]]*)\]/.exec(service)[1].match(/"([^"]+)"/g).map(s => s.slice(1, -1));
  assert.deepEqual([...CARRIER_EDITABLE].sort(), [...serverList, "status"].sort());
  assert.deepEqual(TAB_DEFS.carriermyjob, TAB_DEFS.myjob);
  const store = read("app/scmos/store.ts");
  assert.match(store, /const registerApi = \(\) => \(carrierRegister \? "\/api\/carrier\/jobs" : API\);/);
  assert.match(store, /if \(carrierRegister\) return null;/);                           // no department page requests
  assert.match(store, /if \(jobs\.length \|\| carrierRegister\) return/);               // an empty register never seeds a plan
  const app = read("app/SCMOSApp.tsx");
  assert.match(app, /isCarrier && screen === "carriermyjob"/);
  assert.match(app, /editableField=\{isCarrier \? carrierMay : undefined\}/);
  assert.match(app, /if \(identityState === "loading"\) return;/);                     // the register waits to know whose it is
});

test("a carrier's status choices are its own steps ahead, the same steps the server takes", async () => {
  const { CARRIER_STEPS, carrierStatusChoices } = await import("../app/scmos/carrierPortal.ts");
  const service = read("server/Scmos.Api/Services/CarrierRegisterService.cs");
  const serverSteps = [...service.matchAll(/JobStatus\.(\w+) => CarrierOperations\./g)]
    .map(m => m[1].replace(/([a-z])([A-Z])/g, "$1_$2").toUpperCase());
  assert.deepEqual([...CARRIER_STEPS].sort(), serverSteps.sort());
  const ladder = ["DRAFT", "RECEIVED", "SUPPLIER_CONFIRMED", "TRUCK_ASSIGNED", "DISPATCHED", "PICKED_UP", "IN_TRANSIT",
    "DELIVERED", "CONTAINER_RETURNED", "DOCUMENT_PENDING", "COMPLETED", "CANCELLED", "HOLD"];
  assert.deepEqual(carrierStatusChoices("SUPPLIER_CONFIRMED", ladder),
    ["DISPATCHED", "PICKED_UP", "IN_TRANSIT", "DELIVERED", "CONTAINER_RETURNED", "COMPLETED"]);
  assert.deepEqual(carrierStatusChoices("delivered", ladder), ["CONTAINER_RETURNED", "COMPLETED"]);
  const drawer = read("app/scmos/overlays/WorkspaceOverlays.tsx");
  assert.match(drawer, /p\.canEdit && !p\.carrier && !isCancelled\(j\)/);       // no moving or cancelling a job
  assert.match(drawer, /<button className="ghost-btn" onClick=\{p\.onRaiseIssue\}/); // but may report its own issue
  assert.doesNotMatch(drawer, /\{!p\.carrier && <button className="ghost-btn" onClick=\{p\.onRaiseIssue\}/);
});

test("a fuel band the lanes shown leave empty is no column on the carrier's Rate", async () => {
  const { pricedBands } = await import("../app/scmos/carrierPortal.ts");
  const bands = [{ label: "33", min: 33, max: 35.99, position: 1 }, { label: "30", min: 30, max: 32.99, position: 0 },
    { label: "36", min: 36, max: 38.99, position: 2 }];
  const lanes = rates().lanes;                                   // lane 1 prices bands 0 and 1; lane 2 band 0 only
  assert.deepEqual(pricedBands(bands, lanes).map(one => one.position), [0, 1]);
  assert.deepEqual(pricedBands(bands, [lanes[1]]).map(one => one.position), [0]);
  assert.deepEqual(pricedBands(bands, []), []);
  assert.match(read("app/scmos/screens/CarrierRates.tsx"), /const bands = pricedBands\(rates\.bands, shown\);/);
});

test("the register asks the carrier, bills what closes, and keeps SCMOS's own measures clean", () => {
  const endpoints = read("server/Scmos.Api/Endpoints/JobsEndpoints.cs");
  assert.match(endpoints, /var followed = await follower\.FollowAsync\(jobs\.LastChanges, user, token\);/);
  assert.match(endpoints, /var billed = await jobs\.BilledAsync\(wanted, token\);[\s\S]*?StatusCodes\.Status409Conflict/);
  const program = read("server/Scmos.Api/Program.cs");
  assert.match(program, /AddScoped<RegisterCarrierFollower>\(\)/);
  assert.match(program, /AddHostedService<BillingCaseSweep>\(\)/);
  // A binding SCMOS writes to bill a job is not the carrier's answer.
  assert.match(read("server/Scmos.Api/Services/KpiEngine.cs"), /Where\(row => CarrierAssignment\.IsCarriersOwn\(row\.ReasonCode\)\)/);
  assert.match(read("server/Scmos.Api/Services/CarrierBillingControlTowerService.cs"), /Where\(row => CarrierAssignment\.IsCarriersOwn\(row\.ReasonCode\)\)/);
  assert.match(read("server/Scmos.Api/Services/CarrierBillingService.cs"), /assignment \?\?= await BindAsync\(actor, supplier, job, token\);/);
});

test("a job's owner may accept for its carrier, from the job's drawer, and only the owner", () => {
  const app = read("app/SCMOSApp.tsx");
  assert.match(app, /apiFetch\(`\/api\/jobs\/\$\{encodeURIComponent\(drawer\)\}\/carrier-ask`/);
  assert.match(app, /apiFetch\(`\/api\/jobs\/\$\{encodeURIComponent\(key\)\}\/carrier-ask\/accept`, \{ method: "POST" \}\)/);
  assert.match(app, /onAcceptForCarrier=\{canEditJob\(drawerJob\) \? \(\) => acceptForCarrier\(drawerJob\.key\) : undefined\}/);
  assert.match(app, /if \(!drawer \|\| isCarrier\) return;/);                       // a carrier's own drawer never asks
  const drawer = read("app/scmos/overlays/WorkspaceOverlays.tsx");
  assert.match(drawer, /\{!p\.carrier && p\.carrierAsk && p\.onAcceptForCarrier && \(/);
  assert.match(drawer, /`รับงานแทน \$\{p\.carrierAsk\.carrier\}`/);
  const endpoints = read("server/Scmos.Api/Endpoints/JobsEndpoints.cs");
  assert.match(endpoints, /MapPost\("\/\{key\}\/carrier-ask\/accept"[\s\S]*?CarrierTenantContext\.IsCarrier\(user\)[\s\S]*?OthersJobsAsync\(\[key\]/);
  // The owner's yes is not the carrier's answer: acceptance measures leave it out.
  assert.match(read("server/Scmos.Api/Rules/CarrierAssignment.cs"), /IsCarriersOwn\(string reasonCode\) => reasonCode is not \(RegisterBinding or OwnerAccepted\)/);
});

test("a carrier's Postpone is the department's Postpone / Cancel, over its own jobs", () => {
  const app = read("app/SCMOSApp.tsx");
  assert.match(app, /\{isCarrier && screen === "carrierpostpone" && \(\n\s+<Postpone carrier me=/);
  assert.match(app, /screen !== "carrierpostpone" && CARRIER_SCREENS\.includes\(screen\)/);   // not the old schedule list
  assert.match(read("app/scmos/store.ts"), /apiFetch\(`\$\{registerApi\(\)\}\/changed`/);
  const screen = read("app/scmos/screens/Postpone.tsx");
  assert.match(screen, /\{!carrier && \(\n\s+<label/);                                    // every row is its own
  assert.match(read("server/Scmos.Api/Endpoints/CarrierEndpoints.cs"), /MapGet\("\/jobs\/changed"/);
});
