import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { AGENT_LABEL, answerBody, answerError, draftText, SENT_CHANNELS, inReadingOrder, parseDecisions, RISK_LABEL, RISK_ORDER } from "../app/scmos/aiFindings.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

const decision = (over = {}) => ({
  id: 1, runId: "", agentId: "otd-agent", decisionType: "otd_risk", entityType: "job", entityId: "J1",
  summary: "J-1 · เลยเวลาแผน 65 นาที ยังไม่มีเวลาถึง", resultStatus: "COMPLETED", status: "OPEN", riskLevel: "CRITICAL",
  confidence: null, requiresApproval: false, shadow: true, autonomy: 2, promptVersion: "build:abc",
  findings: {
    facts: [{ text: "แผน 28/09/2026 08:00", source: "job:J1.date+planTime" }],
    ruleResults: [{ text: "ถึงภายใน 08:30 จึงนับตรงเวลา", source: "CustomerTerms.Default" }],
    observations: [], inferences: [{ text: "งานนี้มีแนวโน้มไม่ตรงเวลา", source: null }],
    recommendations: [{ text: "ติดต่อ SHORE เพื่อยืนยันเวลาถึงจริง", source: null }], blockingIssues: [],
  },
  ruleReferences: ["OTD.PAST_WINDOW_NO_ARRIVAL"], evidenceReferences: ["job:J1"], humanChoice: "", humanMatches: null,
  overrideReason: "", decidedBy: "", decidedAt: null, createdAt: "2026-09-28T03:00:00Z", canAnswer: true,
  ...over,
});
const page = (items) => ({ items, total: items.length, page: 1, pageSize: 200 });
const rejects = (value) => assert.throws(() => parseDecisions(value), /invalid_response/);

test("the open decisions the API sends are read as they are", () => {
  assert.equal(parseDecisions(page([decision()])).items.length, 1);
});

test("a decision that mixes or loses its evidence is not drawn", () => {
  const withoutSource = decision();
  withoutSource.findings.facts = [{ text: "แผน 08:00", source: null }];
  rejects(page([withoutSource]));                                     // a fact must name its source
  const noRuleSource = decision();
  noRuleSource.findings.ruleResults = [{ text: "ถึงภายใน 08:30", source: null }];
  rejects(page([noRuleSource]));
  const missingList = decision();
  delete missingList.findings.inferences;
  rejects(page([missingList]));                                        // the four kinds are always there, apart
  rejects(page([decision({ riskLevel: "SEVERE" })]));
  rejects(page([decision({ entityId: "" })]));
  rejects(page([decision({ canAnswer: "yes" })]));                                    // whether I may answer is the server's yes or no
  rejects({ items: [decision()], total: 0 });                           // total below what was sent
});

test("the most serious come first, then the newest", () => {
  const order = inReadingOrder([
    decision({ id: 1, riskLevel: "LOW" }), decision({ id: 2, riskLevel: "WATCH" }),
    decision({ id: 3, riskLevel: "CRITICAL", createdAt: "2026-09-28T01:00:00Z" }), decision({ id: 4, riskLevel: "CRITICAL" }),
  ]).map(d => d.id);
  assert.deepEqual(order, [4, 3, 2, 1]);
  assert.ok(RISK_ORDER.every(level => level === "" || Object.hasOwn(RISK_LABEL, level)));
});

test("the Carrier Agent's recommendations are a filter of their own, named for what they are about", () => {
  assert.equal(AGENT_LABEL["vendor-agent"], "ผู้ขนส่ง");
  assert.match(read("app/scmos/screens/AiFindingsPanel.tsx"), /const AGENT_FILTERS = \["otd-agent", "validation-agent", "vendor-agent", /);
});

test("an answer is sent trimmed; an override carries what was done and why", () => {
  assert.deepEqual(answerBody("OVERRIDDEN", "  Carrier B ", " no DG driver "), { outcome: "OVERRIDDEN", choice: "Carrier B", reason: "no DG driver" });
  assert.deepEqual(answerBody("ACCEPTED"), { outcome: "ACCEPTED", choice: "", reason: "" });
  assert.equal(answerError("forbidden"), "เจ้าของงานหรือหัวหน้างานเท่านั้นที่ตอบได้");
  assert.equal(answerError("?"), "บันทึกไม่สำเร็จ ลองใหม่");
});

test("the panel shows the four kinds apart, answers through the control header, and sits in the Control Tower", () => {
  const panel = read("app/scmos/screens/AiFindingsPanel.tsx");
  const tower = read("app/scmos/screens/AiControlTower.tsx");
  for (const label of ["ข้อเท็จจริง", "ผลตามกฎ", "ข้อสันนิษฐาน", "ข้อแนะนำ"]) assert.ok(panel.includes(`title="${label}"`), label);
  assert.match(panel, /"X-SCMOS-AI-Control": "1"/);
  assert.match(panel, /\/api\/ai\/decisions\?status=OPEN/);
  assert.match(panel, /disabled=\{busy !== null \|\| !choice\.trim\(\) \|\| !reason\.trim\(\)\}/);
  assert.match(tower, /\{canViewDashboard && <AiFindingsPanel onOpenJob=\{onOpenJob\} onDraftJob=\{onDraftJob\} onAnswered=\{tasks\.refresh\} \/>\}/);
});

test("a Communication draft is its template's message; any other decision has none", () => {
  const message = "เรียน ACN รบกวนยืนยันรับงาน J-1 ลูกค้า BASF วันที่ 29/09/2026 เวลา 09:00 (ขอรถเมื่อ 28/09 08:45) ขอบคุณ";
  const draft = decision({
    agentId: "communication-agent", decisionType: "communication_draft", riskLevel: "",
    findings: { ...decision().findings, recommendations: [{ text: message, source: "template:CARRIER_CONFIRMATION_REMINDER" }] },
  });
  assert.equal(parseDecisions(page([draft])).items.length, 1);
  assert.equal(draftText(draft), message);
  assert.equal(draftText(decision()), null);                                         // an OTD finding is not a message
  assert.equal(draftText({ ...draft, findings: { ...draft.findings, recommendations: [{ text: message, source: null }] } }), null);
  assert.equal(AGENT_LABEL["communication-agent"], "ข้อความ");
  assert.deepEqual([...SENT_CHANNELS], ["LINE", "โทรศัพท์", "อีเมล"]);
  assert.deepEqual(answerBody("ACCEPTED", "LINE"), { outcome: "ACCEPTED", choice: "LINE", reason: "" });
});

test("the panel shows a draft to copy and asks whether it was sent — SCMOS sends nothing", () => {
  const panel = read("app/scmos/screens/AiFindingsPanel.tsx");
  for (const label of ["ร่างข้อความ", "คัดลอกข้อความ", "ส่งแล้ว", "ส่งข้อความอื่น", "ไม่ส่ง"]) assert.ok(panel.includes(`>${label}<`), label);
  assert.match(panel, /navigator\.clipboard\.writeText/);
  assert.match(panel, /answer\(item\.id, "ACCEPTED", channel\)/);
  assert.match(panel, /const AGENT_FILTERS = \["otd-agent", "validation-agent", "vendor-agent", "communication-agent", "booking-agent"\] as const/);
  assert.match(panel, /\(\["all", \.\.\.AGENT_FILTERS\] as const\)/);
  assert.match(panel, /\{item\.riskLevel && <span/);                                // no empty risk badge on a draft
  assert.doesNotMatch(panel, /\/api\/line|push|sendMessage/i);                       // nothing here sends
});

test("several findings and carrier messages can be answered at once; booking drafts still one by one (6 Oct 2026)", async () => {
  const { canBatch, answerMany, batchAnswers } = await import("../app/scmos/aiFindings.ts");
  assert.equal(canBatch({ canAnswer: true, decisionType: "otd_risk" }), true);
  assert.equal(canBatch({ canAnswer: false, decisionType: "otd_risk" }), false);
  assert.equal(canBatch({ canAnswer: true, decisionType: "communication_draft" }), true);
  assert.equal(canBatch({ canAnswer: false, decisionType: "communication_draft" }), false);
  assert.equal(canBatch({ canAnswer: true, decisionType: "booking_draft" }), false);
  const message = { decisionType: "communication_draft" }, finding = { decisionType: "otd_risk" };
  assert.equal(batchAnswers([message, message]), "messages");
  assert.equal(batchAnswers([finding]), "findings");
  assert.equal(batchAnswers([message, finding]), "mixed");
  const seen = [];
  let inFlight = 0, widest = 0;
  const result = await answerMany([1, 2, 3, 4, 5, 6, 7], async id => {
    inFlight++; widest = Math.max(widest, inFlight);
    await new Promise(resolve => setTimeout(resolve, 5));
    inFlight--; seen.push(id);
    return id === 3 ? "already_answered" : id === 6 ? null : null;
  }, undefined, 3);
  assert.deepEqual(seen.sort(), [1, 2, 3, 4, 5, 6, 7]);
  assert.equal(widest, 3);
  assert.deepEqual(result, { saved: 6, refused: ["already_answered"] });
  const thrown = await answerMany([9], async () => { throw new Error("network"); });
  assert.deepEqual(thrown, { saved: 0, refused: ["unavailable"] });
  const panel = read("app/scmos/screens/AiFindingsPanel.tsx");
  // Each answer goes through its own route, after a confirmation, and only plain findings carry a box.
  assert.match(panel, /answerMany\(ids, async id => \{\s*const response = await apiFetch\(`\/api\/ai\/decisions\/\$\{id\}\/outcome`/);
  assert.match(panel, /onClick=\{\(\) => setConfirming\("ACCEPTED"\)\}/);
  assert.match(panel, /void answerPicked\(confirming, confirmLabel, confirming === "ACCEPTED" && kind === "messages" \? channel : ""\)/);
  assert.match(panel, /\{canBatch\(item\) && <input type="checkbox"/);
  // The bar is the panel's last child and held to the bottom: a top-sticky bar went under the sticky page header.
  assert.match(panel, /\{shown\.map\(renderItem\)\}<\/ul><\/>\}\s*\{\/\*[^*]*\*\/\}\s*\{picked\.size > 0 && <div className=\{s\.bulkBar\}/);
  assert.match(read("app/scmos/screens/AiControlTower.module.css"), /\.bulkBar \{ position: sticky; bottom: 12px;/);
  // The tab's badge is read again after an answer, one at a time or several.
  assert.match(panel, /if \(result\.saved > 0\) onAnswered\?\.\(\);/);
  assert.match(read("app/scmos/screens/AiControlTower.tsx"), /<AiFindingsPanel [^>]*onAnswered=\{tasks\.refresh\}/);
  // Messages are saved as sent by the chosen channel; "right" is offered only when no message is picked.
  assert.match(panel, /const sentLabel = `ส่งแล้วทาง \$\{channel\}`/);
  assert.match(panel, /\{kind === "findings" && <button[^>]*onClick=\{\(\) => setConfirming\("ACCEPTED"\)\}>ถูกต้อง<\/button>\}/);
});
