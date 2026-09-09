// Optional isolated browser QA. Start ai-control-preview.mjs first.
// SCMOS_QA_PLAYWRIGHT points at an existing Playwright module, never a provider key.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { mkdir } from "node:fs/promises";
const require = createRequire(import.meta.url);
const { chromium, expect } = require(process.env.SCMOS_QA_PLAYWRIGHT || "playwright/test");
const base = "http://127.0.0.1:4107";
const fixture = "http://127.0.0.1:4108/__fixture";
async function inspect() {
  const result = await (await fetch(fixture)).json();
  assert.equal(result.fixture, "scmos-ai-local-qa", "refuse to test an unrecognized server");
  return result;
}
async function mode(value) {
  await inspect();
  const result = await fetch(fixture + "?mode=" + value, { method: "POST" });
  assert.equal(result.status, 200);
}
await inspect();
await mkdir("outputs/ai-qa", { recursive: true });
const browser = await chromium.launch({ channel: "msedge", headless: true });
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, acceptDownloads: false, reducedMotion: "reduce" });
// No app request can leave the fixture origin; no signed-in browser/profile is reused.
await context.route("**/*", async route => {
  const url = new URL(route.request().url());
  if (url.origin !== base) return route.abort();
  if (url.pathname === "/cargo-logo.png")
    return route.fulfill({ path: new URL("../public/cargo-logo.png", import.meta.url).pathname.replace(/^\/([A-Z]:)/, "$1") });
  return route.continue();
});
const page = await context.newPage();
page.setDefaultTimeout(15000);
const errors = [];
page.on("pageerror", error => errors.push(error.message));
const tower = page.getByTestId("ai-control-tower");
const ask = () => tower.getByRole("button", { name: "ถาม Operations AI →", exact: true });
const question = () => page.getByLabel("ต้องการตรวจสอบเรื่องอะไร?");
const pass = label => console.log("PASS: " + label);
async function load(value) {
  await mode(value);
  await page.goto(base + "/ai-control-tower");
  await expect(tower.getByRole("heading", { name: "Morning Brief", exact: true })).toBeVisible();
  await expect(tower.getByText("กำลังตรวจสถานะ AI…", { exact: true })).toHaveCount(0);
}
try {
  await load("control");
  const toggle = () => tower.getByRole("button", { name: "เปิด Operations AI", exact: true });
  await expect(toggle()).toBeEnabled();
  await question().fill("FIXTURE ONLY งานวันนี้");
  await expect(ask()).toBeDisabled();
  await toggle().click();
  await tower.getByRole("button", { name: "ยกเลิก", exact: true }).click();
  assert.equal((await inspect()).calls.filter(c => c.path === "/api/ai/operations-control").length, 0);
  await toggle().click();
  await tower.getByRole("button", { name: "ยืนยัน", exact: true }).click();
  await expect(tower.getByRole("button", { name: "ปิด Operations AI", exact: true })).toBeVisible();
  await expect(ask()).toBeEnabled();
  await page.reload();
  await expect(tower.getByRole("button", { name: "ปิด Operations AI", exact: true })).toBeVisible();
  await tower.getByRole("button", { name: "ปิด Operations AI", exact: true }).click();
  await tower.getByRole("button", { name: "ยืนยัน", exact: true }).click();
  await expect(toggle()).toBeVisible();
  await expect(ask()).toBeDisabled();
  assert.equal((await inspect()).calls.filter(c => c.path === "/api/ai/chat").length, 0);
  await page.screenshot({ path: "outputs/ai-qa/operations-switch-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await toggle().click();
  await expect(tower.getByRole("button", { name: "ยืนยัน", exact: true })).toBeVisible();
  assert.ok(await tower.evaluate(element => element.scrollWidth <= element.clientWidth), "switch confirmation fits mobile");
  await page.screenshot({ path: "outputs/ai-qa/operations-switch-mobile.png", fullPage: true });
  await tower.getByRole("button", { name: "ยกเลิก", exact: true }).click();
  await page.setViewportSize({ width: 1440, height: 1000 });
  pass("Operations switch: explicit confirm/cancel, persistence, disable, no automatic chat");
  await load("control-unready");
  await expect(toggle()).toBeDisabled();
  await load("control-conflict");
  await toggle().click();
  await tower.getByRole("button", { name: "ยืนยัน", exact: true }).click();
  await expect(tower.getByText("มีผู้เปลี่ยนสถานะแล้ว กรุณาตรวจสถานะล่าสุดก่อนลองใหม่", { exact: true })).toBeVisible();
  await expect(ask()).toBeDisabled();
  pass("Operations switch: readiness and conflict fail closed");
  await load("ready");
  await expect(toggle()).toHaveCount(0);
  await expect(tower.getByText("Operations AI พร้อมรับคำถาม", { exact: true })).toBeVisible();
  await expect(tower.getByRole("heading", { name: "Priority Queue", exact: true })).toBeVisible();
  await expect(tower.getByText("N/A", { exact: true }).first()).toBeVisible();
  await tower.getByRole("button", { name: "สรุปงานวันนี้", exact: true }).click();
  await expect(question()).toHaveValue("สรุปงานวันนี้");
  assert.equal((await inspect()).calls.filter(c => c.method === "POST").length, 0);
  pass("page, real-zero/N/A state, prompt chip never auto-submits");
  await question().press("Control+Enter");
  await expect(tower.getByRole("button", { name: "TEST-JOB-001 ↗", exact: true })).toBeVisible();
  await expect(tower.getByText(/แสดง 1 จาก 2 งาน/)).toBeVisible();
  assert.equal((await inspect()).calls.filter(c => c.method === "POST").length, 1);
  pass("Ctrl+Enter submits once and displays source evidence/truncation");
  await tower.getByRole("button", { name: "ดู Audit ของคำตอบนี้", exact: true }).click();
  await expect(tower.getByRole("heading", { name: "รายละเอียดรอบการทำงาน" })).toBeVisible();
  await expect(tower.locator("ol li")).toHaveCount(4);
  await tower.locator('button[aria-pressed]').filter({ hasText: "bbbbbbbb" }).click();
  await expect(tower.getByText("ไม่พบเหตุการณ์จบรอบ ห้ามถือว่ารอบนี้ทำงานสำเร็จ")).toBeVisible();
  await expect(tower.locator("ol li")).toHaveCount(2);
  pass("Audit selection changes timeline; incomplete never shown as success");
  await tower.getByRole("button", { name: "รายการเก่ากว่า →" }).click();
  await expect(tower.getByText("ยังไม่มีรอบการทำงานที่บันทึกไว้ในหน้านี้")).toBeVisible();
  await tower.getByRole("button", { name: "กลับรายการล่าสุด" }).click();
  await expect(tower.locator('button[aria-pressed]')).toHaveCount(2);
  pass("Audit cursor and latest navigation");
  await page.screenshot({ path: "outputs/ai-qa/desktop-activity.png", fullPage: true });
  await tower.getByRole("button", { name: "TEST-JOB-001 ↗", exact: true }).click();
  await expect(page.getByText("TEST DESTINATION", { exact: true }).first()).toBeVisible();
  assert.ok((await inspect()).calls.some(c => c.path === "/api/jobs"));
  pass("evidence opens the requested job even when it is absent from the current page");
  await load("slow");
  await question().fill("งานวันนี้");
  await ask().click();
  await question().press("Control+Enter");
  await tower.getByRole("button", { name: "หยุดรอ", exact: true }).click();
  // The deliberate slow fixture resolves later; this proves no stale answer reappears.
  await page.waitForTimeout(4500);
  await expect(tower.getByRole("button", { name: "TEST-JOB-001 ↗", exact: true })).toHaveCount(0);
  assert.equal((await inspect()).calls.filter(c => c.method === "POST").length, 1);
  pass("duplicate submit blocked and late result ignored after cancel");
  await load("disabled"); await question().fill("งานวันนี้"); await expect(ask()).toBeDisabled();
  await expect(tower.getByText("SCMOS AI ยังปิดอยู่", { exact: true })).toBeVisible();
  await load("audit-missing"); await question().fill("งานวันนี้"); await expect(ask()).toBeDisabled();
  await expect(tower.getByText("Audit ถาวรยังไม่พร้อม", { exact: true })).toBeVisible();
  pass("disabled and missing-audit gates");
  await load("no-access");
  await expect(tower.getByText("บัญชีนี้ไม่มีสิทธิ์ ViewAudit จึงไม่โหลดประวัติการทำงาน")).toBeVisible();
  assert.equal((await inspect()).calls.filter(c => /\/api\/(dashboard|ai\/audit)/.test(c.path)).length, 0);
  pass("no capability means no dashboard/audit requests");
  await load("operator");
  await expect(tower.getByRole("button", { name: "เปิด My Job เพื่อตรวจงาน →" }).first()).toBeVisible();
  pass("operator receives safe My Job source route without Monitor permission");
  await load("empty");
  await expect(tower.getByText("FIXTURE · ไม่มีงาน active")).toBeVisible();
  pass("empty brief is explicit");
  await load("error"); await question().fill("งานวันนี้"); await ask().click();
  await expect(tower.getByText("อ่านข้อมูลงานไม่สำเร็จ", { exact: true })).toBeVisible();
  assert.ok(!(await tower.innerText()).includes("fixture-private-error"));
  pass("upstream error text is sanitized");
  await load("mock"); await question().fill("งานวันนี้"); await ask().click();
  await expect(tower.getByText("MOCK · ไม่ได้อ่านงานจริง", { exact: true })).toBeVisible();
  await expect(tower.getByRole("region", { name: "หลักฐานข้อมูลงาน" })).toHaveCount(0);
  pass("mock answer carries no job evidence");
  await load("ready");
  await tower.locator("section").first().scrollIntoViewIfNeeded();
  await page.screenshot({ path: "outputs/ai-qa/desktop.png", fullPage: true });
  await page.setViewportSize({ width: 768, height: 1024 });
  await expect.poll(async () => page.locator(".app-rail").evaluate(element => element.getBoundingClientRect().right)).toBeLessThanOrEqual(0);
  await page.screenshot({ path: "outputs/ai-qa/tablet.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: "outputs/ai-qa/mobile.png", fullPage: true });
  console.log("LAYOUT: " + JSON.stringify(await tower.evaluate(element => ({
    viewport: innerWidth, left: element.getBoundingClientRect().left, right: element.getBoundingClientRect().right,
    client: element.clientWidth, scroll: element.scrollWidth,
  }))));
  pass("desktop/tablet/mobile screenshots captured for visual inspection");
  await question().fill("งานวันนี้"); await ask().click();
  await expect(tower.getByRole("button", { name: "TEST-JOB-001 ↗", exact: true })).toBeVisible();
  await page.screenshot({ path: "outputs/ai-qa/mobile-answer.png", fullPage: true });
  assert.ok(await tower.evaluate(element => element.scrollWidth <= element.clientWidth), "no horizontal page overflow on mobile");
  await mode("carrier"); await page.reload();
  await expect(tower).toHaveCount(0);
  pass("mobile submit works; carrier never sees the internal Control Tower");
  assert.deepEqual(errors, [], "no browser runtime exceptions");
  pass("no JavaScript runtime exceptions");
} catch (error) {
  await page.screenshot({ path: "outputs/ai-qa/failure.png", fullPage: true }).catch(() => {});
  console.error("PAGE: " + (await page.locator("body").innerText().catch(() => "")).slice(0,5000));
  throw error;
} finally { await context.close(); await browser.close(); }
