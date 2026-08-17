import type { TableModel, TableRow } from "./DataTable";
import type { Db, Ship } from "./demo";
import { cell, cols, fdate, money, pad, paginate, searched } from "./util";

export type Filters = {
  dir: string; cust: string; sub: string; truck: string; status: string; month: string;
};

type Args = {
  screen: string;
  tab: string;
  db: Db;
  filtered: Ship[];
  q: string;
  page: number;
  /** Rows per page, from the viewer's settings. */
  per: number;
  onSort: (key: string) => void;
  selectShip: (id: number) => void;
  toast: (message: string) => void;
};

/** Builds the table model for every list-style screen. Returns null for screens that have none. */
export function buildTable(a: Args): TableModel | null {
  const { screen, tab, db, q, page } = a;

  const wrap = (
    title: string,
    meta: (total: number) => string,
    colDefs: [string, ("left" | "right" | "center")?][],
    rows: TableRow[],
    total: number,
    pageCount: number,
    p: number,
    per: number,
  ): TableModel => ({
    title, meta: meta(total), cols: cols(colDefs, a.onSort), rows, total, pageCount, page: p, per,
  });

  // The Subcontractor register and CAR/PAR have their own screens now, reading
  // the real register over the API — see screens/Suppliers.tsx and
  // screens/Incidents.tsx. Nothing is drawn for either name here: a generated
  // register underneath the real one is how a system ends up giving two answers
  // to the same question.

  // Transportation Rates has its own screen now — it reads the subcontractors'
  // real quotations, which price in diesel bands and cannot be shown as one
  // number per lane. See screens/Rates.tsx.

  if (screen === "billing") {
    let list = a.filtered.filter((s) => s.status === "Completed");
    if (tab === "Advance Receipts") list = list.slice(0, 14);
    list = searched(list, ["abs", "cust", "sub", "bill"], q);

    const pg = paginate(list, page, a.per);
    const rows: TableRow[] = pg.slice.map((s) => {
      const add = Math.round((s.cost * 0.06) / 10) * 10;
      const tone = s.bill === "Overdue" ? "red" : s.bill === "Due Soon" ? "amber" : s.bill === "Completed" ? "gray" : "green";
      return {
        key: String(s.id),
        go: () => a.selectShip(s.id),
        style: "cursor:pointer;background:#fff",
        cells: [
          cell(s.abs, { mono: true, bold: true }), cell(s.job, { mono: true }), cell(s.cust), cell(s.sub, { bold: true }),
          cell(fdate(s.plan), { mono: true }),
          cell(s.invDays === null ? "—" : fdate(new Date(s.plan.getTime() + s.invDays * 86400000)), { mono: true }),
          cell(s.invDays === null ? "—" : s.invDays + " d", {
            mono: true, align: "right",
            color: s.invDays !== null && s.invDays > 4 ? "#B42318" : s.invDays === 3 ? "#B45309" : "#16794C",
          }),
          cell(money(s.cost), { mono: true, align: "right" }), cell(money(add), { mono: true, align: "right" }),
          cell(s.id % 4 === 0 ? money(s.cost * 0.3) : "—", { mono: true, align: "right", mute: true }),
          cell(money(s.cost + add), { mono: true, align: "right", bold: true }),
          cell(s.bill, { tone }),
        ],
      };
    });
    return wrap("Billing Monitor", (t) => t + " completed jobs · KPI: supplier invoice within 4 calendar days",
      [["ABS No."], ["Job No."], ["Customer"], ["Subcontractor"], ["Completion Date"], ["Invoice Received"],
        ["Days After"], ["Trucking Charge"], ["Additional"], ["Advance Receipt"], ["Total"], ["Billing Status"]],
      rows, pg.total, pg.pageCount, pg.p, pg.per);
  }

  if (screen === "documents") {
    const kinds: [string, string][] = [
      ["Cargo Insurance Certificate", "Compliance"], ["Transport Licence", "Compliance"],
      ["Safety Training Record", "Safety"], ["Driver Licence", "Safety"],
      ["Vehicle Registration", "Compliance"], ["ISO 9001 Certificate", "Quality"],
      ["Service Agreement", "Contract"], ["Rate Agreement", "Contract"],
    ];
    const docs: { id: string; sup: string; name: string; cat: string; up: string; exp: string; by: string; size: string }[] = [];
    db.suppliers.forEach((s, i) =>
      kinds.forEach((k, j) => {
        if ((i + j) % 3 !== 0) return;
        docs.push({
          id: "DOC-" + (5100 + i * 8 + j), sup: s.name, name: k[0], cat: k[1],
          up: "12 Mar 2026", exp: j % 3 === 0 ? "30 Sep 2026" : "31 Mar 2027",
          by: "K. Wanida", size: 120 + i * 37 + " KB",
        });
      }),
    );
    let list = tab === "Expiring" ? docs.filter((d) => d.exp.indexOf("2026") > 0) : docs;
    list = searched(list, ["id", "sup", "name", "cat"], q);

    const pg = paginate(list, page, a.per);
    const rows: TableRow[] = pg.slice.map((d) => ({
      key: d.id,
      go: () => a.toast(d.name + " — preview"),
      style: "cursor:pointer;background:#fff",
      cells: [
        cell(d.id, { mono: true, bold: true }), cell(d.sup, { bold: true }), cell(d.name),
        cell(d.cat, { tone: "blue" }), cell(d.up, { mono: true, mute: true }),
        cell(d.exp, { mono: true, color: d.exp.indexOf("2026") > 0 ? "#B45309" : null }),
        cell(d.by), cell(d.size, { mono: true, mute: true, align: "right" }),
        cell(d.exp.indexOf("2026") > 0 ? "Expiring" : "Valid", { tone: d.exp.indexOf("2026") > 0 ? "amber" : "green" }),
      ],
    }));
    return wrap("Document Register", (t) => t + " controlled documents",
      [["Doc ID"], ["Supplier"], ["Document Name"], ["Category"], ["Uploaded"], ["Expiry"], ["Uploaded By"], ["Size"], ["Status"]],
      rows, pg.total, pg.pageCount, pg.p, pg.per);
  }

  if (screen === "admin") return adminTable(a);
  return null;
}

/**
 * The Azure SQL schema, as designed and as built.
 *
 * The status column is the point of this table: it says which of the planned
 * tables exist today and which are still on paper, so nobody reads the design
 * as a description of the system. Kept in step with
 * server/Scmos.Api/Data/ScmosDbContext.cs.
 *
 * [entity, primary key, related to, key attributes, status, notes]
 */
const ENTITIES: string[][] = [
  ["Users", "user_id", "Roles", "email, display_name, operator_id", "planned", "วันนี้ยังอ่านจาก Web App Login ตรงๆ ไม่มีตาราง"],
  ["Roles", "role_id", "Permissions", "name, level", "planned", "ตอนนี้เป็นค่าคงที่ใน Auth/Directory.cs"],
  ["Permissions", "permission_id", "Roles", "action, scope", "planned", "จำเป็นก่อนทำ AI Permission Matrix"],

  ["Customers", "customer_id", "—", "name, code", "planned", "ตอนนี้เป็นข้อความในตัวงาน 102 ราย"],
  ["Suppliers", "supplier_id", "—", "name, code, status, approved_at", "planned", "ตัวบล็อกหลัก — แก้ปัญหาชื่อไม่ตรงกัน TATIYAPOL/TTP"],
  ["SupplierContacts", "contact_id", "Suppliers", "name, phone, email", "planned", ""],
  ["SupplierDocuments", "document_id", "Suppliers", "kind, expiry, object_key", "planned", "ผูกกับ Blob"],
  ["SupplierTrucks", "truck_id", "Suppliers", "plate, type, dg_capable", "planned", ""],
  ["SupplierDrivers", "driver_id", "Suppliers", "name, licence, training_expiry", "planned", ""],
  ["SupplierCapacity", "capacity_id", "Suppliers", "date, truck_type, available", "planned", ""],

  ["Bookings", "booking_id", "Customers", "reference, requested_at", "partial", "รวมอยู่ใน operation_jobs"],
  ["Shipments", "shipment_id", "Bookings", "cat, date, container, status", "built", "operation_jobs · 2,102 แถว"],
  ["ShipmentEvents", "event_id", "Shipments", "stage, planned, actual, photo", "built", "shipment_milestones"],
  ["ShipmentStatusHistory", "history_id", "Shipments", "from, to, hold, by, at", "built", "workflow_events · append-only"],

  ["TruckAssignments", "assignment_id", "Shipments, Suppliers", "rank, outcome, response_minutes", "built", "supplier_requests"],
  ["PreRunConfirmations", "prerun_id", "Shipments", "sent_at, responded_at, correction", "built", "pre_run_checks"],

  ["Rates", "rate_id", "Suppliers, Customers", "lane, vehicle, price per band", "partial", "2,270 เส้นทางอยู่ในไฟล์ ยังไม่เข้า SQL"],
  ["RateHistory", "rate_history_id", "Rates", "effective_from, changed_by", "planned", ""],
  ["FuelBands", "band_id", "—", "min, max, label", "partial", "24 ช่วง คำนวณตอนอ่านไฟล์"],

  ["Documents", "document_id", "Shipments", "kind, object_key, uploaded_by", "partial", "report_uploads + operation_uploads"],
  ["DocumentVersions", "version_id", "Documents", "version, replaced_at", "planned", ""],
  ["DocumentVerification", "verification_id", "Documents", "bl_match, image_clear, by", "planned", "ขั้นตอนมีใน workflow แล้ว ยังไม่มีตาราง"],

  ["Incidents", "incident_id", "Shipments", "category, 5W1H, ai_summary", "built", "incident_cases"],
  ["CARPAR", "case_id", "Incidents", "kind, stage, due_date, approved_by", "built", "incident_cases (kind = CAR/PAR)"],
  ["CorrectiveActions", "action_id", "CARPAR", "action, owner, due", "partial", "เป็นคอลัมน์ใน incident_cases"],
  ["PreventiveActions", "action_id", "CARPAR", "action, owner, due", "partial", "เป็นคอลัมน์ใน incident_cases"],

  ["Audits", "audit_id", "Suppliers", "type, date, auditor, result", "planned", "Pre-Audit / Audit ใน Supplier flow"],
  ["AuditFindings", "finding_id", "Audits", "finding, severity, cap", "planned", ""],

  ["KPIs", "kpi_id", "—", "measure, period, value, base", "partial", "คำนวณสดทุกครั้ง ยังไม่เก็บผลย้อนหลัง"],
  ["DelayReasons", "delay_id", "Shipments", "category, responsible, impact", "built", "delay_records · 8 หมวดตายตัว"],

  ["Notifications", "notification_id", "Users", "kind, read_at", "planned", ""],
  ["Approvals", "approval_id", "Users", "action, state, approved_by", "planned", "จำเป็นสำหรับ AI ⚠️ Approval"],
  ["AuditLogs", "log_id", "Users", "entity, action, before, after", "built", "workflow_events ครอบคลุมงานขนส่ง"],
];

function adminTable(a: Args): TableModel {
  const { tab, q, page } = a;

  if (tab === "Data Model") {
    const list = searched(ENTITIES.map((e) => ({ key: e.join(" "), e })), ["key"], q);
    const pg = paginate(list, page, a.per);
    const rows: TableRow[] = pg.slice.map((x) => ({
      key: x.e[0],
      go: () => a.toast(x.e[0] + " — schema detail"),
      style: "cursor:pointer;background:" + (x.e[4] === "built" ? "#F6FBF8" : "#fff"),
      cells: [
        cell(x.e[0], { bold: true, mono: true }), cell(x.e[1], { mono: true }), cell(x.e[2], { mute: true }),
        cell(x.e[3], { mono: true, mute: true, w: 340 }),
        cell(x.e[4], { tone: x.e[4] === "built" ? "green" : x.e[4] === "partial" ? "amber" : "gray" }),
        cell(x.e[5], { mute: true, w: 260 }),
      ],
    }));
    return {
      title: "Relational Data Model",
      meta: "โครง Azure SQL ตามสถาปัตยกรรม · สถานะบอกว่าตารางไหนมีจริงแล้ว ตารางไหนยังเป็นแบบบนกระดาษ",
      cols: cols([["Entity"], ["Primary Key"], ["Related to"], ["Key Attributes"], ["Status"], ["Notes"]], a.onSort),
      rows, total: pg.total, pageCount: pg.pageCount, page: pg.p, per: pg.per,
    };
  }

  if (tab === "Audit Log") {
    const logs = [];
    for (let i = 0; i < 20; i++) {
      logs.push({
        ts: "11 Aug 2026 " + pad(16 - Math.floor(i / 2)) + ":" + pad((i * 7) % 60),
        user: ["K. Wanida", "K. Somsak", "N. Pichai", "K. Nattaya", "system"][i % 5],
        role: ["Coordinator", "Supervisor", "Manager", "Billing", "System"][i % 5],
        act: ["UPDATE", "CREATE", "DELETE", "APPROVE", "EXPORT"][i % 5],
        ent: ["Shipment", "Rate", "CAR/PAR", "Supplier", "Report"][i % 5],
        rec: "ABS" + (26000 + i * 7),
        det: "Status changed to In Transit",
        ip: "10.20." + (i % 8) + "." + (11 + i),
      });
    }
    const list = searched(logs, ["user", "act", "ent", "rec"], q);
    const pg = paginate(list, page, a.per);
    const rows: TableRow[] = pg.slice.map((l, i) => ({
      key: l.rec + i,
      style: "background:#fff",
      cells: [
        cell(l.ts, { mono: true }), cell(l.user, { bold: true }), cell(l.role, { mute: true }),
        cell(l.act, { tone: l.act === "DELETE" ? "red" : l.act === "APPROVE" ? "green" : "blue" }),
        cell(l.ent), cell(l.rec, { mono: true }), cell(l.det, { mute: true }), cell(l.ip, { mono: true, mute: true }),
      ],
    }));
    return {
      title: "Audit Log", meta: pg.total + " entries · immutable record of controlled changes",
      cols: cols([["Timestamp"], ["User"], ["Role"], ["Action"], ["Entity"], ["Record"], ["Detail"], ["IP Address"]], a.onSort),
      rows, total: pg.total, pageCount: pg.pageCount, page: pg.p, per: pg.per,
    };
  }

  if (tab === "Users") {
    const us: string[][] = [
      ["Nattapong P.", "Subcontract Mgmt Manager", "nattapong.p", "Active"],
      ["Wanida S.", "Coordinator", "wanida.s", "Active"],
      ["Somsak R.", "Supervisor", "somsak.r", "Active"],
      ["Nattaya K.", "Billing", "nattaya.k", "Active"],
      ["Chaiwat P.", "Assistant Manager", "chaiwat.p", "Active"],
      ["Pornthip L.", "Customer Service", "pornthip.l", "Active"],
      ["Anan T.", "Key Account", "anan.t", "Locked"],
      ["Duangjai M.", "Read Only", "duangjai.m", "Active"],
      ["IT Admin", "Administrator", "it.admin", "Active"],
      ["Management View", "Management", "mgmt.view", "Active"],
    ];
    const list = searched(us.map((u) => ({ key: u.join(" "), u })), ["key"], q);
    const pg = paginate(list, page, a.per);
    const rows: TableRow[] = pg.slice.map((r) => ({
      key: r.u[2],
      go: () => a.toast("Edit user " + r.u[0]),
      style: "cursor:pointer;background:#fff",
      cells: [
        cell(r.u[0], { bold: true }), cell(r.u[1]), cell(r.u[2], { mono: true }),
        cell("Subcontract Management", { mute: true }), cell("11 Aug 2026 08:14", { mono: true, mute: true }),
        cell("Enabled", { tone: "green" }), cell(r.u[3], { tone: r.u[3] === "Active" ? "green" : "red" }),
      ],
    }));
    return {
      title: "Users", meta: pg.total + " accounts",
      cols: cols([["Name"], ["Role"], ["Username"], ["Department"], ["Last Login"], ["MFA"], ["Status"]], a.onSort),
      rows, total: pg.total, pageCount: pg.pageCount, page: pg.p, per: pg.per,
    };
  }

  const roles = ["Administrator", "Subcontract Mgmt Manager", "Assistant Manager", "Supervisor", "Coordinator", "Billing", "Customer Service", "Key Account", "Management", "Read Only"];
  const perms = ["View", "Create", "Edit", "Delete", "Approve", "Export", "Administer"];
  const matrix: Record<string, number[]> = {
    Administrator: [1, 1, 1, 1, 1, 1, 1],
    "Subcontract Mgmt Manager": [1, 1, 1, 1, 1, 1, 0],
    "Assistant Manager": [1, 1, 1, 0, 1, 1, 0],
    Supervisor: [1, 1, 1, 0, 0, 1, 0],
    Coordinator: [1, 1, 1, 0, 0, 1, 0],
    Billing: [1, 1, 1, 0, 0, 1, 0],
    "Customer Service": [1, 0, 0, 0, 0, 1, 0],
    "Key Account": [1, 0, 0, 0, 0, 1, 0],
    Management: [1, 0, 0, 0, 1, 1, 0],
    "Read Only": [1, 0, 0, 0, 0, 0, 0],
  };
  const list = searched(roles.map((r) => ({ key: r, r })), ["key"], q);
  const pg = paginate(list, page, a.per);
  const rows: TableRow[] = pg.slice.map((x) => ({
    key: x.r,
    go: () => a.toast("Edit permissions — " + x.r),
    style: "cursor:pointer;background:#fff",
    cells: [cell(x.r, { bold: true })].concat(
      matrix[x.r].map((v) => cell(v ? "●" : "—", { align: "center", color: v ? "#16794C" : null, mute: !v })),
    ),
  }));
  return {
    title: "Roles & Permissions", meta: "10 roles × 7 permissions · changes are written to the audit log",
    cols: cols(([["Role"]] as [string, ("left" | "right" | "center")?][]).concat(
      perms.map((p) => [p, "center"] as [string, "center"]),
    ), a.onSort),
    rows, total: pg.total, pageCount: pg.pageCount, page: pg.p, per: pg.per,
  };
}
