import type { Db } from "./demo";
import { NAV, type Screen } from "./nav";
import type { Job, Ops } from "./ops";

/**
 * One search box across the whole application.
 *
 * The header search used to narrow the demo shipment list and nothing else, so
 * looking up a container number found nothing unless you were already standing
 * on the right screen. This walks every source the app carries — the real
 * operation jobs first, then the registers and the menu itself — and every hit
 * carries the action that opens it where it lives.
 */

/** What opening a result does. */
export type SearchAction =
  | { kind: "job"; key: string }
  | { kind: "customer"; value: string }
  | { kind: "trucker"; value: string }
  | { kind: "supplier"; name: string }
  | { kind: "ship"; id: number }
  | { kind: "screen"; screen: Screen; note?: string };

export type SearchHit = {
  id: string;
  /** Group heading, e.g. "งานปฏิบัติการ · Operation jobs". */
  group: string;
  title: string;
  sub: string;
  /** Short tag on the right, usually the record type or status. */
  tag: string;
  tone: "dark" | "blue" | "teal" | "amber" | "green" | "gray";
  action: SearchAction;
  /** Demo-sourced hits say so, so nobody reads them as operational truth. */
  demo?: boolean;
};

export type SearchGroup = { group: string; hits: SearchHit[]; more: number };

const LIMIT_PER_GROUP = 6;

const norm = (v: unknown) => String(v ?? "").toLowerCase();

function matches(needle: string, fields: (string | undefined)[]): boolean {
  return fields.some((f) => f && norm(f).indexOf(needle) >= 0);
}

/** Counts jobs by a field so "BOTTCHER" can answer with the customer, not 12 rows. */
function tally(jobs: Job[], pick: (j: Job) => string | undefined) {
  const out: Record<string, number> = {};
  jobs.forEach((j) => {
    const key = (pick(j) || "").trim();
    if (key) out[key] = (out[key] || 0) + 1;
  });
  return out;
}

export function globalSearch(query: string, ops: Ops | null, db: Db): SearchGroup[] {
  const q = query.trim().toLowerCase();
  if (q.length < 2) return [];

  const groups: SearchGroup[] = [];
  const push = (group: string, hits: SearchHit[]) => {
    if (!hits.length) return;
    groups.push({ group, hits: hits.slice(0, LIMIT_PER_GROUP), more: Math.max(0, hits.length - LIMIT_PER_GROUP) });
  };

  const jobs = ops?.jobs ?? [];

  // ---- real operation jobs ------------------------------------------------
  const jobHits: SearchHit[] = jobs
    .filter((j) => matches(q, [
      j.jobCode, j.abs, j.booking, j.container, j.seal, j.licence, j.driver, j.contact,
      j.customer, j.trucker, j.destination, j.plant, j.sid, j.jobNo, j.wh, j.op, j.status, j.type,
    ]))
    .map((j) => ({
      id: "job-" + j.key,
      group: "งานปฏิบัติการ · Operation jobs",
      title: (j.jobCode || j.abs || j.jobNo || j.customer) + " · " + j.customer,
      sub: [j.cat, j.date, j.trucker, j.container, j.licence, j.driver].filter(Boolean).join(" · "),
      tag: j.status || j.cat,
      tone: j.cat === "IMPORT" ? "dark" : j.cat === "EXPORT" ? "blue" : "teal",
      action: { kind: "job", key: j.key },
    }));
  push("งานปฏิบัติการ · Operation jobs", jobHits);

  // ---- master values, answered as a set rather than as rows ---------------
  const customers = tally(jobs, (j) => j.customer);
  const custHits: SearchHit[] = Object.keys(customers)
    .filter((c) => norm(c).indexOf(q) >= 0)
    .sort((a, b) => customers[b] - customers[a])
    .map((c) => ({
      id: "cust-" + c,
      group: "ลูกค้า · Customers",
      title: c,
      sub: customers[c] + " งานในแผน · เปิดใน Operation Workspace",
      tag: String(customers[c]),
      tone: "blue" as const,
      action: { kind: "customer", value: c },
    }));
  push("ลูกค้า · Customers", custHits);

  const truckers = tally(jobs, (j) => j.trucker);
  const truckerHits: SearchHit[] = Object.keys(truckers)
    .filter((t) => norm(t).indexOf(q) >= 0)
    .sort((a, b) => truckers[b] - truckers[a])
    .map((t) => ({
      id: "trucker-" + t,
      group: "ผู้ขนส่ง · Subcontractors",
      title: t,
      sub: truckers[t] + " เที่ยวในแผน · เปิดใน Operation Workspace",
      tag: String(truckers[t]),
      tone: "green" as const,
      action: { kind: "trucker", value: t },
    }));

  // ---- supplier register --------------------------------------------------
  const supplierHits: SearchHit[] = db.suppliers
    .filter((s) => matches(q, [s.code, s.name, s.vendor, s.contact, s.phone, s.email, s.area, s.service]))
    .map((s) => ({
      id: "sup-" + s.code,
      group: "ผู้ขนส่ง · Subcontractors",
      title: s.name,
      sub: s.code + " · " + s.contact + " · " + s.phone + " · เปิดประวัติผู้รับเหมาช่วง",
      tag: s.status,
      tone: s.status === "Approved" ? "green" : s.status === "Suspended" ? "amber" : "gray",
      action: { kind: "supplier", name: s.name },
      demo: true,
    }));
  push("ผู้ขนส่ง · Subcontractors", truckerHits.concat(supplierHits));

  // ---- demo shipment register (still reachable from Billing) --------------
  const shipHits: SearchHit[] = db.ships
    .filter((s) => matches(q, [s.abs, s.job, s.bkg, s.cont, s.plate, s.driver, s.cust, s.sub, s.from, s.to]))
    .map((s) => ({
      id: "ship-" + s.id,
      group: "งานขนส่ง (ข้อมูลจำลอง) · Shipment records",
      title: s.abs + " · " + s.cust,
      sub: [s.job, s.dir, s.sub, s.cont, s.plate].filter(Boolean).join(" · "),
      tag: s.status,
      tone: "gray",
      action: { kind: "ship", id: s.id },
      demo: true,
    }));
  push("งานขนส่ง (ข้อมูลจำลอง) · Shipment records", shipHits);

  // ---- rate cards and CAR/PAR --------------------------------------------
  const rateHits: SearchHit[] = db.rates
    .filter((r) => matches(q, [r.cust, r.sub, r.from, r.to, r.truck, r.contType]))
    .map((r, i) => ({
      id: "rate-" + i + r.cust + r.sub,
      group: "อัตราค่าขนส่ง · Rate cards",
      title: r.cust + " → " + r.sub,
      sub: r.from + " → " + r.to + " · " + r.truck + " · ฿" + r.sell.toLocaleString("en-US"),
      tag: r.status,
      tone: r.status === "Active" ? "green" : "amber",
      action: { kind: "screen", screen: "rates" },
      demo: true,
    }));
  push("อัตราค่าขนส่ง · Rate cards", rateHits);

  const carparHits: SearchHit[] = db.carpar
    .filter((c) => matches(q, [c.no, c.cust, c.sub, c.type, c.owner, c.status]))
    .map((c) => ({
      id: "carpar-" + c.no,
      group: "CAR / PAR",
      title: c.no + " · " + c.sub,
      sub: c.type + " · เจ้าของเรื่อง " + c.owner + " · ครบกำหนด " + c.target,
      tag: c.status,
      tone: c.status === "Closed" ? "green" : c.status === "Overdue" ? "amber" : "blue",
      action: { kind: "screen", screen: "carpar" },
      demo: true,
    }));
  push("CAR / PAR", carparHits);

  // ---- the menu itself ----------------------------------------------------
  const screenHits: SearchHit[] = NAV
    .filter(([, label, th]) => norm(label).indexOf(q) >= 0 || th.indexOf(query.trim()) >= 0)
    .map(([key, label, th]) => ({
      id: "screen-" + key,
      group: "เมนู · Screens",
      title: label,
      sub: th,
      tag: "เปิดหน้านี้",
      tone: "gray" as const,
      action: { kind: "screen", screen: key },
    }));
  push("เมนู · Screens", screenHits);

  return groups;
}

export function countHits(groups: SearchGroup[]): number {
  return groups.reduce((sum, g) => sum + g.hits.length + g.more, 0);
}
