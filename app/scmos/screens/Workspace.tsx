"use client";

import { useEffect, useState } from "react";
import { css, opTone, STATUS_LADDER, STATUS_TH } from "../theme";
import { isCancelled, STATUS_RE, wasMoved, type Job, type Ops } from "../ops";
import type { Account } from "../nav";
import { DataTable, type TableModel, type TableRow } from "../DataTable";
import { JobCards } from "../JobCards";
import { monthLabel, partsOf } from "../period";
import type { PanelPrefs } from "../settings";
import { cell, cols, dnum, dowOf, pad, paginate, tmin, type Cell, type CellOpts } from "../util";

export type WsState = {
  tab: string;
  cat: string;
  cust: string;
  trucker: string;
  date: string;
  kpi: string;
  assignee: string;
  /** Exact status, set from the process board — the old Shipment Monitor's status filter. */
  status: string;
  /** Truck / container type, carried over from the monitor's TRUCK TYPE filter. */
  type: string;
  /** Calendar year, e.g. "2026". "ALL" spans every year in the register. */
  year: string;
  /** Month of that year as MM, e.g. "07". */
  month: string;
  q: string;
  page: number;
  edit: { key: string; field: string } | null;
  editVal: string;
  /** Column sorting, driven by clicking a header. */
  sort: { key: string; dir: "asc" | "desc" } | null;
  /** Keys of the rows ticked for a bulk action. */
  picked: string[];
};

type Props = {
  ops: Ops;
  me: Account;
  ws: WsState;
  set: (patch: Partial<WsState>) => void;
  onDrawer: (key: string) => void;
  onDelay: (key: string) => void;
  onSaveCell: (job: Job, field: keyof Job) => void;
  /** Writes a known value straight onto a field — what the dropdowns use. */
  onSetField: (job: Job, field: keyof Job, value: string) => void;
  onStatusChange: (job: Job, value: string) => void;
  onSort: (key: string) => void;
  /**
   * Whether this person may write to a job, and whether they may hand one to
   * somebody else. Passed in rather than worked out here: this file had its own
   * copy of the edit rule, it said `role !== "Operation User"`, and it would
   * have handed an edit button to every Viewer and CS account the moment those
   * roles were used. Every rule this codebase has written twice has drifted.
   */
  canEdit: (job: Job) => boolean;
  canAssign: boolean;
  /** Rows per page, from the viewer's settings. */
  per: number;
  /**
   * The page the API chose, one entry per section, when the screen is being
   * fed from the server rather than from the whole register in the browser.
   *
   * Absent means the old path: filter and paginate 2,626 jobs here. Present
   * means the rows and the totals come from `/api/jobs/page`, which applied the
   * same tab, category, period and sort rules — moved there rather than copied,
   * and checked against these ones on every tab and category before this was
   * wired up.
   */
  serverPages?: Record<string, WorkspaceServerPage>;
  /** Summary panels and editing become authoritative once this is true. */
  fullRegisterLoaded: boolean;
  /**
   * Which page each section is on, and how to change it.
   *
   * Owned above rather than here whenever the server is answering, because the
   * fetch and the pager have to agree on the number: this screen kept its own
   * and the two drifted apart the moment somebody pressed next — the pager
   * moved to page two and the request that would have fetched it never ran, so
   * the grid drew a page that had not been asked for and came out empty.
   */
  sectionPages?: Record<string, number>;
  onSectionPage?: (layout: string, page: number) => void;
  /** Whether edits are reaching the database, shown next to the welcome line. */
  sync: { state: "idle" | "waking" | "saving" | "saved" | "error" | "off"; at: string; message: string };
  /** Which panels above the grid are expanded, and which one to fold. */
  panels: PanelPrefs;
  onPanel: (key: keyof PanelPrefs) => void;
  /** Bulk actions over the ticked rows; all three only touch jobs you may edit. */
  onBulkStatus: (keys: string[], status: string) => void;
  onBulkAssign: (keys: string[], owner: string) => void;
  onBulkDelete: (keys: string[]) => void;
  /** Reports what is currently on screen so Export writes exactly that. */
  onView: (view: { jobs: Job[]; layout: string }) => void;
};

export type WorkspaceServerPage = {
  jobs: Job[];
  total: number;
  pageCount: number;
  counts: Record<string, number>;
  dates: string[];
};

const CATEGORIES = ["ALL", "IMPORT", "EXPORT", "DELIVERY"];

const COL_DEFS: Record<string, [string][]> = {
  // The operators' own column order, from their plan sheets.
  IMPORT: [["Priority"], ["Own"], ["Category"], ["Date"], ["Customer"], ["Truck"], ["Job Code"], ["Product"], ["Destination"], ["Plan Loading Time"], ["Type"], ["CY Yard"], ["Total Weight"], ["No Container"], ["Licence"], ["Driver"], ["Driver Contact"], ["Status"], ["Arrival Date"], ["Arrival Time"], ["Reason / Delay"], ["Pickup Plan"], ["CS"], ["Incident Report"], ["Change"], ["Assigned To"]],
  EXPORT: [["Priority"], ["Own"], ["Category"], ["Customer"], ["Truck"], ["Booking"], ["ABS No."], ["Plant Loading"], ["Plan Loading Date"], ["Plan Loading Time"], ["Type"], ["CY Yard"], ["Return"], ["Closing Date"], ["Closing Time"], ["Closing Risk"], ["No Container"], ["No Seal"], ["Tare"], ["Licence"], ["Driver Name"], ["Driver Contact"], ["Arrival Date"], ["Arrival Time"], ["Status"], ["Remark"], ["Incident Report"], ["Change"], ["Assigned To"]],
  DELIVERY: [["Priority"], ["Own"], ["W/H"], ["Job No."], ["Pickup Date"], ["SID No."], ["Customer"], ["Province"], ["ZIP"], ["Pallet"], ["Weight KG"], ["4W"], ["6W"], ["10W"], ["Trailer"], ["Transport Cost"], ["Status"], ["Remark"], ["Change"], ["Assigned To"]],
  // Mixed lists (My Work, Team Work, Delay, Completed) carry every column from
  // both plans, so no field is missing whichever kind of job you are looking at.
  ALL: [["Priority"], ["Own"], ["Category"], ["Date"], ["Customer"], ["Truck"], ["Job Code"], ["ABS No."], ["Booking"], ["Product"], ["Destination"], ["Plant Loading"], ["Plan Loading Time"], ["Type"], ["CY Yard"], ["Return"], ["Closing Date"], ["Closing Time"], ["Closing Risk"], ["Total Weight"], ["No Container"], ["No Seal"], ["Tare"], ["Licence"], ["Driver Name"], ["Driver Contact"], ["Status"], ["Arrival Date"], ["Arrival Time"], ["Reason / Delay"], ["Remark"], ["Pickup Plan"], ["CS"], ["Incident Report"], ["Change"], ["Assigned To"]],
};

const KPI_DEFS: [string, string, string, string][] = [
  ["MY JOBS", "งานของฉัน", "Mine", "#2E7DD1"],
  ["TEAM JOBS", "งานทีม", "Team", "#0A2240"],
  ["IMPORT", "นำเข้า", "Imp", "#0A2240"],
  ["EXPORT", "ส่งออก", "Exp", "#6FA8DC"],
  ["DELIVERY", "งานกระจายสินค้า", "Del", "#0A6E8A"],
  ["WAITING TRUCK", "รอรถ", "Wait", "#475569"],
  ["TRUCK CONFIRMED", "ยืนยันรถ", "Conf", "#1D5FA8"],
  ["IN OPERATION", "กำลังปฏิบัติงาน", "Run", "#0A6E8A"],
  ["DELAYED", "ล่าช้า", "Delay", "#B42318"],
  ["COMPLETED", "เสร็จสิ้น", "Done", "#16794C"],
  ["ACTION REQUIRED", "ต้องดำเนินการ", "Act", "#B45309"],
  ["FORMAT ERROR", "รูปแบบข้อมูลผิด", "Fmt", "#B42318"],
];

/** Jobs carrying at least one blocking format issue — excluded from the KPIs. */
const hasFormatError = (j: Job) => j.issues.some((i) => i.severity === "error");

/**
 * Column header -> the job field it sorts on, and how to compare it. Clicking a
 * header used to raise a toast and change nothing; these are the columns the
 * grid actually shows, so every one of them now sorts.
 */
const SORT_BY: Record<string, { pick: (j: Job) => string | undefined; as: "text" | "date" | "time" | "number" | "prio" }> = {
  Priority: { pick: (j) => j.prio, as: "prio" },
  Own: { pick: (j) => j.op, as: "text" },
  Category: { pick: (j) => j.cat, as: "text" },
  Date: { pick: (j) => j.date, as: "date" },
  "Plan Date": { pick: (j) => j.date, as: "date" },
  "Pickup Date": { pick: (j) => j.date, as: "date" },
  Customer: { pick: (j) => j.customer, as: "text" },
  Truck: { pick: (j) => j.trucker, as: "text" },
  "Job Code": { pick: (j) => j.jobCode, as: "text" },
  "Job / ABS": { pick: (j) => j.jobCode || j.abs, as: "text" },
  "Job No.": { pick: (j) => j.jobNo, as: "text" },
  "ABS No.": { pick: (j) => j.abs, as: "text" },
  Booking: { pick: (j) => j.booking, as: "text" },
  "SID No.": { pick: (j) => j.sid, as: "text" },
  DG: { pick: (j) => j.product, as: "text" },
  "FCL/LCL": { pick: (j) => j.fclLcl, as: "text" },
  Destination: { pick: (j) => j.destination, as: "text" },
  "Plant Loading": { pick: (j) => j.plant, as: "text" },
  Province: { pick: (j) => j.province, as: "text" },
  ZIP: { pick: (j) => j.zip, as: "text" },
  "W/H": { pick: (j) => j.wh, as: "text" },
  Plan: { pick: (j) => j.planTime, as: "time" },
  "Plan Time": { pick: (j) => j.planTime, as: "time" },
  Type: { pick: (j) => j.type, as: "text" },
  "CY Yard": { pick: (j) => j.cyYard, as: "text" },
  Return: { pick: (j) => j.returnLoc, as: "text" },
  Weight: { pick: (j) => j.weight, as: "number" },
  "Weight KG": { pick: (j) => j.kgs, as: "number" },
  Pallet: { pick: (j) => j.pallet, as: "number" },
  "4W": { pick: (j) => j.v4, as: "number" },
  "6W": { pick: (j) => j.v6, as: "number" },
  "10W": { pick: (j) => j.v10, as: "number" },
  Trailer: { pick: (j) => j.vtr, as: "number" },
  "Transport Cost": { pick: (j) => j.cost, as: "number" },
  Tare: { pick: (j) => j.tare, as: "number" },
  Container: { pick: (j) => j.container, as: "text" },
  "Container No.": { pick: (j) => j.container, as: "text" },
  Seal: { pick: (j) => j.seal, as: "text" },
  Licence: { pick: (j) => j.licence, as: "text" },
  Driver: { pick: (j) => j.driver, as: "text" },
  "Driver Contact": { pick: (j) => j.contact, as: "text" },
  Status: { pick: (j) => j.status, as: "text" },
  "Closing Date": { pick: (j) => j.closingDate, as: "date" },
  "Closing Time": { pick: (j) => j.closingTime, as: "time" },
  "Arr. Date": { pick: (j) => j.arrDate, as: "date" },
  "Arr. Time": { pick: (j) => j.arrTime, as: "time" },
  Arrival: { pick: (j) => j.arrTime, as: "time" },
  "Reason / Delay": { pick: (j) => j.reason, as: "text" },
  Remark: { pick: (j) => j.remark, as: "text" },
  OT: { pick: (j) => j.ot, as: "text" },
  CS: { pick: (j) => j.cs, as: "text" },
  "Assigned To": { pick: (j) => j.op, as: "text" },
  // Headers as the operators write them on their own sheets.
  Product: { pick: (j) => j.product, as: "text" },
  "Plan Loading Date": { pick: (j) => j.date, as: "date" },
  "Plan Loading Time": { pick: (j) => j.planTime, as: "time" },
  "Total Weight": { pick: (j) => j.weight, as: "number" },
  "No Container": { pick: (j) => j.container, as: "text" },
  "No Seal": { pick: (j) => j.seal, as: "text" },
  "Driver Name": { pick: (j) => j.driver, as: "text" },
  "Arrival Date": { pick: (j) => j.arrDate, as: "date" },
  "Arrival Time": { pick: (j) => j.arrTime, as: "time" },
  "Pickup Plan": { pick: (j) => j.pickupPlan, as: "text" },
  "Incident Report": { pick: (j) => j.incident, as: "text" },
};

const PRIO_RANK: Record<string, number> = { HIGH: 0, MEDIUM: 1, LOW: 2 };

function sortJobs(list: Job[], sort: { key: string; dir: "asc" | "desc" } | null): Job[] {
  if (!sort) return list;
  const rule = SORT_BY[sort.key];
  if (!rule) return list;
  const flip = sort.dir === "desc" ? -1 : 1;

  return list.slice().sort((a, b) => {
    const va = (rule.pick(a) || "").trim();
    const vb = (rule.pick(b) || "").trim();
    // Blanks always sit at the bottom: an empty licence is not "before A".
    if (!va && !vb) return 0;
    if (!va) return 1;
    if (!vb) return -1;

    let diff = 0;
    if (rule.as === "date") diff = dnum(va) - dnum(vb);
    else if (rule.as === "time") diff = (tmin(va) ?? 0) - (tmin(vb) ?? 0);
    else if (rule.as === "number") diff = (Number(va.replace(/,/g, "")) || 0) - (Number(vb.replace(/,/g, "")) || 0);
    else if (rule.as === "prio") diff = (PRIO_RANK[va] ?? 3) - (PRIO_RANK[vb] ?? 3);
    else diff = va.localeCompare(vb, "th");
    return diff * flip;
  });
}

/** Tints a cell whose value breaks the data standard so it is findable by eye. */
function markIssue(c: Cell, j: Job, field: string) {
  const issue = j.issues.find((i) => i.field === field);
  if (!issue) return;
  const accent = issue.severity === "error" ? "#B42318" : "#D89614";
  c.td += "background:" + (issue.severity === "error" ? "#FDF0EF" : "#FFF8E8") +
    ";box-shadow:inset 3px 0 " + accent + ";";
}

/**
 * The status buckets, from the one place that defines them.
 *
 * This file used to keep its own copy of the patterns. When the register moved
 * to controlled codes that copy went on reading the old free text, so the KPI
 * strip showed nought jobs waiting for a truck and counted 228 running jobs as
 * complete — the panel and the grid beside it disagreed about the same rows.
 *
 * `open` and `active` are the two groupings the process board needs and are
 * built from the shared buckets rather than from patterns of their own.
 */
const RE = {
  ...STATUS_RE,
  open: { test: (s: string) => STATUS_RE.waiting.test(s) },
  active: { test: (s: string) => STATUS_RE.confirmed.test(s) || STATUS_RE.running.test(s) },
};

function kpiCount(scope: Job[], code: string, mine: (j: Job) => boolean) {
  switch (code) {
    case "Mine": return scope.filter(mine).length;
    case "Team": return scope.length;
    case "Imp": return scope.filter((j) => j.cat === "IMPORT").length;
    case "Exp": return scope.filter((j) => j.cat === "EXPORT").length;
    case "Del": return scope.filter((j) => j.cat === "DELIVERY").length;
    case "Wait": return scope.filter((j) => RE.waiting.test(j.status)).length;
    case "Conf": return scope.filter((j) => RE.confirmed.test(j.status)).length;
    case "Run": return scope.filter((j) => RE.running.test(j.status)).length;
    case "Delay": return scope.filter((j) => RE.delayed.test(j.status)).length;
    case "Done": return scope.filter((j) => RE.done.test(j.status)).length;
    case "Act": return scope.filter((j) => j.action).length;
    case "Fmt": return scope.filter(hasFormatError).length;
    default: return 0;
  }
}

/** Today and tomorrow as the plan writes dates, so they compare as text. */
function planDate(offsetDays: number): string {
  const at = new Date();
  at.setDate(at.getDate() + offsetDays);
  return `${pad(at.getDate())}/${pad(at.getMonth() + 1)}/${at.getFullYear()}`;
}

/**
 * The document values the plan itself carries.
 *
 * There is no document register yet — DocumentVerification is still on the
 * schema rather than in it — so "document missing" means the identifiers the
 * workbooks do carry: the container number, and the seal on an export. A truck
 * load that never has a container is not missing one.
 */
function documentMissing(job: Job): boolean {
  if (RE.done.test(job.status)) return false;
  const blank = (value: string | undefined) => !(value ?? "").trim();
  const needsContainer = !/6WH|4WH|10W|COMBINE/i.test(job.type || "");
  if (needsContainer && blank(job.container)) return true;
  return job.cat === "EXPORT" && blank(job.seal);
}

/**
 * What each tab means, in one place.
 *
 * The counts on the tab strip and the rows in the grid both read this, so a tab
 * cannot say 419 and then show a different set.
 *
 * Only MY JOBS narrows to the signed-in operator. The rest are team-wide: the
 * process asks that everyone can see what the team is carrying, and ownership
 * governs editing rather than looking.
 */
export const WORKSPACE_TABS: Record<string, (job: Job, opId: string) => boolean> = {
  "MY JOBS": (job, opId) => !!opId && job.opId === opId,
  // A cancelled job is not work waiting to be done, and it used to sit in
  // PENDING for the rest of its life looking like some. It keeps its place in
  // MY JOBS and CALENDAR, where the question is what belongs to whom and what
  // was planned, and leaves the three lists that mean "still to do".
  PENDING: (job) => !RE.done.test(job.status) && !isCancelled(job),
  TODAY: (job) => job.date === planDate(0) && !isCancelled(job),
  TOMORROW: (job) => job.date === planDate(1) && !isCancelled(job),
  // A delay is the status saying so, or a reason somebody wrote down. Not
  // "action required" — that bucket is mostly missing values, and calling those
  // delays would put 1,758 jobs behind a word that should mean something.
  //
  // A postponement is not a delay either: a delay is a job that missed its
  // plan, a postponement is a plan that changed before it was missed. They are
  // different conversations with different people, so they get different tabs.
  DELAY: (job) => (RE.delayed.test(job.status) || !!(job.reason ?? "").trim()) && !isCancelled(job),
  "DOCUMENT MISSING": (job) => documentMissing(job) && !isCancelled(job),
  COMPLETED: (job) => RE.done.test(job.status),
  "CANCEL / MOVED": (job) => isCancelled(job) || wasMoved(job),
};

/** Tab labels carry live counts; the header renders them, so this is exported. */
export function workspaceTabCounts(ops: Ops | null, opId: string, cat: string): Record<string, number> {
  if (!ops) return {};
  const base = cat === "ALL" ? ops.jobs : ops.jobs.filter((j) => j.cat === cat);
  const counts: Record<string, number> = {};
  for (const [tab, matches] of Object.entries(WORKSPACE_TABS)) {
    counts[tab] = base.filter((job) => matches(job, opId)).length;
  }
  counts.CALENDAR = new Set(base.map((j) => j.date).filter(Boolean)).size;
  return counts;
}

export function Workspace(p: Props) {
  const { ops, me, ws } = p;
  const all = ops.jobs;
  const M = ops.masters;
  const server = p.serverPages;
  const complete = p.fullRegisterLoaded;
  const serverTotal = server
    ? Object.values(server).reduce((sum, answer) => sum + answer.total, 0)
    : 0;

  // On the owner id, never the display name — the same rule the rest of the app
  // uses. This copy was missed when ownership moved off names, which would have
  // shown an operator an empty workspace the day real sign-in arrived.
  const mineJ = (j: Job) => !!me.opId && j.opId === me.opId;
  const canEditJob = p.canEdit;
  const canAssign = p.canAssign;

  const inCat = (c: string) => (c === "ALL" ? all : all.filter((j) => j.cat === c));
  const catBase = inCat(ws.cat);
  // Year and month narrow everything downstream — the day strip, the process
  // board, the KPI tiles and the grid all describe the same slice.
  const base = catBase.filter((j) => {
    const parts = partsOf(j.date);
    if (ws.year !== "ALL" && (!parts || parts.y !== ws.year)) return false;
    if (ws.month !== "ALL" && (!parts || parts.m !== ws.month)) return false;
    return true;
  });

  // Every option offered exists in the data, and each level narrows the next.
  const years = [...new Set(catBase.map((j) => partsOf(j.date)?.y).filter(Boolean) as string[])].sort();
  const months = [...new Set(catBase
    .filter((j) => ws.year === "ALL" || partsOf(j.date)?.y === ws.year)
    .map((j) => partsOf(j.date)?.m)
    .filter(Boolean) as string[])].sort();
  const undated = catBase.filter((j) => !partsOf(j.date)).length;

  // ---- date strip -------------------------------------------------------
  const dateCount: Record<string, number> = {};
  base.forEach((j) => { if (j.date) dateCount[j.date] = (dateCount[j.date] || 0) + 1; });
  const dates = Object.keys(dateCount).sort((a, b) => dnum(a) - dnum(b));
  const busiest = dates.length ? dates.reduce((x, y) => (dateCount[y] > dateCount[x] ? y : x), dates[0]) : "";
  const anchor = ws.date !== "ALL" && dateCount[ws.date] !== undefined ? ws.date : busiest;
  const anchorIndex = Math.max(0, dates.indexOf(anchor) - 3);
  const step = (n: number) => {
    const i = dates.indexOf(anchor);
    const target = dates[Math.min(dates.length - 1, Math.max(0, i + n))];
    if (target) p.set({ date: target, page: 1 });
  };

  const scope = ws.date === "ALL" ? base : base.filter((j) => j.date === anchor);

  // ---- workload ---------------------------------------------------------
  const workload = M.operators.map((name) => {
    const set = base.filter((j) => j.op === name);
    return {
      name,
      init: name.slice(0, 2).toUpperCase(),
      total: set.length,
      open: set.filter((j) => RE.open.test(j.status)).length,
      running: set.filter((j) => RE.active.test(j.status)).length,
      delay: set.filter((j) => RE.delayed.test(j.status)).length,
      done: set.filter((j) => RE.done.test(j.status)).length,
    };
  });

  // ---- process board ----------------------------------------------------
  // Replaces the Import / Export "Process Progress" screens. Same idea, same
  // stage names, but every figure is the real status of a real job — the old
  // screens derived "cleared / pending" from the row index, not from the data.
  const ladderCat =
    ws.cat !== "ALL" ? ws.cat
      : ["IMPORT", "EXPORT", "DELIVERY"].indexOf(ws.tab) >= 0 ? ws.tab
        : "";
  // The ladder can come from the tab while the category buttons still say ALL,
  // so the counts have to be narrowed the same way or the board would show one
  // category's stages with every category's jobs.
  const boardScope = ladderCat ? scope.filter((j) => j.cat === ladderCat) : scope;
  const statusCount: Record<string, number> = {};
  boardScope.forEach((j) => { if (j.status) statusCount[j.status] = (statusCount[j.status] || 0) + 1; });
  const ladder = ladderCat
    ? STATUS_LADDER[ladderCat] ?? []
    : [...new Set(Object.values(STATUS_LADDER).flat())];
  // A status an operator typed by hand is not on the ladder, and a job must not
  // be able to hide from the board — so those are listed after it, marked as
  // off-ladder rather than numbered as if they were part of the process.
  const stages = ladder
    .filter((s) => (ladderCat ? true : (statusCount[s] || 0) > 0))
    .map((s, i) => ({ status: s, th: STATUS_TH[s] ?? "", n: statusCount[s] || 0, step: pad(i + 1), off: false }))
    .concat(
      Object.keys(statusCount)
        .filter((s) => ladder.indexOf(s) < 0)
        .sort((a, b) => statusCount[b] - statusCount[a])
        .map((s) => ({ status: s, th: "นอกลำดับสถานะ", n: statusCount[s], step: "!", off: true })),
    );

  const rankedOptionsOf = (key: "customer" | "trucker" | "type") => {
    const m: Record<string, number> = {};
    base.forEach((j) => { const v = j[key]; if (v) m[v] = (m[v] || 0) + 1; });
    return Object.keys(m).sort((a, b) => m[b] - m[a] || a.localeCompare(b));
  };

  // ---- row filtering ----------------------------------------------------
  let list = base.slice();
  const tabRule = WORKSPACE_TABS[ws.tab];
  if (tabRule) list = list.filter((job) => tabRule(job, me.opId));

  if (ws.tab !== "MY JOBS") {
    if (ws.assignee === "My Work") list = list.filter(mineJ);
    else if (M.operators.indexOf(ws.assignee) >= 0) list = list.filter((j) => j.op === ws.assignee);
  }
  if (ws.cust !== "ALL") list = list.filter((j) => j.customer === ws.cust);
  if (ws.trucker !== "ALL") list = list.filter((j) => j.trucker === ws.trucker);
  if (ws.type !== "ALL") list = list.filter((j) => j.type === ws.type);
  if (ws.status !== "ALL") list = list.filter((j) => j.status === ws.status);
  if (ws.date !== "ALL" && anchor) list = list.filter((j) => j.date === anchor);

  const K = ws.kpi;
  if (K === "Mine") list = list.filter(mineJ);
  if (K === "Imp") list = list.filter((j) => j.cat === "IMPORT");
  if (K === "Exp") list = list.filter((j) => j.cat === "EXPORT");
  if (K === "Del") list = list.filter((j) => j.cat === "DELIVERY");
  if (K === "Wait") list = list.filter((j) => RE.waiting.test(j.status));
  if (K === "Conf") list = list.filter((j) => RE.confirmed.test(j.status));
  if (K === "Run") list = list.filter((j) => RE.running.test(j.status));
  if (K === "Delay") list = list.filter((j) => RE.delayed.test(j.status));
  if (K === "Done") list = list.filter((j) => RE.done.test(j.status));
  if (K === "Act") list = list.filter((j) => j.action);
  if (K === "Fmt") list = list.filter(hasFormatError);

  const q = (ws.q || "").toLowerCase().trim();
  if (q) {
    list = list.filter((j) =>
      [j.jobCode, j.abs, j.booking, j.customer, j.container, j.seal, j.licence, j.driver, j.trucker, j.destination, j.op, j.sid]
        .join(" ").toLowerCase().indexOf(q) >= 0,
    );
  }

  // ---- cell builders ----------------------------------------------------

  /**
   * The editable columns, in the order they are actually rendered.
   *
   * Captured as the rows are built rather than declared separately: keyboard
   * navigation has to walk the same columns the eye does, and a hand-written
   * second list would be one more rule to keep in step with the row builder.
   * The jobs on screen per layout are recorded alongside, so up and down know
   * which row comes next.
   */
  const editOrder: Record<string, string[]> = {};
  const rowsByLayout: Record<string, Job[]> = {};
  let building = "ALL";

  /** Where the cursor is now, and what is either side of it. */
  const locate = () => {
    if (!ws.edit) return null;
    for (const [layout, fields] of Object.entries(editOrder)) {
      const column = fields.indexOf(ws.edit.field);
      const row = (rowsByLayout[layout] ?? []).findIndex((j) => j.key === ws.edit!.key);
      if (column >= 0 && row >= 0) return { layout, fields, column, row, jobs: rowsByLayout[layout] };
    }
    return null;
  };

  /**
   * Moves the edit cursor, saving whatever was typed on the way.
   *
   * `onSaveCell` closes the editor; setting the next cell in the same handler
   * re-opens it one along, and React batches both so the grid never flickers
   * through a closed state. Stops at the edges rather than wrapping — wrapping
   * from the last column of one job to the first of the next is how somebody
   * types a container number into the wrong row.
   */
  const moveEdit = (job: Job, field: keyof Job, dx: number, dy: number) => {
    const at = locate();
    p.onSaveCell(job, field);
    if (!at) return;

    const column = Math.min(Math.max(at.column + dx, 0), at.fields.length - 1);
    const row = Math.min(Math.max(at.row + dy, 0), at.jobs.length - 1);
    const next = at.jobs[row];
    if (!next || !canEditJob(next)) return;
    if (next.key === job.key && at.fields[column] === String(field)) return;

    p.set({
      edit: { key: next.key, field: at.fields[column] },
      editVal: (next[at.fields[column] as keyof Job] as string) || "",
    });
  };

  /** An editable text cell: click to edit in place, Enter/blur to save. */
  const ed = (j: Job, field: keyof Job, opts: CellOpts = {}): Cell => {
    // Recorded for every job, editable or not: the column order belongs to the
    // layout, not to whoever happens to own the first row on the page.
    const order = editOrder[building] ??= [];
    if (!order.includes(String(field))) order.push(String(field));

    const editing = ws.edit?.key === j.key && ws.edit?.field === field;
    if (editing) {
      return {
        kind: "input",
        v: "",
        sp: "",
        value: ws.editVal,
        td: "padding:4px 8px;white-space:nowrap;border-bottom:1px solid #EDF1F5;background:#FFF7DE;",
        inpStyle: "height:26px;width:100%;min-width:118px;border:1px solid #2E7DD1;border-radius:3px;font-size:12px;padding:0 6px;outline:none;background:#fff",
        onChange: (e) => p.set({ editVal: e.target.value }),
        onBlur: () => p.onSaveCell(j, field),
        onKey: (e) => {
          const input = e.currentTarget;
          const caret = input.selectionStart ?? 0;
          const selecting = input.selectionEnd !== input.selectionStart;

          // Left and right only leave the cell from its edges. Anywhere else
          // they move the caret, which is what somebody correcting one digit of
          // a container number expects them to do.
          const atStart = caret === 0 && !selecting;
          const atEnd = caret === input.value.length && !selecting;

          if (e.key === "Escape") { p.set({ edit: null }); return; }
          if (e.key === "Enter") { e.preventDefault(); moveEdit(j, field, 0, e.shiftKey ? -1 : 1); return; }
          if (e.key === "Tab") { e.preventDefault(); moveEdit(j, field, e.shiftKey ? -1 : 1, 0); return; }
          if (e.key === "ArrowLeft" && atStart) { e.preventDefault(); moveEdit(j, field, -1, 0); return; }
          if (e.key === "ArrowRight" && atEnd) { e.preventDefault(); moveEdit(j, field, 1, 0); return; }
          if (e.key === "ArrowUp") { e.preventDefault(); moveEdit(j, field, 0, -1); return; }
          if (e.key === "ArrowDown") { e.preventDefault(); moveEdit(j, field, 0, 1); }
        },
      };
    }
    const c = cell((j[field] as string) || "—", opts);
    markIssue(c, j, String(field));
    if (canEditJob(j)) {
      c.td += "cursor:cell;";
      c.go = (e) => {
        e.stopPropagation();
        p.set({ edit: { key: j.key, field: String(field) }, editVal: (j[field] as string) || "" });
      };
    }
    return c;
  };

  /** A cell whose value is one of a fixed set — category, priority. */
  const edChoice = (j: Job, field: keyof Job, options: string[], opts: CellOpts = {}): Cell => {
    const current = (j[field] as string) || "";
    if (!canEditJob(j)) return cell(current || "—", opts);
    return {
      kind: "select",
      v: current,
      sp: "",
      value: current,
      options: options.indexOf(current) >= 0 ? options : [current].concat(options),
      td: "padding:5px 9px;white-space:nowrap;border-bottom:1px solid #EDF1F5;",
      selStyle: "height:27px;border:1px solid #BBD5EE;border-radius:4px;background:#F4F8FC;font-size:11.5px;color:#0A2240;font-weight:600;padding:0 5px;cursor:pointer",
      onChange: (e) => {
        e.stopPropagation();
        p.onSetField(j, field, e.target.value);
      },
      go: (e) => e.stopPropagation(),
    };
  };

  /** Status is a dropdown for jobs you own; "Delayed" routes into the delay modal. */
  const stCell = (j: Job): Cell => {
    if (!canEditJob(j)) {
      const plain = cell(j.status, { tone: opTone(j.status) });
      markIssue(plain, j, "status");
      return plain;
    }
    const opts = STATUS_LADDER[j.cat] || STATUS_LADDER.IMPORT;
    const options = opts.indexOf(j.status) >= 0 ? opts : [j.status].concat(opts);
    return {
      kind: "select",
      v: j.status,
      sp: "",
      value: j.status,
      options,
      td: "padding:5px 9px;white-space:nowrap;border-bottom:1px solid #EDF1F5;",
      selStyle: "height:27px;max-width:158px;border:1px solid #BBD5EE;border-radius:4px;background:#F4F8FC;font-size:11.5px;color:#0A2240;font-weight:600;padding:0 5px;cursor:pointer",
      onChange: (e) => { e.stopPropagation(); p.onStatusChange(j, e.target.value); },
      go: (e) => e.stopPropagation(),
    };
  };

  /**
   * Suggestion lists for the master-ish columns, built from the jobs already in
   * the register — most used first, so the names the team actually works with
   * are at the top. Typing a value that is not on the list is still allowed: a
   * new customer has to be able to arrive without an admin step.
   */
  const suggestions = (field: keyof Job, limit = 400) => {
    const counts: Record<string, number> = {};
    all.forEach((j) => {
      const value = String(j[field] ?? "").trim();
      if (value) counts[value] = (counts[value] || 0) + 1;
    });
    return Object.keys(counts).sort((a, b) => counts[b] - counts[a] || a.localeCompare(b)).slice(0, limit);
  };

  const PICK_FIELDS: (keyof Job)[] = [
    "customer", "trucker", "product", "destination", "plant", "returnLoc", "cyYard", "type",
    // Delivery's own master-ish columns, now that its grid is editable.
    "wh", "province",
  ];
  const pickLists = PICK_FIELDS.map((field) => ({ id: "ws-list-" + field, options: suggestions(field) }));

  /** An editable cell that offers what the register already contains. */
  const edPick = (j: Job, field: keyof Job, opts: CellOpts = {}): Cell => {
    const base = ed(j, field, opts);
    if (base.kind !== "input") return base;
    return { ...base, kind: "combo", listId: "ws-list-" + String(field) };
  };

  /** Export gate-in risk: closing time minus arrival time. */
  const riskCell = (j: Job): Cell => {
    const cm = tmin(j.closingTime);
    const am = tmin(j.arrTime || j.planTime);
    if (cm === null || am === null) return cell("—", { mute: true });
    const d = cm - am;
    return cell(d < 0 ? "OVERDUE" : d < 360 ? "RISK <6h" : "OK", { tone: d < 0 ? "red" : d < 360 ? "amber" : "green" });
  };

  let colKey = "ALL";
  if (ws.cat === "IMPORT") colKey = "IMPORT";
  if (ws.cat === "EXPORT") colKey = "EXPORT";
  if (ws.cat === "DELIVERY") colKey = "DELIVERY";

  list = sortJobs(list, ws.sort);

  /**
   * Import and export are different jobs with different paperwork, so a list
   * that holds both is shown as two grids — each with its own columns, its own
   * paging and its own header — rather than one table of everything where half
   * the columns are always blank.
   */
  // Every tab is category-mixed now, so the grid splits import from export on
  // all of them unless a category is chosen above it.
  const splitMixed = ws.cat === "ALL";
  // With the server answering, the sections are the ones it answered for — it
  // was asked per category, and a category it returned nothing for is a section
  // with no rows, exactly as an empty filter result is here.
  const sections = server
    ? Object.keys(server)
        .filter((layout) => server[layout].total > 0)
        .map((layout) => ({ layout, jobs: server[layout].jobs }))
    : (splitMixed
      ? (["IMPORT", "EXPORT", "DELIVERY"] as const)
        .map((c) => ({ layout: c as string, jobs: list.filter((j) => j.cat === c) }))
        .filter((s) => s.jobs.length)
      : [{ layout: colKey, jobs: list }]);
  if (!sections.length) sections.push({ layout: colKey, jobs: server ? [] : list });

  // ---- selection --------------------------------------------------------
  const picked = new Set(ws.picked);
  const pickedJobs = list.filter((j) => picked.has(j.key));

  const togglePick = (key: string) => {
    const next = new Set(ws.picked);
    if (next.has(key)) next.delete(key); else next.add(key);
    p.set({ picked: [...next] });
  };
  /** Ticks or clears every editable row on one grid's current page. */
  const togglePageOf = (rows: Job[], allPicked: boolean) => {
    const next = new Set(ws.picked);
    rows.forEach((j) => { if (allPicked) next.delete(j.key); else next.add(j.key); });
    p.set({ picked: [...next] });
  };

  const sortBy = (label: string) => {
    if (!SORT_BY[label]) return;
    const same = ws.sort?.key === label;
    // Third click on the same header clears the sort and hands back plan order.
    if (same && ws.sort?.dir === "desc") p.set({ sort: null, page: 1 });
    else p.set({ sort: { key: label, dir: same ? "desc" : "asc" }, page: 1 });
  };

  // ---- what is narrowing the grid right now ------------------------------
  const activeFilters: [string, string, () => void][] = [];
  // The tab is a filter and has to say so. TODAY showing "0 จาก 2,102" beside
  // "nothing is filtered" reads as an empty register rather than an empty day.
  // PENDING is the widest view, so it is the way out of every other tab.
  if (ws.tab !== "PENDING" && WORKSPACE_TABS[ws.tab]) {
    activeFilters.push(["มุมมอง", ws.tab, () => p.set({ tab: "PENDING", page: 1 })]);
  }
  if (ws.cat !== "ALL") activeFilters.push(["ประเภท", ws.cat, () => p.set({ cat: "ALL", page: 1 })]);
  if (ws.year !== "ALL") activeFilters.push(["ปี", ws.year, () => p.set({ year: "ALL", month: "ALL", date: "ALL", page: 1 })]);
  if (ws.month !== "ALL") activeFilters.push(["เดือน", monthLabel(ws.month), () => p.set({ month: "ALL", date: "ALL", page: 1 })]);
  if (ws.date !== "ALL") activeFilters.push(["วันที่", anchor, () => p.set({ date: "ALL", page: 1 })]);
  if (ws.tab !== "MY JOBS" && ws.assignee !== "All Team") activeFilters.push(["ผู้รับผิดชอบ", ws.assignee, () => p.set({ assignee: "All Team", page: 1 })]);
  if (ws.cust !== "ALL") activeFilters.push(["ลูกค้า", ws.cust, () => p.set({ cust: "ALL", page: 1 })]);
  if (ws.trucker !== "ALL") activeFilters.push(["ผู้ขนส่ง", ws.trucker, () => p.set({ trucker: "ALL", page: 1 })]);
  if (ws.type !== "ALL") activeFilters.push(["ประเภทรถ/ตู้", ws.type, () => p.set({ type: "ALL", page: 1 })]);
  if (ws.status !== "ALL") activeFilters.push(["สถานะ", ws.status, () => p.set({ status: "ALL", page: 1 })]);
  if (ws.kpi !== "All") activeFilters.push(["KPI", ws.kpi, () => p.set({ kpi: "All", page: 1 })]);
  if (ws.q) activeFilters.push(["ค้นหา", ws.q, () => p.set({ q: "", page: 1 })]);
  if (ws.sort) activeFilters.push(["เรียง", ws.sort.key + (ws.sort.dir === "asc" ? " ↑" : " ↓"), () => p.set({ sort: null })]);

  const clearAll = () => p.set({
    cat: "ALL", year: "ALL", month: "ALL", date: "ALL", assignee: "All Team", cust: "ALL",
    trucker: "ALL", type: "ALL", status: "ALL", kpi: "All", q: "", sort: null, page: 1,
  });

  const panel = (key: keyof PanelPrefs) => () => p.onPanel(key);
  const foldStyle = (open: boolean) =>
    "height:26px;padding:0 10px;border:1px solid " + (open ? "#BBD5EE" : "#D8E0E8") +
    ";background:" + (open ? "#F4F8FC" : "#fff") + ";color:#475569;border-radius:4px;font-size:11px;cursor:pointer";

  // Export writes the whole filtered set, not just the page being viewed. A
  // split view exports every column, since it holds both kinds of job.
  const { onView } = p;
  useEffect(() => { onView({ jobs: list, layout: splitMixed ? "ALL" : colKey }); });

  // Each grid pages on its own. The page numbers are stored against the filters
  // they belong to, so changing what is being filtered starts every grid back at
  // page one without an effect that re-renders after the fact.
  const filterSignature = [
    ws.tab, ws.cat, ws.cust, ws.trucker, ws.type, ws.status, ws.kpi, ws.assignee,
    ws.year, ws.month, ws.date, ws.q, ws.sort?.key, ws.sort?.dir, p.per,
  ].join("|");
  const [paging, setPaging] = useState<{ sig: string; pages: Record<string, number> }>({ sig: filterSignature, pages: {} });
  const pages = paging.sig === filterSignature ? paging.pages : {};
  const setPage = (layout: string, page: number) =>
    setPaging({ sig: filterSignature, pages: { ...pages, [layout]: page } });

  /**
   * What changed about the plan, in one column.
   *
   * Cancelled and postponed share a column because they are the same question —
   * "is this still happening as booked?" — and because a job is one or the
   * other far more often than both. The colour does the work at a glance; the
   * words are there for the person who then has to ring somebody about it.
   */
  const changeCell = (j: Job): Cell => {
    if (isCancelled(j)) {
      return cell("ยกเลิก" + (j.cancelReason ? " · " + j.cancelReason : ""),
        { color: "#B42318", w: 190 });
    }
    if (wasMoved(j)) {
      const who = j.moveBy ? " (" + j.moveBy + ")" : "";
      return cell("เลื่อนจาก " + j.origDate + who + (j.moveReason ? " · " + j.moveReason : ""),
        { color: "#B45309", w: 190 });
    }
    return cell("", { mute: true });
  };

  const rowCells = (j: Job, layout: string): Cell[] => {
    // Which layout the following cells belong to, so `ed` records the column
    // order against the right one.
    building = layout;

    const mine = mineJ(j);
    // Priority and the ownership flag are worked out from the job rather than
    // typed on it — `flagJob` sets one and `store.ts` drops both before saving.
    // Offering them as fields would let somebody change a value that reverts the
    // moment the page reloads, which is worse than not offering it at all.
    const head = [
      cell(j.prio, { tone: j.prio === "HIGH" ? "red" : j.prio === "MEDIUM" ? "amber" : "gray" }),
      cell(mine ? "MY JOB" : "VIEW ONLY", { tone: mine ? "blue" : "gray" }),
    ];

    // Changing the category moves a job to a different grid with different
    // paperwork, so it is a choice rather than free text — but it is a field on
    // the job and an operator has to be able to correct it.
    const catCell = edChoice(j, "cat", ["IMPORT", "EXPORT", "DELIVERY"],
      { tone: j.cat === "IMPORT" ? "dark" : j.cat === "EXPORT" ? "blue" : "teal" });

    if (layout === "IMPORT") {
      return head.concat([
        catCell, ed(j, "date", { mono: true }), edPick(j, "customer", { bold: true, w: 150 }), edPick(j, "trucker"),
        ed(j, "jobCode", { mono: true }), edPick(j, "product", { tone: /^\s*DG/i.test(j.product) ? "amber" : "gray" }),
        edPick(j, "destination", { w: 150 }), ed(j, "planTime", { mono: true }), edPick(j, "type", { mono: true }),
        edPick(j, "cyYard"), ed(j, "weight", { mono: true, align: "right" }),
        ed(j, "container", { mono: true }), ed(j, "licence", { mono: true }), ed(j, "driver", { w: 150 }),
        ed(j, "contact", { mono: true }), stCell(j), ed(j, "arrDate", { mono: true }), ed(j, "arrTime", { mono: true }),
        ed(j, "reason", { w: 180, color: j.reason ? "#B45309" : null }),
        ed(j, "pickupPlan", { w: 180, mute: true }), ed(j, "cs", { mono: true }),
        ed(j, "incident", { w: 170, color: j.incident ? "#B42318" : null }),
        changeCell(j),
        cell(j.op, { bold: mine, mute: !mine }),
      ]);
    }
    if (layout === "EXPORT") {
      return head.concat([
        catCell, edPick(j, "customer", { bold: true, w: 150 }), edPick(j, "trucker"), ed(j, "booking", { mono: true, w: 170 }),
        ed(j, "abs", { mono: true }), edPick(j, "plant", { w: 150 }),
        ed(j, "date", { mono: true }), ed(j, "planTime", { mono: true }), edPick(j, "type", { mono: true }),
        edPick(j, "cyYard"), edPick(j, "returnLoc", { w: 150 }), ed(j, "closingDate", { mono: true }),
        ed(j, "closingTime", { mono: true, bold: true }), riskCell(j),
        ed(j, "container", { mono: true }), ed(j, "seal", { mono: true }), ed(j, "tare", { mono: true, align: "right" }),
        ed(j, "licence", { mono: true }), ed(j, "driver", { w: 150 }), ed(j, "contact", { mono: true }),
        ed(j, "arrDate", { mono: true }), ed(j, "arrTime", { mono: true }), stCell(j),
        ed(j, "remark", { w: 180, mute: true }),
        ed(j, "incident", { w: 170, color: j.incident ? "#B42318" : null }),
        changeCell(j),
        cell(j.op, { bold: mine, mute: !mine }),
      ]);
    }
    if (layout === "DELIVERY") {
      // Delivery was read-only in all but two columns, which made the grid a
      // report rather than a place to work. Every stored field is editable now;
      // cost is the one that is not, because it is priced from the rate card
      // rather than typed, and a hand-keyed cost that disagrees with the card is
      // the kind of number nobody can later explain.
      return head.concat([
        edPick(j, "wh", { bold: true }), ed(j, "jobNo", { mono: true }), ed(j, "date", { mono: true }),
        ed(j, "sid", { mono: true, mute: true }), edPick(j, "customer", { w: 200 }), edPick(j, "province"),
        ed(j, "zip", { mono: true }), ed(j, "pallet", { mono: true, align: "right" }),
        ed(j, "kgs", { mono: true, align: "right" }),
        ed(j, "v4", { mono: true, align: "right" }), ed(j, "v6", { mono: true, align: "right" }),
        ed(j, "v10", { mono: true, align: "right" }), ed(j, "vtr", { mono: true, align: "right" }),
        cell(j.cost ? "฿" + Number(j.cost).toLocaleString("en-US") : "—", { mono: true, align: "right", bold: true }),
        stCell(j), ed(j, "remark", { w: 170, mute: true }), changeCell(j),
      cell(j.op, { bold: mine, mute: !mine }),
      ]);
    }
    return head.concat([
      catCell, ed(j, "date", { mono: true }), edPick(j, "customer", { bold: true, w: 150 }), edPick(j, "trucker"),
      ed(j, "jobCode", { mono: true }), ed(j, "abs", { mono: true }), ed(j, "booking", { mono: true, w: 160 }),
      edPick(j, "product", { tone: /^\s*DG/i.test(j.product) ? "amber" : "gray" }),
      edPick(j, "destination", { w: 150 }), edPick(j, "plant", { w: 150 }),
      ed(j, "planTime", { mono: true }), edPick(j, "type", { mono: true }), edPick(j, "cyYard"),
      edPick(j, "returnLoc", { w: 140 }), ed(j, "closingDate", { mono: true }), ed(j, "closingTime", { mono: true }),
      riskCell(j), ed(j, "weight", { mono: true, align: "right" }),
      ed(j, "container", { mono: true }), ed(j, "seal", { mono: true }), ed(j, "tare", { mono: true, align: "right" }),
      ed(j, "licence", { mono: true }), ed(j, "driver", { w: 150 }), ed(j, "contact", { mono: true }),
      stCell(j), ed(j, "arrDate", { mono: true }), ed(j, "arrTime", { mono: true }),
      ed(j, "reason", { w: 170, color: j.reason ? "#B45309" : null }), ed(j, "remark", { w: 170, mute: true }),
      ed(j, "pickupPlan", { w: 170, mute: true }), ed(j, "cs", { mono: true }),
      ed(j, "incident", { w: 160, color: j.incident ? "#B42318" : null }),
      changeCell(j),
      cell(j.op, { bold: mine, mute: !mine }),
    ]);
  };

  /** Leading tick-box, so a row can be picked without opening it. */
  const checkCell = (j: Job): Cell => ({
    kind: "check",
    v: "",
    sp: "",
    td: "padding:8px 6px 8px 12px;border-bottom:1px solid #EDF1F5;vertical-align:middle;width:34px;",
    checked: picked.has(j.key),
    disabled: !canEditJob(j),
    title: canEditJob(j) ? "เลือกงานนี้" : "งานของ " + j.op + " — เลือกไม่ได้",
    onCheck: () => togglePick(j.key),
  });

  const listTitle =
    ws.tab === "MY JOBS" ? "My Jobs — " + me.name
      : ws.tab === "DELAY" ? "Delayed Jobs"
        : ws.tab === "COMPLETED" ? "Completed Jobs"
          : "Team Work — one operation database";

  const SECTION_TITLE: Record<string, string> = {
    IMPORT: "งานนำเข้า · Import",
    EXPORT: "งานส่งออก · Export",
    DELIVERY: "งานกระจายสินค้า · Delivery",
    ALL: listTitle,
  };

  /** One grid per section: its own columns, its own page, its own tick-all. */
  const grids = sections.map((section) => {
    // The server already chose this page; paginating it again here would slice
    // twenty-five rows out of twenty-five and report a page count of one.
    const answered = server?.[section.layout];
    const pg = answered
      ? {
          slice: answered.jobs,
          total: answered.total,
          pageCount: answered.pageCount,
          p: p.sectionPages?.[section.layout] ?? 1,
          per: p.per,
        }
      : paginate(section.jobs, pages[section.layout] ?? 1, p.per);
    // What up and down move through: the rows on this page, in the order shown.
    // Navigation stops at the page edge rather than paging — a cursor that
    // jumps to a row nobody can see is a cursor that types into the dark.
    rowsByLayout[section.layout] = pg.slice;
    const editableOnPage = pg.slice.filter(canEditJob);
    const allPagePicked = editableOnPage.length > 0 && editableOnPage.every((j) => picked.has(j.key));

    const rows: TableRow[] = pg.slice.map((j) => ({
      key: j.key,
      go: () => p.onDrawer(j.key),
      title: isCancelled(j) ? "ยกเลิกแล้ว" + (j.cancelReason ? " — " + j.cancelReason : "")
        : wasMoved(j) ? "เลื่อนจาก " + j.origDate + (j.moveReason ? " — " + j.moveReason : "")
          : mineJ(j) ? "Your job — editable" : "Assigned to: " + j.op + " · View Only",
      // A cancelled row is faded and struck through, a moved one carries an
      // amber edge. Ownership still wins the tick and the border it already had:
      // a row you have selected has to look selected whatever else is true of
      // it, or the bulk actions act on rows you cannot see you picked.
      style: "cursor:pointer;background:" +
        (picked.has(j.key) ? "#FFF7DE"
          : isCancelled(j) ? "#F4F6F8"
            : wasMoved(j) ? "#FFFCF4"
              : mineJ(j) ? "#F4F8FC" : "#fff") +
        ";border-left:3px solid " +
        (picked.has(j.key) ? "#D89614"
          : isCancelled(j) ? "#B42318"
            : wasMoved(j) ? "#D89614"
              : mineJ(j) ? "#2E7DD1" : "transparent") +
        (isCancelled(j) ? ";opacity:.62;text-decoration:line-through" : ""),
      cells: [checkCell(j)].concat(rowCells(j, section.layout)),
    }));

    // The header of the tick column toggles that page; the rest sort.
    const headerDefs = ([[allPagePicked ? "☑" : "☐", "center"]] as [string, ("left" | "right" | "center")?][])
      .concat((COL_DEFS[section.layout] as [string][]).map((d) => {
        const active = ws.sort?.key === d[0];
        return [d[0] + (active ? (ws.sort?.dir === "asc" ? "  ↑" : "  ↓") : ""), undefined];
      }));

    const model: TableModel = {
      title: splitMixed ? SECTION_TITLE[section.layout] ?? section.layout : listTitle,
      meta: pg.total + " jobs · " + section.layout + " layout · คลิกหัวคอลัมน์เพื่อเรียง · ติ๊กเพื่อจัดการหลายงานพร้อมกัน",
      cols: cols(headerDefs, (label) => {
        if (label.startsWith("☑") || label.startsWith("☐")) { togglePageOf(editableOnPage, allPagePicked); return; }
        sortBy(label.replace(/\s+[↑↓]$/, ""));
      }),
      tools: [],
      datalists: pickLists,
      rows,
      total: pg.total,
      pageCount: pg.pageCount,
      page: pg.p,
      per: pg.per,
    };
    return { layout: section.layout, model };
  });

  const calendarDays = dates.slice(-21).map((d) => {
    const set = base.filter((j) => j.date === d);
    return {
      key: d,
      date: d.slice(0, 5),
      dow: dowOf(d),
      total: set.length,
      mine: set.filter(mineJ).length,
      delayed: set.filter((j) => RE.delayed.test(j.status)).length,
      active: d === anchor,
      width: Math.min(100, set.length * 7),
    };
  });

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("display:flex;align-items:center;gap:14px;padding:12px 18px;background:#0A2240;border-radius:5px;flex-wrap:wrap")}>
        <div style={css("width:36px;height:36px;border-radius:5px;background:#2E7DD1;color:#fff;display:flex;align-items:center;justify-content:center;font-size:13px;font-weight:600")}>
          {me.init}
        </div>
        <div style={css("display:flex;flex-direction:column;line-height:1.3")}>
          <span style={css("font-size:14.5px;font-weight:600;color:#fff")}>Welcome, {me.name}</span>
          <span style={css("font-size:11px;color:#7FA5CC")}>{me.role} · One team visibility, individual work ownership</span>
        </div>

        {/* Whether what you type is actually being kept. */}
        <span
          title={p.sync.message}
          style={css(
            "display:flex;align-items:center;gap:7px;height:26px;padding:0 11px;border-radius:13px;font-size:11px;border:1px solid " +
            (p.sync.state === "error" ? "#7A2F2A" : p.sync.state === "off" ? "#7A5A2A" : "#24476E") +
            ";background:" + (p.sync.state === "error" ? "#3A1E1C" : p.sync.state === "off" ? "#3A2E18" : "#0E2B4F") +
            ";color:" + (p.sync.state === "error" ? "#FF9C8F" : p.sync.state === "off" ? "#FFC978" : "#9FD0FF"),
          )}
        >
          <span style={css("width:7px;height:7px;border-radius:50%;background:" +
            (p.sync.state === "error" ? "#FF6B5B" : p.sync.state === "off" ? "#FFC978"
              : p.sync.state === "saving" || p.sync.state === "waking" ? "#9FD0FF" : "#3CB371"))} />
          {p.sync.state === "waking" ? "กำลังปลุกฐานข้อมูล… รอสักครู่ อย่าเพิ่งคีย์งาน"
            : p.sync.state === "saving" ? "กำลังบันทึกลงฐานข้อมูล…"
            : p.sync.state === "error" ? "บันทึกไม่สำเร็จ — กดแก้ซ้ำอีกครั้ง"
              : p.sync.state === "off" ? "ยังไม่ได้ต่อฐานข้อมูล — รีเฟรชหน้าเพื่อลองใหม่ · งานที่คีย์ไว้ตอนนี้จะหาย"
                : p.sync.at ? "บันทึกลงฐานข้อมูลแล้ว " + p.sync.at
                  : "ต่อฐานข้อมูลแล้ว"}
        </span>
        <div style={css("margin-left:auto;display:flex;align-items:center;gap:7px")}>
          {CATEGORIES.map((c) => (
            <button
              key={c}
              onClick={() => p.set({ cat: c, page: 1 })}
              style={css(
                "display:flex;align-items:center;gap:8px;height:31px;padding:0 14px;border:1px solid " +
                (ws.cat === c ? "#4E9BE8" : "#24476E") + ";background:" + (ws.cat === c ? "#16406E" : "transparent") +
                ";color:#fff;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;letter-spacing:.05em",
              )}
            >
              {c}
              <span style={css("font-family:'IBM Plex Mono',monospace;font-size:11.5px;color:" + (ws.cat === c ? "#9FD0FF" : "#7FA5CC"))}>
                {complete
                  ? inCat(c).length
                  : c === "ALL" ? serverTotal : server?.[c]?.total ?? "…"}
              </span>
            </button>
          ))}
        </div>
      </div>

      {!complete && (
        <div style={css("background:#F4F8FC;border:1px solid #BBD5EE;border-left:3px solid #2E7DD1;border-radius:5px;padding:10px 14px;font-size:11.5px;color:#475569")}>
          ตารางพร้อมแล้วจากข้อมูลแบบแบ่งหน้า · กำลังโหลดข้อมูลสรุป ตัวเลือกทั้งหมด และสิทธิ์แก้ไขในพื้นหลัง
        </div>
      )}

      {complete && (<>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:11px 14px;display:flex;align-items:center;gap:9px;flex-wrap:wrap")}>
        <button onClick={() => step(-1)} aria-label="Previous day" style={css("width:30px;height:31px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;color:#475569;cursor:pointer;font-size:13px")}>‹</button>
        {dates.slice(anchorIndex, anchorIndex + 7).map((d) => {
          const active = ws.date !== "ALL" && d === anchor;
          return (
            <button
              key={d}
              type="button"
              onClick={() => p.set({ date: d, page: 1 })}
              style={css(
                "font-family:inherit;display:flex;flex-direction:column;align-items:center;gap:1px;min-width:74px;padding:6px 10px;border:1px solid " +
                (active ? "#2E7DD1" : "#E2E8F0") + ";background:" + (active ? "#0A2240" : "#fff") +
                ";color:" + (active ? "#fff" : "#334155") + ";border-radius:4px;cursor:pointer",
              )}
            >
              <span style={css("font-size:9.5px;letter-spacing:.06em;opacity:.75")}>{dowOf(d)}</span>
              <span style={css("font-size:12.5px;font-weight:600;font-family:'IBM Plex Mono',monospace")}>{d.slice(0, 5)}</span>
              <span style={css("font-size:9.5px;opacity:.75")}>{dateCount[d]} jobs</span>
            </button>
          );
        })}
        <button onClick={() => step(1)} aria-label="Next day" style={css("width:30px;height:31px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;color:#475569;cursor:pointer;font-size:13px")}>›</button>
        <button
          onClick={() => p.set({ date: "ALL", page: 1 })}
          style={css(
            "height:31px;padding:0 14px;border:1px solid " + (ws.date === "ALL" ? "#0A2240" : "#D8E0E8") +
            ";background:" + (ws.date === "ALL" ? "#0A2240" : "#fff") + ";color:" + (ws.date === "ALL" ? "#fff" : "#475569") +
            ";border-radius:4px;font-size:12px;cursor:pointer;font-weight:600",
          )}
        >
          All dates
        </button>
        <span style={css("margin-left:auto;font-size:11.5px;color:#64748B;font-family:'IBM Plex Mono',monospace")}>
          {ws.date === "ALL" ? "All operation dates" : dowOf(anchor) + " " + anchor}
        </span>
      </div>

      {/* ปี → เดือน → วัน, the same period model the dashboard reports on. */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:11px 14px;display:flex;align-items:center;gap:12px;flex-wrap:wrap")}>
        <span style={css("font-size:11px;font-weight:700;color:#0A2240;letter-spacing:.06em")}>ช่วงเวลา</span>

        {([
          ["ปี", ws.year, ["ALL", ...years], (v: string) => v, (v: string) => p.set({ year: v, month: "ALL", date: "ALL", page: 1 })],
          ["เดือน", ws.month, ["ALL", ...months], (v: string) => (v === "ALL" ? v : monthLabel(v) + " (" + v + ")"), (v: string) => p.set({ month: v, date: "ALL", page: 1 })],
          ["วัน", ws.date, ["ALL", ...dates], (v: string) => (v === "ALL" ? v : v.slice(0, 2) + " · " + dateCount[v] + " งาน"), (v: string) => p.set({ date: v, page: 1 })],
        ] as [string, string, string[], (v: string) => string, (v: string) => void][]).map(([label, value, options, render, onPick]) => (
          <label key={label} style={css("display:flex;align-items:center;gap:6px")}>
            <span style={css("font-size:10.5px;color:#8496A8;letter-spacing:.05em;font-weight:600")}>{label}</span>
            <select
              value={value}
              onChange={(e) => onPick(e.target.value)}
              style={css("height:31px;min-width:96px;border:1px solid #D8E0E8;border-radius:4px;background:#F8FAFC;font-size:12.5px;color:#16232F;padding:0 8px;outline:none;cursor:pointer")}
            >
              {options.map((o) => <option key={o} value={o}>{o === "ALL" ? "ทั้งหมด" : render(o)}</option>)}
            </select>
          </label>
        ))}

        {(ws.year !== "ALL" || ws.month !== "ALL" || ws.date !== "ALL") && (
          <button
            onClick={() => p.set({ year: "ALL", month: "ALL", date: "ALL", page: 1 })}
            style={css("height:29px;padding:0 12px;border:1px solid #BBD5EE;background:#F4F8FC;border-radius:4px;font-size:11.5px;color:#0A2240;font-weight:600;cursor:pointer")}
          >
            ล้างช่วงเวลา
          </button>
        )}

        <span style={css("margin-left:auto;display:flex;align-items:baseline;gap:8px")}>
          <span style={css("font-size:15px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{base.length}</span>
          <span style={css("font-size:11.5px;color:#64748B")}>จาก {catBase.length} งานในหมวดนี้</span>
          {!!undated && (
            <span style={css("font-size:11px;color:#B45309")} title="งานที่วันที่ยังไม่ถูกต้อง จะไม่ถูกนับเมื่อเลือกปีหรือเดือน">
              · วันที่ใช้ไม่ได้ {undated}
            </span>
          )}
        </span>
      </div>
      </>)}

      {/* What is narrowing the grid, in one line, with a way out of each. */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:9px 12px;display:flex;align-items:center;gap:8px;flex-wrap:wrap")}>
        <span style={css("font-size:10px;font-weight:700;color:#0A2240;letter-spacing:.06em")}>กำลังดู</span>
        <span style={css("font-size:12.5px;font-weight:600;color:#0A2240;font-family:'IBM Plex Mono',monospace")}>{complete ? list.length : serverTotal}</span>
        <span style={css("font-size:11.5px;color:#64748B")}>จาก {complete ? all.length : serverTotal} งาน</span>

        {activeFilters.length ? activeFilters.map(([label, value, clear]) => (
          <button
            key={label + value}
            onClick={clear}
            title={"เอา " + label + " ออก"}
            style={css("height:24px;padding:0 8px;border:1px solid #BBD5EE;background:#F4F8FC;color:#0A2240;border-radius:12px;font-size:11px;cursor:pointer;display:flex;align-items:center;gap:6px")}
          >
            <span style={css("color:#64748B")}>{label}:</span>
            <span style={css("font-weight:600")}>{value}</span>
            <span style={css("color:#94A3B8")}>✕</span>
          </button>
        )) : (
          <span style={css("font-size:11.5px;color:#94A3B8")}>
            {complete ? "ยังไม่ได้กรองอะไร — เห็นงานทั้งแผน" : "แสดงหน้าข้อมูลล่าสุดจากเซิร์ฟเวอร์"}
          </span>
        )}

        {activeFilters.length > 1 && (
          <button onClick={clearAll} style={css("height:24px;padding:0 10px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:12px;font-size:11px;cursor:pointer")}>
            ล้างทั้งหมด
          </button>
        )}

        <span style={css("margin-left:auto;display:flex;gap:6px;flex-wrap:wrap")}>
          {complete ? ([["kpi", "KPI"], ["process", "ขั้นตอนงาน"], ["team", "ภาระทีม"], ["filters", "ตัวกรอง"]] as [keyof PanelPrefs, string][]).map(([key, label]) => (
            <button key={key} onClick={panel(key)} style={css(foldStyle(p.panels[key]))}>
              {p.panels[key] ? "▾" : "▸"} {label}
            </button>
          )) : <span style={css("font-size:11px;color:#2E7DD1")}>กำลังเตรียมข้อมูลสรุป…</span>}
        </span>
      </div>

      {complete && p.panels.kpi && (
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(148px,1fr));gap:10px")}>
        {KPI_DEFS.map((k) => (
          <button
            key={k[2]}
            type="button"
            onClick={() => p.set({ kpi: ws.kpi === k[2] ? "All" : k[2], page: 1 })}
            style={css(
              "font-family:inherit;text-align:left;width:100%;" +
              "background:" + (ws.kpi === k[2] ? "#F4F8FC" : "#fff") + ";border:1px solid " +
              (ws.kpi === k[2] ? "#2E7DD1" : "#D8E0E8") + ";border-top:3px solid " + k[3] +
              ";border-radius:5px;padding:11px 13px;cursor:pointer",
            )}
          >
            <div style={css("font-size:10px;font-weight:700;color:#475569;letter-spacing:.06em")}>{k[0]}</div>
            <div style={css("font-size:10px;color:#94A3B8")}>{k[1]}</div>
            <div style={css("font-size:25px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:" + k[3] + ";letter-spacing:-.02em;margin-top:6px")}>
              {kpiCount(scope, k[2], mineJ)}
            </div>
          </button>
        ))}
      </div>
      )}

      {complete && (ws.tab !== "CALENDAR" ? (
        <div style={css("display:flex;flex-direction:column;gap:13px")}>
          {p.panels.process && (
          <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
            <div style={css("display:flex;align-items:baseline;gap:10px;margin-bottom:12px;flex-wrap:wrap")}>
              <h3 style={css("margin:0;font-size:13.5px;font-weight:600;color:#0A2240")}>
                Operation Process Progress · ความคืบหน้ากระบวนการทำงาน
              </h3>
              <span style={css("font-size:11px;color:#94A3B8")}>
                {(ladderCat || "ทุกประเภทงาน") + " · " + boardScope.length + " งานในมุมมองนี้ · คลิกการ์ดเพื่อกรองตามสถานะ"}
              </span>
              {ws.status !== "ALL" && (
                <button
                  onClick={() => p.set({ status: "ALL", page: 1 })}
                  style={css("margin-left:auto;height:26px;padding:0 11px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:11px;color:#475569;cursor:pointer")}
                >
                  ล้างตัวกรองสถานะ ({ws.status})
                </button>
              )}
            </div>
            <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(166px,1fr));gap:10px")}>
              {stages.map((st) => {
                const active = ws.status === st.status;
                const pct = boardScope.length ? (st.n / boardScope.length) * 100 : 0;
                return (
                  <button
                    key={st.status}
                    type="button"
                    onClick={() => p.set({ status: active ? "ALL" : st.status, page: 1 })}
                    title={st.n + " jobs · " + st.status + (st.off ? " — ไม่อยู่ในลำดับสถานะของงาน " + (ladderCat || "นี้") : "")}
                    style={css(
                      "font-family:inherit;text-align:left;width:100%;border:1px " + (st.off ? "dashed #E7C9A0" : "solid " + (active ? "#2E7DD1" : "#E2E8F0")) +
                      ";background:" + (active ? "#F4F8FC" : st.off ? "#FFFBF2" : "#F8FAFC") + ";border-radius:5px;padding:11px 12px;cursor:pointer",
                    )}
                  >
                    <div style={css("display:flex;justify-content:space-between;align-items:center;margin-bottom:7px")}>
                      <span style={css("width:22px;height:22px;border-radius:3px;background:" + (st.off ? "#B45309" : active ? "#2E7DD1" : "#0A2240") + ";color:#fff;font-size:11px;font-weight:600;display:flex;align-items:center;justify-content:center;font-family:'IBM Plex Mono',monospace")}>
                        {st.step}
                      </span>
                      <span style={css("font-size:17px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:" + (st.n ? "#0A2240" : "#CBD5E1"))}>
                        {st.n}
                      </span>
                    </div>
                    <div style={css("font-size:12px;font-weight:600;color:" + (st.n ? "#0A2240" : "#94A3B8") + ";line-height:1.3;word-break:break-word")}>{st.status}</div>
                    <div style={css("font-size:10.5px;color:" + (st.off ? "#B45309" : "#94A3B8") + ";margin-bottom:8px")}>{st.th || "—"}</div>
                    <div style={css("height:5px;background:#E2E8F0;border-radius:3px;overflow:hidden")}>
                      <span style={css("display:block;height:100%;width:" + pct.toFixed(1) + "%;background:" + (st.off ? "#B45309" : active ? "#2E7DD1" : "#94A3B8"))} />
                    </div>
                  </button>
                );
              })}
            </div>
          </div>
          )}

          {p.panels.team && (
          <div style={css("display:flex;gap:10px;flex-wrap:wrap")}>
            {workload.map((w) => (
              <button
                key={w.name}
                type="button"
                onClick={() => p.set({ assignee: w.name, tab: "PENDING", page: 1 })}
                style={css(
                  "font-family:inherit;text-align:left;" +
                  "flex:1;min-width:172px;border:1px solid " + (w.name === me.name ? "#BBD5EE" : "#E9EFF5") +
                  ";background:" + (w.name === me.name ? "#F4F8FC" : "#fff") + ";border-radius:5px;padding:12px 13px;cursor:pointer",
                )}
              >
                <div style={css("display:flex;align-items:center;gap:9px")}>
                  <span style={css(
                    "width:26px;height:26px;border-radius:4px;background:" + (w.name === me.name ? "#2E7DD1" : "#E2E8F0") +
                    ";color:" + (w.name === me.name ? "#fff" : "#475569") +
                    ";font-size:10.5px;font-weight:600;display:flex;align-items:center;justify-content:center",
                  )}>
                    {w.init}
                  </span>
                  <span style={css("font-size:12.5px;font-weight:600;color:#0A2240;flex:1")}>{w.name}</span>
                  <span style={css("font-size:16px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{w.total}</span>
                </div>
                <div style={css("height:6px;background:#F1F5F9;border-radius:3px;overflow:hidden;margin:9px 0 8px")}>
                  <span style={css(
                    "display:block;height:100%;width:" + Math.min(100, (w.total / Math.max(1, base.length / 3)) * 100) +
                    "%;background:" + (w.name === me.name ? "#2E7DD1" : "#94A3B8"),
                  )} />
                </div>
                <div style={css("display:flex;gap:12px")}>
                  {([["open", w.open, "#475569"], ["running", w.running, "#0A6E8A"], ["delay", w.delay, w.delay > 0 ? "#B42318" : "#94A3B8"], ["done", w.done, "#16794C"]] as [string, number, string][]).map((s) => (
                    <span key={s[0]} style={css("display:flex;flex-direction:column")}>
                      <span style={css("font-size:13px;font-family:'IBM Plex Mono',monospace;color:" + s[2] + (s[0] === "delay" ? ";font-weight:600" : ""))}>{s[1]}</span>
                      <span style={css("font-size:9px;color:#94A3B8;letter-spacing:.04em")}>{s[0].toUpperCase()}</span>
                    </span>
                  ))}
                </div>
              </button>
            ))}
          </div>
          )}

          {p.panels.filters && (
          <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:11px 14px;overflow-x:auto")}>
            <div style={css("display:grid;grid-template-columns:minmax(170px,1.15fr) repeat(3,minmax(150px,1fr)) auto auto;align-items:end;gap:10px;min-width:1030px")}>
              {([
                ["ผู้รับผิดชอบ", ws.tab === "MY JOBS" ? "My Work" : ws.assignee, ["All Team", "My Work", ...M.operators], (v: string) => p.set({ assignee: v, page: 1 }), ws.tab === "MY JOBS", "#0A2240"],
                ["ลูกค้า", ws.cust, ["ALL", ...rankedOptionsOf("customer")], (v: string) => p.set({ cust: v, page: 1 }), false, "#2E7DD1"],
                ["ผู้ขนส่ง", ws.trucker, ["ALL", ...rankedOptionsOf("trucker")], (v: string) => p.set({ trucker: v, page: 1 }), false, "#16794C"],
                ["ประเภทรถ / ตู้", ws.type, ["ALL", ...rankedOptionsOf("type")], (v: string) => p.set({ type: v, page: 1 }), false, "#B45309"],
              ] as [string, string, string[], (value: string) => void, boolean, string][]).map(([label, value, options, onPick, disabled, tone]) => {
                const active = value !== "ALL" && value !== "All Team";
                return (
                  <label key={label} style={css("display:flex;flex-direction:column;gap:5px;min-width:0")}>
                    <span style={css("font-size:10px;font-weight:700;color:#64748B;letter-spacing:.05em")}>{label}</span>
                    <select
                      value={value}
                      disabled={disabled}
                      onChange={(e) => onPick(e.target.value)}
                      style={css(
                        "width:100%;height:34px;border:1px solid " + (active ? tone : "#D8E0E8") +
                        ";border-radius:4px;background:" + (disabled ? "#F1F5F9" : active ? "#F4F8FC" : "#fff") +
                        ";color:#16232F;padding:0 30px 0 9px;font-size:12px;font-weight:" + (active ? "600" : "400") +
                        ";outline:none;cursor:" + (disabled ? "not-allowed" : "pointer"),
                      )}
                    >
                      {options.map((option) => (
                        <option key={option} value={option}>
                          {option === "ALL" || option === "All Team" ? "ทั้งหมด" : option === "My Work" ? "งานของฉัน" : option}
                        </option>
                      ))}
                    </select>
                  </label>
                );
              })}

              <button
                type="button"
                disabled={ws.cust === "ALL" && ws.trucker === "ALL" && ws.type === "ALL" && (ws.tab === "MY JOBS" || ws.assignee === "All Team")}
                onClick={() => p.set({ assignee: "All Team", cust: "ALL", trucker: "ALL", type: "ALL", page: 1 })}
                style={css(
                  "height:34px;padding:0 13px;border:1px solid #D8E0E8;border-radius:4px;background:#fff;color:#475569;" +
                  "font-size:11.5px;font-weight:600;white-space:nowrap;cursor:pointer",
                )}
              >
                ล้างตัวกรอง
              </button>

              <span style={css("height:34px;display:flex;align-items:center;gap:10px;white-space:nowrap;font-size:10.5px;color:#64748B")}>
                <span style={css("display:flex;align-items:center;gap:5px")}>
                  <span style={css("width:10px;height:10px;border-radius:2px;background:#F4F8FC;border:1px solid #2E7DD1")} /> แก้ไขได้
                </span>
                <span style={css("display:flex;align-items:center;gap:5px")}>
                  <span style={css("width:10px;height:10px;border-radius:2px;background:#fff;border:1px solid #D8E0E8")} /> ดูอย่างเดียว
                </span>
              </span>
            </div>
          </div>
          )}
        </div>
      ) : (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:16px 18px")}>
          <div style={css("display:flex;justify-content:space-between;align-items:baseline;margin-bottom:14px")}>
            <h3 style={css("margin:0;font-size:13.5px;font-weight:600;color:#0A2240")}>Operation Calendar · ปฏิทินงาน</h3>
            <span style={css("font-size:11px;color:#94A3B8")}>Click a day to open that date in Team Work</span>
          </div>
          <div style={css("display:grid;grid-template-columns:repeat(7,1fr);gap:8px")}>
            {calendarDays.map((d) => (
              <button
                key={d.key}
                type="button"
                onClick={() => p.set({ date: d.key, tab: "PENDING", page: 1 })}
                style={css(
                  "font-family:inherit;text-align:left;width:100%;" +
                  "border:1px solid " + (d.active ? "#2E7DD1" : "#E9EFF5") + ";background:" + (d.active ? "#F4F8FC" : "#fff") +
                  ";border-radius:4px;padding:10px 11px;min-height:96px;display:flex;flex-direction:column;gap:5px;cursor:pointer",
                )}
              >
                <div style={css("display:flex;justify-content:space-between;align-items:baseline")}>
                  <span style={css("font-size:14px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{d.date}</span>
                  <span style={css("font-size:10px;color:#94A3B8")}>{d.dow}</span>
                </div>
                <div style={css("height:5px;border-radius:3px;background:#2E7DD1;width:" + d.width + "%")} />
                <div style={css("display:flex;flex-direction:column;gap:1px;margin-top:auto")}>
                  <span style={css("font-size:10.5px;color:#475569")}>{d.total} jobs</span>
                  <span style={css("font-size:10.5px;color:#2E7DD1;font-weight:600")}>{d.mine} mine</span>
                  <span style={css("font-size:10.5px;color:#B42318")}>{d.delayed} delayed</span>
                </div>
              </button>
            ))}
          </div>
        </div>
      ))}

      {/* One bar for the whole selection, however many grids it spans. */}
      {!!pickedJobs.length && (
            <div style={css("padding:10px 14px;background:#FFF7DE;border:1px solid #EADFC8;border-radius:5px;display:flex;align-items:center;gap:10px;flex-wrap:wrap")}>
              <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>
                เลือกไว้ {pickedJobs.length} งาน
              </span>
              <span style={css("font-size:11px;color:#64748B")}>เปลี่ยนพร้อมกันได้ทั้งชุด · แก้ได้เฉพาะงานของคุณ</span>

              <select
                defaultValue=""
                onChange={(e) => {
                  if (!e.target.value) return;
                  p.onBulkStatus(pickedJobs.map((j) => j.key), e.target.value);
                  e.target.value = "";
                }}
                style={css("height:30px;border:1px solid #D8E0E8;border-radius:4px;background:#fff;font-size:12px;padding:0 8px;cursor:pointer")}
              >
                <option value="">เปลี่ยนสถานะเป็น…</option>
                {/* Delayed is deliberately absent: it needs a reason, which the delay form collects one job at a time. */}
                {(STATUS_LADDER[pickedJobs[0]?.cat ?? "IMPORT"] ?? STATUS_LADDER.IMPORT)
                  .filter((s) => !/delay/i.test(s))
                  .map((s) => <option key={s} value={s}>{s}</option>)}
              </select>

              {canAssign && (
                <select
                  defaultValue=""
                  onChange={(e) => {
                    if (!e.target.value) return;
                    p.onBulkAssign(pickedJobs.map((j) => j.key), e.target.value);
                    e.target.value = "";
                  }}
                  style={css("height:30px;border:1px solid #D8E0E8;border-radius:4px;background:#fff;font-size:12px;padding:0 8px;cursor:pointer")}
                >
                  <option value="">มอบหมายให้…</option>
                  {M.operators.map((o) => <option key={o} value={o}>{o}</option>)}
                </select>
              )}

              <button
                onClick={() => p.onBulkDelete(pickedJobs.map((j) => j.key))}
                style={css("margin-left:auto;height:30px;padding:0 12px;border:1px solid #F3C3BE;background:#FDF6F5;border-radius:4px;font-size:12px;color:#B42318;font-weight:600;cursor:pointer")}
              >
                ลบงานที่เลือก
              </button>
              <button
                onClick={() => p.set({ picked: [] })}
                style={css("height:30px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:12px;color:#475569;cursor:pointer")}
              >
                ล้างการเลือก
              </button>
            </div>
      )}

      {/* Both are rendered; the stylesheet shows one. `.grid-only` is hidden on
          a phone and `.cards-only` on everything else, so neither has to ask how
          wide the screen is — see the note in JobCards. */}
      {grids.map((grid) => (
        <div key={grid.layout}>
          <div className="grid-only">
            <DataTable
              model={grid.model}
              onPage={(page) => (p.onSectionPage ?? setPage)(grid.layout, page)}
              onTool={() => undefined}
            />
          </div>
          <JobCards
            jobs={rowsByLayout[grid.layout] ?? []}
            mine={mineJ}
            onOpen={p.onDrawer}
          />
        </div>
      ))}

      {!canAssign && (
        <span style={css("font-size:11px;color:#94A3B8")}>
          Reassignment is limited to Supervisor and above — open a job to request a change.
        </span>
      )}
    </div>
  );
}
